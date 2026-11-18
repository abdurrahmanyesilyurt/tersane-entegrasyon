using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tersane.Data;
using Tersane.Data.Entities;
using Tersane.Worker.Erp;

namespace Tersane.Worker.Sync;

public sealed record SyncRunResult(string Feed, bool Success, int Fetched, int Changed, string? Error);

/// <summary>
/// Pulls changes from the ERP (delta by ChangedAt watermark) into the internal database:
/// part catalogue (OPENJSON + MERGE), stock (table-valued parameter + MERGE) and requisition states.
/// Each feed re-reads a small overlap window before its watermark; the procedures are idempotent,
/// so re-reading a row is harmless and a change landing at the boundary is never missed.
/// </summary>
public sealed class ErpSyncRunner(
    IDbContextFactory<TersaneDbContext> dbFactory,
    ErpClient erp,
    IOptions<IntegrationOptions> options,
    TimeProvider clock,
    ILogger<ErpSyncRunner> logger)
{
    public const string PartsFeed = "ErpParts";
    public const string StockFeed = "ErpStock";
    public const string RequisitionFeed = "ErpRequisitionStates";

    public async Task<IReadOnlyList<SyncRunResult>> RunOnceAsync(CancellationToken ct)
    {
        // Order matters: parts first, so stock rows always find their material.
        return
        [
            await RunFeedAsync(PartsFeed, SyncPartsAsync, ct),
            await RunFeedAsync(StockFeed, SyncStockAsync, ct),
            await RunFeedAsync(RequisitionFeed, SyncRequisitionStatesAsync, ct),
        ];
    }

    private async Task<(int Fetched, int Changed, DateTime? NewWatermark)> SyncPartsAsync(DateTime since, TersaneSql sql, CancellationToken ct)
    {
        var (json, max, count) = await erp.GetChangedSinceAsJsonAsync(ErpClient.Parts, since, null, Ctx("Sync.Parts"), ct);
        if (count == 0) return (0, 0, null);
        var result = await sql.UpsertMaterialsFromErpJsonAsync(json, ct);
        return (count, result.InsertedCount + result.UpdatedCount, max?.UtcDateTime);
    }

    private async Task<(int Fetched, int Changed, DateTime? NewWatermark)> SyncStockAsync(DateTime since, TersaneSql sql, CancellationToken ct)
    {
        var rows = await erp.GetChangedSinceAsync<ErpStock>(ErpClient.Stock, since, null, Ctx("Sync.Stock"), ct);
        if (rows.Count == 0) return (0, 0, null);
        var result = await sql.UpsertStockAsync(
            rows.Select(r => new StockRow(r.PartNo, r.Contract, r.LocationNo, r.QtyOnhand, r.QtyReserved, r.ChangedAt.UtcDateTime)), ct);
        if (result.UnknownPartCount > 0)
            logger.LogWarning("Stok senkronu: {Count} satırın malzemesi iç sistemde yok.", result.UnknownPartCount);
        return (rows.Count, result.InsertedCount + result.UpdatedCount, rows.Max(r => r.ChangedAt).UtcDateTime);
    }

    private async Task<(int Fetched, int Changed, DateTime? NewWatermark)> SyncRequisitionStatesAsync(DateTime since, TersaneSql sql, CancellationToken ct)
    {
        var (json, max, count) = await erp.GetChangedSinceAsJsonAsync(ErpClient.Requisitions, since,
            "RequisitionNo,ExternalRef,Objstate,ChangedAt", Ctx("Sync.RequisitionStates"), ct);
        if (count == 0) return (0, 0, null);
        var changed = await sql.ApplyErpRequisitionStatesAsync(json, ct);
        return (count, changed, max?.UtcDateTime);
    }

    private async Task<SyncRunResult> RunFeedAsync(string feed,
        Func<DateTime, TersaneSql, CancellationToken, Task<(int Fetched, int Changed, DateTime? NewWatermark)>> step, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var state = await db.SyncStates.SingleOrDefaultAsync(s => s.Name == feed, ct);
        if (state is null)
        {
            state = new SyncState { Name = feed, Watermark = DateTime.UnixEpoch };
            db.SyncStates.Add(state);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        state.LastRunAt = now;
        var since = state.Watermark == DateTime.UnixEpoch ? state.Watermark : state.Watermark.AddSeconds(-options.Value.SyncOverlapSeconds);

        try
        {
            var (fetched, changed, watermark) = await step(since, new TersaneSql(db), ct);
            if (watermark > state.Watermark) state.Watermark = watermark.Value;
            state.LastSuccessAt = now;
            state.LastError = null;
            state.LastRowCount = changed;

            if (changed > 0)
            {
                db.IntegrationLogs.Add(new IntegrationLog
                {
                    OccurredAt = now, Direction = "IN", Operation = "Sync." + feed, Level = "Info",
                    Message = $"{feed}: ERP'den {fetched} satır okundu, {changed} kayıt güncellendi.",
                });
                logger.LogInformation("{Feed}: {Fetched} okundu, {Changed} güncellendi.", feed, fetched, changed);
            }
            await db.SaveChangesAsync(ct);
            return new SyncRunResult(feed, true, fetched, changed, null);
        }
        catch (ErpException ex)
        {
            // Sync is naturally retried on the next tick; just record the error.
            db.ChangeTracker.Clear();
            await db.SyncStates.Where(s => s.Name == feed)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastRunAt, now).SetProperty(x => x.LastError, ex.Message), ct);
            logger.LogWarning("{Feed} senkronu başarısız: {Error}", feed, ex.Message);
            return new SyncRunResult(feed, false, 0, 0, ex.Message);
        }
    }

    private static ErpCallContext Ctx(string operation) => new(operation, AlwaysLog: false);
}
