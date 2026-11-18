using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Tersane.Data;
using Tersane.Data.Entities;

namespace Tersane.Tests;

/// <summary>T-SQL behaviour against a real SQL Server: procedures, view, TVF, TVP + MERGE, outbox claim.</summary>
public sealed class DatabaseTests(SqlServer sql) : IAsyncLifetime
{
    private string _cs = "";

    public async ValueTask InitializeAsync()
    {
        _cs = await sql.CreateDatabaseAsync("dbtests");
        await using var db = SqlServer.CreateContext(_cs);
        await TestData.SeedMaterialsAsync(db);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private TersaneDbContext Db() => SqlServer.CreateContext(_cs);

    [Fact]
    public async Task Migration_creates_the_programmability_objects()
    {
        await using var db = Db();
        var names = await db.Database.SqlQuery<string>($"""
            SELECT SCHEMA_NAME(schema_id) + '.' + name AS Value FROM sys.objects
            WHERE type IN ('P', 'V', 'IF') AND is_ms_shipped = 0
            """).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Superset(new HashSet<string>
        {
            "dbo.usp_MaterialRequest_Submit", "dbo.usp_MaterialRequest_ApplyErpStates", "dbo.usp_StockBalance_Upsert",
            "dbo.usp_Material_UpsertFromErp", "integration.usp_Outbox_Claim", "integration.usp_Outbox_Requeue",
            "integration.usp_IntegrationLog_Purge", "dbo.vw_StockStatus", "integration.vw_IntegrationHealth",
            "dbo.fn_ProjectMaterialSummary",
        }, names.ToHashSet());
    }

    [Fact]
    public async Task Request_number_comes_from_a_sequence_and_a_persisted_computed_column()
    {
        await using var db = Db();
        var first = await TestData.CreateDraftAsync(db, ("ST-AH36-08", 5));
        var second = await TestData.CreateDraftAsync(db, ("ST-AH36-08", 5));

        Assert.Matches(@"^MT-\d{4}-\d{5}$", first.No);
        Assert.Equal(first.SeqNo + 1, second.SeqNo);
    }

    [Fact]
    public async Task Submit_changes_status_and_writes_the_outbox_message_in_one_transaction()
    {
        await using var db = Db();
        var request = await TestData.CreateDraftAsync(db, ("ST-AH36-08", 12), ("PR-HP160-9", 120.5m));

        var result = await new TersaneSql(db).SubmitMaterialRequestAsync(request.Id, "test.user", "corr-1");

        Assert.False(result.AlreadyQueued);
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.MessageId == result.MessageId);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal($"MaterialRequest.Submit:{request.No}", message.IdempotencyKey);
        Assert.Contains("\"partNo\":\"PR-HP160-9\"", message.Payload);  // built with FOR JSON PATH
        Assert.Contains("\"quantity\":120.500", message.Payload);
        var status = await db.MaterialRequests.AsNoTracking().Where(r => r.Id == request.Id).Select(r => r.Status).SingleAsync();
        Assert.Equal(MaterialRequestStatus.Submitted, status);
    }

    [Fact]
    public async Task Submitting_twice_returns_the_same_message_instead_of_a_duplicate()
    {
        await using var db = Db();
        var request = await TestData.CreateDraftAsync(db, ("ST-AH36-08", 1));
        var proc = new TersaneSql(db);

        var first = await proc.SubmitMaterialRequestAsync(request.Id, "a", null);
        var second = await proc.SubmitMaterialRequestAsync(request.Id, "a", null);

        Assert.True(second.AlreadyQueued);
        Assert.Equal(first.MessageId, second.MessageId);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.AggregateId == request.Id.ToString()));
    }

    [Fact]
    public async Task Submit_without_lines_is_a_business_error_and_changes_nothing()
    {
        await using var db = Db();
        var request = await TestData.CreateDraftAsync(db);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => new TersaneSql(db).SubmitMaterialRequestAsync(request.Id, "a", null));

        Assert.Equal(50422, ex.Code);
        await using var check = Db();
        Assert.Equal(MaterialRequestStatus.Draft, (await check.MaterialRequests.SingleAsync(r => r.Id == request.Id)).Status);
        Assert.False(await check.OutboxMessages.AnyAsync(m => m.AggregateId == request.Id.ToString()));
    }

    [Fact]
    public async Task Parallel_workers_never_claim_the_same_message()
    {
        await using (var db = Db())
        {
            for (var i = 0; i < 60; i++)
                db.OutboxMessages.Add(NewMessage($"claim-test-{i}"));
            await db.SaveChangesAsync();
        }

        var claimed = new ConcurrentBag<Guid>();
        await Parallel.ForEachAsync(Enumerable.Range(1, 6), async (worker, ct) =>
        {
            await using var db = Db();
            while (true)
            {
                var batch = await new TersaneSql(db).ClaimOutboxAsync($"worker-{worker}", 4, 60, ct);
                var mine = batch.Where(m => m.IdempotencyKey.StartsWith("claim-test-", StringComparison.Ordinal)).ToList();
                if (batch.Count == 0) break;
                foreach (var m in mine) claimed.Add(m.MessageId);
            }
        });

        Assert.Equal(60, claimed.Count);
        Assert.Equal(60, claimed.Distinct().Count());
    }

    [Fact]
    public async Task A_message_whose_lease_expired_is_claimed_again()
    {
        await using var db = Db();
        var message = NewMessage("lease-test");
        message.Status = OutboxStatus.Processing;
        message.AttemptCount = 1; // the crashed worker had claimed it once
        message.LockedBy = "crashed-worker";
        message.LockedUntil = DateTime.UtcNow.AddMinutes(-1);
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();

        var batch = await new TersaneSql(db).ClaimOutboxAsync("rescuer", 50, 60);

        var reclaimed = Assert.Single(batch, m => m.MessageId == message.MessageId);
        Assert.Equal(2, reclaimed.AttemptCount);
    }

    [Fact]
    public async Task Requeue_puts_a_failed_message_back_and_logs_who_did_it()
    {
        await using var db = Db();
        var request = await TestData.CreateDraftAsync(db, ("ST-AH36-08", 3));
        var submitted = await new TersaneSql(db).SubmitMaterialRequestAsync(request.Id, "a", null);
        await db.OutboxMessages.Where(m => m.MessageId == submitted.MessageId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Failed).SetProperty(m => m.AttemptCount, 5).SetProperty(m => m.LastError, "ERP 503"));
        await db.MaterialRequests.Where(r => r.Id == request.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, MaterialRequestStatus.Failed));

        var count = await new TersaneSql(db).RequeueAsync(submitted.MessageId, "operator.ali");

        Assert.Equal(1, count);
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.MessageId == submitted.MessageId);
        Assert.Equal((OutboxStatus.Pending, 0), (message.Status, message.AttemptCount));
        Assert.Equal(MaterialRequestStatus.Submitted, (await db.MaterialRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);
        Assert.True(await db.IntegrationLogs.AnyAsync(l => l.MessageId == submitted.MessageId && l.Operation == "Outbox.Requeue" && l.Message.Contains("operator.ali")));
        Assert.Equal(0, await new TersaneSql(db).RequeueAsync(submitted.MessageId, "operator.ali")); // not Failed any more
    }

    [Fact]
    public async Task Stock_upsert_with_a_table_valued_parameter_is_idempotent()
    {
        await using var db = Db();
        var proc = new TersaneSql(db);
        var stamp = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
        StockRow[] rows =
        [
            new("ST-AH36-08", "TRS1", "SAC-SAHA", 64, 12, stamp),
            new("PR-HP160-9", "TRS1", "PROFIL-01", 820, 140, stamp),
            new("UNKNOWN-1", "TRS1", "A-01", 1, 0, stamp),
        ];

        var first = await proc.UpsertStockAsync(rows);
        var replay = await proc.UpsertStockAsync(rows);
        var change = await proc.UpsertStockAsync([rows[0] with { QtyOnHand = 50, ErpChangedAt = stamp.AddMinutes(1) }]);

        Assert.Equal(new UpsertResult(2, 0, 1), first);
        Assert.Equal(new UpsertResult(0, 0, 1), replay);
        Assert.Equal(new UpsertResult(0, 1, 0), change);
    }

    [Fact]
    public async Task Stock_view_shows_available_quantity_and_what_is_on_order()
    {
        await using var db = Db();
        await new TersaneSql(db).UpsertStockAsync([new("PR-HP160-9", "TRS1", "PROFIL-01", 300, 100, DateTime.UtcNow)]);
        var request = await TestData.CreateDraftAsync(db, ("PR-HP160-9", 75));
        await new TersaneSql(db).SubmitMaterialRequestAsync(request.Id, "a", null);

        var row = await db.StockStatus.AsNoTracking().SingleAsync(s => s.PartNo == "PR-HP160-9");

        Assert.Equal(200, row.QtyAvailable);   // 300 on hand − 100 reserved
        Assert.True(row.QtyOnOrder >= 75);
        Assert.Equal("LOW", row.StockLevel);    // below the safety stock of 300
    }

    [Fact]
    public async Task Erp_states_only_move_a_request_forward()
    {
        await using var db = Db();
        var request = await TestData.CreateDraftAsync(db, ("ST-AH36-08", 2));
        await new TersaneSql(db).SubmitMaterialRequestAsync(request.Id, "a", null);
        var proc = new TersaneSql(db);

        var closed = await proc.ApplyErpRequisitionStatesAsync($$"""[{"RequisitionNo":"PR26-00077","ExternalRef":"{{request.No}}","Objstate":"Closed"}]""");
        var stale = await proc.ApplyErpRequisitionStatesAsync($$"""[{"RequisitionNo":"PR26-00077","ExternalRef":"{{request.No}}","Objstate":"Released"}]""");

        Assert.Equal((1, 0), (closed, stale));
        var saved = await db.MaterialRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id);
        Assert.Equal((MaterialRequestStatus.Received, "PR26-00077"), (saved.Status, saved.ErpRequisitionNo));
    }

    [Fact]
    public async Task Part_catalogue_upsert_from_json_marks_obsolete_parts_as_blocked()
    {
        await using var db = Db();
        var blocked = await db.Materials.AsNoTracking().SingleAsync(m => m.PartNo == "WL-E6013-32");
        Assert.True(blocked.IsBlockedInErp);

        // An older change (same timestamp) must not overwrite; a newer one must.
        var result = await new TersaneSql(db).UpsertMaterialsFromErpJsonAsync("""
            [{"PartNo":"WL-E6013-32","Description":"Eski elektrot","UnitMeas":"KG","PartCategory":"Kaynak sarf","SafetyStock":0,"PartStatus":"A","ChangedAt":"2026-11-01T08:00:00Z"},
             {"PartNo":"ST-AH36-08","Description":"Sac 8 mm (rev)","UnitMeas":"AD","PartCategory":"Sac","SafetyStock":45,"PartStatus":"A","ChangedAt":"2026-11-02T08:00:00+03:00"}]
            """);

        Assert.Equal(1, result.UpdatedCount);
        var sheet = await db.Materials.AsNoTracking().SingleAsync(m => m.PartNo == "ST-AH36-08");
        Assert.Equal(new DateTime(2026, 11, 2, 5, 0, 0), sheet.ErpChangedAt); // +03:00 normalised to UTC
    }

    [Fact]
    public async Task Project_material_summary_tvf_is_composable_from_linq()
    {
        await using var db = Db();
        var request = await TestData.CreateDraftAsync(db, ("ST-AH36-08", 7), ("PR-HP160-9", 30));
        await new TersaneSql(db).SubmitMaterialRequestAsync(request.Id, "a", null);

        var rows = await db.ProjectMaterialSummary(request.ProjectId).Where(r => r.PartNo == "ST-AH36-08").ToListAsync();

        var row = Assert.Single(rows);
        Assert.True(row.QtyOpen >= 7);
    }

    private static OutboxMessage NewMessage(string key) => new()
    {
        MessageId = Guid.NewGuid(),
        MessageType = "Test",
        AggregateType = "Test",
        AggregateId = key,
        IdempotencyKey = key,
        Payload = "{}",
        Status = OutboxStatus.Pending,
        MaxAttempts = 5,
        NextAttemptAt = DateTime.UtcNow.AddSeconds(-1),
        CreatedAt = DateTime.UtcNow,
    };
}
