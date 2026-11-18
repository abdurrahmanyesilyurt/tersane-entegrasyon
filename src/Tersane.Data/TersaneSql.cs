using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Tersane.Data;

public sealed record SubmitResult(Guid MessageId, bool AlreadyQueued);

public sealed record ClaimedMessage(
    long Id,
    Guid MessageId,
    string MessageType,
    string AggregateType,
    string AggregateId,
    string IdempotencyKey,
    string Payload,
    int AttemptCount,
    int MaxAttempts,
    string? CorrelationId);

public sealed record RequeueResult(int RequeuedCount);

public sealed record StateSyncResult(int UpdatedCount);

public sealed record UpsertResult(int InsertedCount, int UpdatedCount, int UnknownPartCount);

public sealed record StockRow(string PartNo, string Site, string LocationNo, decimal QtyOnHand, decimal QtyReserved, DateTime ErpChangedAt);

/// <summary>A business rule raised by a stored procedure with THROW 50xxx.</summary>
public sealed class BusinessRuleException(int code, string message, Exception inner) : Exception(message, inner)
{
    public int Code { get; } = code;
}

/// <summary>
/// Typed wrappers around the T-SQL stored procedures. EF Core is used for the ordinary CRUD;
/// set-based and concurrency-sensitive work (outbox claim, MERGE upserts, submit) lives in T-SQL.
/// </summary>
public sealed class TersaneSql(TersaneDbContext db)
{
    public async Task<SubmitResult> SubmitMaterialRequestAsync(int id, string user, string? correlationId, CancellationToken ct = default)
    {
        try
        {
            var rows = await db.Database
                .SqlQuery<SubmitResult>($"EXEC dbo.usp_MaterialRequest_Submit @MaterialRequestId = {id}, @SubmittedBy = {user}, @CorrelationId = {correlationId}")
                .ToListAsync(ct);
            return rows.Single();
        }
        catch (SqlException ex) when (ex.Number is >= 50000 and < 51000)
        {
            throw new BusinessRuleException(ex.Number, ex.Message, ex);
        }
    }

    public async Task<IReadOnlyList<ClaimedMessage>> ClaimOutboxAsync(string workerId, int batchSize, int leaseSeconds, CancellationToken ct = default)
        => await db.Database
            .SqlQuery<ClaimedMessage>($"EXEC integration.usp_Outbox_Claim @WorkerId = {workerId}, @BatchSize = {batchSize}, @LeaseSeconds = {leaseSeconds}")
            .ToListAsync(ct);

    public async Task<int> RequeueAsync(Guid? messageId, string requestedBy, CancellationToken ct = default)
    {
        var rows = await db.Database
            .SqlQuery<RequeueResult>($"EXEC integration.usp_Outbox_Requeue @MessageId = {messageId}, @RequestedBy = {requestedBy}")
            .ToListAsync(ct);
        return rows.Single().RequeuedCount;
    }

    public async Task<UpsertResult> UpsertStockAsync(IEnumerable<StockRow> rows, CancellationToken ct = default)
    {
        var table = new DataTable();
        table.Columns.Add("PartNo", typeof(string));
        table.Columns.Add("Site", typeof(string));
        table.Columns.Add("LocationNo", typeof(string));
        table.Columns.Add("QtyOnHand", typeof(decimal));
        table.Columns.Add("QtyReserved", typeof(decimal));
        table.Columns.Add("ErpChangedAt", typeof(DateTime));
        foreach (var r in rows)
            table.Rows.Add(r.PartNo, r.Site, r.LocationNo, r.QtyOnHand, r.QtyReserved, r.ErpChangedAt);

        var parameter = new SqlParameter("@Rows", SqlDbType.Structured) { TypeName = "dbo.StockBalanceRow", Value = table };
        var result = await db.Database
            .SqlQueryRaw<UpsertResult>("EXEC dbo.usp_StockBalance_Upsert @Rows", parameter)
            .ToListAsync(ct);
        return result.Single();
    }

    public async Task<UpsertResult> UpsertMaterialsFromErpJsonAsync(string json, CancellationToken ct = default)
    {
        var result = await db.Database
            .SqlQuery<UpsertResult>($"EXEC dbo.usp_Material_UpsertFromErp @Json = {json}")
            .ToListAsync(ct);
        return result.Single();
    }

    public async Task<int> ApplyErpRequisitionStatesAsync(string json, CancellationToken ct = default)
    {
        var result = await db.Database
            .SqlQuery<StateSyncResult>($"EXEC dbo.usp_MaterialRequest_ApplyErpStates @Json = {json}")
            .ToListAsync(ct);
        return result.Single().UpdatedCount;
    }
}
