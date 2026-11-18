using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tersane.Data;
using Tersane.Data.Entities;
using Tersane.Worker.Erp;

namespace Tersane.Worker.Outbox;

public sealed record BatchResult(int Claimed, int Succeeded, int Retrying, int Failed);

/// <summary>
/// Claims outbox messages and delivers them to the ERP.
///
/// Two retry levels:
///  1. Polly on the HttpClient: a few quick retries with exponential backoff + jitter (seconds).
///  2. The outbox itself: if Polly gives up, the message waits (backoff in minutes) and is tried again,
///     up to MaxAttempts. Then it is marked Failed and an operator can re-queue it from the UI.
/// Permanent errors (ERP rejected the data) skip level 2 and go straight to Failed.
/// </summary>
public sealed class OutboxProcessor(
    IDbContextFactory<TersaneDbContext> dbFactory,
    ErpClient erp,
    IOptions<IntegrationOptions> options,
    TimeProvider clock,
    ILogger<OutboxProcessor> logger)
{
    public string WorkerId { get; } = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid().ToString("N")[..6]}";

    public async Task<BatchResult> ProcessBatchAsync(CancellationToken ct)
    {
        var o = options.Value;
        IReadOnlyList<ClaimedMessage> batch;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            batch = await new TersaneSql(db).ClaimOutboxAsync(WorkerId, o.OutboxBatchSize, o.OutboxLeaseSeconds, ct);

        int ok = 0, retrying = 0, failed = 0;
        foreach (var message in batch)
        {
            try
            {
                var externalRef = message.MessageType switch
                {
                    "MaterialRequest.Submitted" => await SendMaterialRequestAsync(message, ct),
                    _ => throw new ErpException(ErpFailureKind.Permanent, $"Bilinmeyen mesaj tipi: {message.MessageType}"),
                };
                await MarkProcessedAsync(message, externalRef, ct);
                ok++;
            }
            catch (ErpException ex) when (ex.Kind == ErpFailureKind.Transient && message.AttemptCount < message.MaxAttempts)
            {
                var delay = Backoff(message.AttemptCount, o);
                await MarkRetryAsync(message, ex.Message, delay, ct);
                logger.LogWarning("{MessageId} geçici hata, {Delay} sonra tekrar denenecek ({Attempt}/{Max}): {Error}",
                    message.MessageId, delay, message.AttemptCount, message.MaxAttempts, ex.Message);
                retrying++;
            }
            catch (Exception ex) when (ex is ErpException or JsonException)
            {
                var permanent = ex is not ErpException { Kind: ErpFailureKind.Transient };
                var reason = permanent ? ex.Message : $"{ex.Message} (deneme hakkı bitti: {message.AttemptCount}/{message.MaxAttempts})";
                await MarkFailedAsync(message, reason, ct);
                logger.LogError("{MessageId} başarısız ({Kind}): {Error}", message.MessageId, permanent ? "kalıcı" : "geçici", reason);
                failed++;
            }
        }

        return new BatchResult(batch.Count, ok, retrying, failed);
    }

    /// <summary>
    /// Idempotent delivery: first ask the ERP whether a requisition with our business key already exists
    /// (the worker may have crashed after the ERP created it but before we recorded it); 409 is treated the same way.
    /// </summary>
    private async Task<string> SendMaterialRequestAsync(ClaimedMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<MaterialRequestPayload>(message.Payload, ErpClient.Json)
            ?? throw new JsonException("Boş mesaj gövdesi.");
        var context = new ErpCallContext("Requisition.Create", message.MessageId, message.CorrelationId);

        var existing = await erp.FindRequisitionByExternalRefAsync(payload.RequestNo, context with { Operation = "Requisition.Lookup" }, ct);
        if (existing is not null)
        {
            await LogAsync(message, "Info", $"{payload.RequestNo} ERP'de zaten var ({existing.RequisitionNo}); tekrar oluşturulmadı (idempotent).", ct);
            return existing.RequisitionNo;
        }

        var activity = string.Join(" / ", new[] { payload.BlockCode, payload.WorkOrderNo }.Where(s => !string.IsNullOrEmpty(s)));
        var body = new ErpRequisitionCreate(
            ExternalRef: payload.RequestNo,
            ProjectId: payload.HullNo,
            RequisitionerCode: payload.RequestedBy,
            Note: payload.Note,
            Lines: payload.Lines.Select(l => new ErpRequisitionLine(
                l.LineNo, l.PartNo, l.Quantity, l.Unit,
                new DateTimeOffset(payload.NeedDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                activity.Length == 0 ? null : activity, l.Note)).ToList());

        try
        {
            var created = await erp.CreateRequisitionAsync(body, context, ct);
            return created.RequisitionNo;
        }
        catch (ErpException ex) when (ex.Kind == ErpFailureKind.Conflict)
        {
            var again = await erp.FindRequisitionByExternalRefAsync(payload.RequestNo, context with { Operation = "Requisition.Lookup" }, ct)
                ?? throw new ErpException(ErpFailureKind.Transient, "ERP 409 döndü ama kayıt bulunamadı.", ex.Status, ex.ErrorCode, ex);
            return again.RequisitionNo;
        }
    }

    private async Task MarkProcessedAsync(ClaimedMessage message, string externalRef, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;

        // Fencing: only the worker that still holds the lease may complete the message.
        var updated = await db.OutboxMessages
            .Where(m => m.Id == message.Id && m.LockedBy == WorkerId && m.Status == OutboxStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxStatus.Processed)
                .SetProperty(m => m.ProcessedAt, now)
                .SetProperty(m => m.ExternalRef, externalRef)
                .SetProperty(m => m.LastError, (string?)null)
                .SetProperty(m => m.LockedUntil, (DateTime?)null), ct);

        if (updated == 0)
        {
            logger.LogWarning("{MessageId}: kilit başka bir worker'a geçmiş, tamamlama atlandı.", message.MessageId);
            return;
        }

        if (message.AggregateType == "MaterialRequest" && int.TryParse(message.AggregateId, out var requestId))
        {
            await db.MaterialRequests
                .Where(r => r.Id == requestId && (r.Status == MaterialRequestStatus.Submitted || r.Status == MaterialRequestStatus.Failed))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, MaterialRequestStatus.SentToErp)
                    .SetProperty(r => r.ErpRequisitionNo, externalRef)
                    .SetProperty(r => r.ErpState, "Released")
                    .SetProperty(r => r.UpdatedAt, now), ct);
        }

        db.IntegrationLogs.Add(Log(message, "Info", $"Mesaj işlendi → ERP satınalma talebi {externalRef}."));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task MarkRetryAsync(ClaimedMessage message, string error, TimeSpan delay, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var next = clock.GetUtcNow().UtcDateTime + delay;
        await db.OutboxMessages
            .Where(m => m.Id == message.Id && m.LockedBy == WorkerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxStatus.Pending)
                .SetProperty(m => m.NextAttemptAt, next)
                .SetProperty(m => m.LastError, Truncate(error))
                .SetProperty(m => m.LockedBy, (string?)null)
                .SetProperty(m => m.LockedUntil, (DateTime?)null), ct);
        db.IntegrationLogs.Add(Log(message, "Warning", $"Geçici hata, {delay.TotalSeconds:0} sn sonra tekrar denenecek (deneme {message.AttemptCount}/{message.MaxAttempts}): {error}"));
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkFailedAsync(ClaimedMessage message, string error, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.OutboxMessages
            .Where(m => m.Id == message.Id && m.LockedBy == WorkerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxStatus.Failed)
                .SetProperty(m => m.LastError, Truncate(error))
                .SetProperty(m => m.LockedUntil, (DateTime?)null), ct);

        if (message.AggregateType == "MaterialRequest" && int.TryParse(message.AggregateId, out var requestId))
        {
            await db.MaterialRequests
                .Where(r => r.Id == requestId && r.Status == MaterialRequestStatus.Submitted)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, MaterialRequestStatus.Failed)
                                          .SetProperty(r => r.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);
        }

        db.IntegrationLogs.Add(Log(message, "Error", $"Mesaj HATALI olarak işaretlendi, arayüzden yeniden işlenebilir: {error}"));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task LogAsync(ClaimedMessage message, string level, string text, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.IntegrationLogs.Add(Log(message, level, text));
        await db.SaveChangesAsync(ct);
    }

    private IntegrationLog Log(ClaimedMessage message, string level, string text) => new()
    {
        OccurredAt = clock.GetUtcNow().UtcDateTime,
        Direction = "SYS",
        Operation = "Outbox." + message.MessageType,
        Level = level,
        MessageId = message.MessageId,
        CorrelationId = message.CorrelationId,
        Attempt = message.AttemptCount,
        Message = Truncate(text)!,
    };

    internal static TimeSpan Backoff(int attempt, IntegrationOptions o)
    {
        var seconds = o.OutboxBackoffBaseSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        return TimeSpan.FromSeconds(Math.Min(seconds, o.OutboxBackoffMaxSeconds));
    }

    private static string? Truncate(string? s) => s is null || s.Length <= 2000 ? s : s[..2000];
}

/// <summary>Shape of the JSON built by dbo.usp_MaterialRequest_Submit (FOR JSON PATH).</summary>
public sealed record MaterialRequestPayload(
    int MaterialRequestId,
    string RequestNo,
    string HullNo,
    string? BlockCode,
    string? WorkOrderNo,
    string RequestedBy,
    DateOnly NeedDate,
    string? Note,
    List<MaterialRequestPayloadLine> Lines);

public sealed record MaterialRequestPayloadLine(int LineNo, string PartNo, decimal Quantity, string Unit, string? Note);
