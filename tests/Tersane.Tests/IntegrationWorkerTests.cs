using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tersane.Data;
using Tersane.Data.Entities;

namespace Tersane.Tests;

/// <summary>
/// The integration worker end to end: real SQL Server + in-memory mock ERP + the production HTTP pipeline
/// (Polly retry, token handler, audit log). Each test gets its own database and ERP instance.
/// </summary>
public sealed class IntegrationWorkerTests(SqlServer sql)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sync_brings_the_erp_part_catalogue_and_stock_into_sql_server()
    {
        await using var h = await IntegrationHarness.CreateAsync(sql, "sync");
        await using var db = h.Db();

        Assert.Equal(16, await db.Materials.CountAsync(Ct));
        Assert.True((await db.Materials.SingleAsync(m => m.PartNo == "WL-E6013-32", Ct)).IsBlockedInErp);
        var sheet = await db.StockStatus.SingleAsync(s => s.PartNo == "ST-AH36-08", Ct);
        Assert.Equal((64m, 12m, 52m), (sheet.QtyOnHand, sheet.QtyReservedInErp, sheet.QtyAvailable));

        var second = await h.Sync.RunOnceAsync(Ct); // nothing changed in the ERP → nothing written
        Assert.All(second, r => Assert.Equal(0, r.Changed));
    }

    [Fact]
    public async Task Submitted_request_reaches_the_erp_and_the_requisition_number_comes_back()
    {
        await using var h = await IntegrationHarness.CreateAsync(sql, "deliver");
        var request = await SubmitAsync(h, ("ST-AH36-08", 18), ("PR-HP160-9", 120.5m));

        var result = await h.Outbox.ProcessBatchAsync(Ct);

        Assert.Equal(1, result.Succeeded);
        await using var db = h.Db();
        var saved = await db.MaterialRequests.SingleAsync(r => r.Id == request.Id, Ct);
        Assert.Equal(MaterialRequestStatus.SentToErp, saved.Status);
        Assert.Matches(@"^PR\d{2}-\d{5}$", saved.ErpRequisitionNo);
        var erp = await ErpRequisitionsAsync(h, request.No);
        var lines = Assert.Single(erp).GetProperty("Lines");
        Assert.Equal(120.5m, lines[1].GetProperty("OriginalQty").GetDecimal());
        Assert.True(await db.IntegrationLogs.AnyAsync(l => l.Operation == "Requisition.Create" && l.HttpStatus == 201, Ct));
    }

    [Fact]
    public async Task Crash_after_the_erp_created_the_document_does_not_create_a_duplicate()
    {
        await using var h = await IntegrationHarness.CreateAsync(sql, "idem");
        var request = await SubmitAsync(h, ("ST-AH36-08", 4));

        // Simulate: a previous run created the requisition in the ERP, then died before recording it.
        using (var client = await h.Erp.AuthorizedClientAsync())
        {
            var created = await client.PostAsJsonAsync("/mock-erp/projection/v1/PurchaseRequisitionHandling.svc/PurchaseRequisitionSet",
                new { ExternalRef = request.No, Lines = new[] { new { PartNo = "ST-AH36-08", OriginalQty = 4 } } }, Ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var result = await h.Outbox.ProcessBatchAsync(Ct);

        Assert.Equal(1, result.Succeeded);
        Assert.Single(await ErpRequisitionsAsync(h, request.No));
        await using var db = h.Db();
        Assert.True(await db.IntegrationLogs.AnyAsync(l => l.Message.Contains("idempotent"), Ct));
    }

    [Fact]
    public async Task Transient_erp_errors_are_absorbed_by_polly_retries()
    {
        await using var h = await IntegrationHarness.CreateAsync(sql, "polly");
        var request = await SubmitAsync(h, ("PR-HP160-9", 10));
        await h.Erp.SetChaosAsync(failNext: 2); // the next two calls get 503

        var result = await h.Outbox.ProcessBatchAsync(Ct);

        Assert.Equal(1, result.Succeeded);
        await using var db = h.Db();
        var attempts = await db.IntegrationLogs.Where(l => l.MessageId != null && l.HttpStatus != null)
            .OrderBy(l => l.Id).Select(l => new { l.HttpStatus, l.Attempt }).ToListAsync(Ct);
        Assert.Equal([503, 503, 200, 201], attempts.Select(a => a.HttpStatus!.Value));
        Assert.Equal(3, attempts[2].Attempt); // the third attempt of the same lookup call succeeded
        Assert.Equal(MaterialRequestStatus.SentToErp, (await db.MaterialRequests.SingleAsync(r => r.Id == request.Id, Ct)).Status);
    }

    [Fact]
    public async Task Long_outage_marks_the_message_failed_and_requeue_from_the_ui_recovers_it()
    {
        await using var h = await IntegrationHarness.CreateAsync(sql, "outage");
        var request = await SubmitAsync(h, ("ST-AH36-08", 6));
        await h.Erp.SetChaosAsync(outage: true);

        for (var i = 0; i < 5; i++)
            await h.Outbox.ProcessBatchAsync(Ct); // backoff is 0 in tests, so each round is the next attempt

        await using (var db = h.Db())
        {
            var message = await db.OutboxMessages.SingleAsync(m => m.AggregateId == request.Id.ToString(), Ct);
            Assert.Equal((OutboxStatus.Failed, 5), (message.Status, message.AttemptCount));
            Assert.Contains("503", message.LastError);
            Assert.Equal(MaterialRequestStatus.Failed, (await db.MaterialRequests.SingleAsync(r => r.Id == request.Id, Ct)).Status);
            Assert.Equal(1, (await db.IntegrationHealth.SingleAsync(Ct)).FailedCount);
        }

        await h.Erp.SetChaosAsync(outage: false);
        await using (var db = h.Db())
        {
            var messageId = (await db.OutboxMessages.SingleAsync(m => m.AggregateId == request.Id.ToString(), Ct)).MessageId;
            Assert.Equal(1, await new TersaneSql(db).RequeueAsync(messageId, "operator", Ct)); // "Yeniden işle" button
        }
        var result = await h.Outbox.ProcessBatchAsync(Ct);

        Assert.Equal(1, result.Succeeded);
        await using var after = h.Db();
        Assert.Equal(MaterialRequestStatus.SentToErp, (await after.MaterialRequests.SingleAsync(r => r.Id == request.Id, Ct)).Status);
    }

    [Fact]
    public async Task Erp_validation_error_fails_immediately_without_retries()
    {
        await using var h = await IntegrationHarness.CreateAsync(sql, "permanent");
        var request = await SubmitAsync(h, ("WL-E6013-32", 5)); // obsolete part: the ERP rejects it

        var result = await h.Outbox.ProcessBatchAsync(Ct);

        Assert.Equal(1, result.Failed);
        await using var db = h.Db();
        var message = await db.OutboxMessages.SingleAsync(m => m.AggregateId == request.Id.ToString(), Ct);
        Assert.Equal((OutboxStatus.Failed, 1), (message.Status, message.AttemptCount));
        Assert.Contains("satınalmaya kapalı", message.LastError);
        Assert.Equal(1, await db.IntegrationLogs.CountAsync(l => l.MessageId == message.MessageId && l.HttpStatus == 400, Ct)); // no Polly retry on 400
    }

    [Fact]
    public async Task Erp_approval_and_goods_receipt_flow_back_into_the_internal_system()
    {
        await using var h = await IntegrationHarness.CreateAsync(sql, "states");
        var request = await SubmitAsync(h, ("VL-BFY-DN100", 10));
        await h.Outbox.ProcessBatchAsync(Ct);
        await using var db = h.Db();
        var erpNo = (await db.MaterialRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id, Ct)).ErpRequisitionNo!;

        (await h.Erp.AdminPostAsync($"/mock-admin/requisitions/{erpNo}/advance")).EnsureSuccessStatusCode(); // Authorized
        await h.Sync.RunOnceAsync(Ct);
        Assert.Equal(MaterialRequestStatus.ApprovedInErp, (await db.MaterialRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id, Ct)).Status);

        (await h.Erp.AdminPostAsync($"/mock-admin/requisitions/{erpNo}/advance")).EnsureSuccessStatusCode(); // Closed: goods received
        await h.Sync.RunOnceAsync(Ct);
        Assert.Equal(MaterialRequestStatus.Received, (await db.MaterialRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id, Ct)).Status);
        Assert.Equal(15m, (await db.StockStatus.AsNoTracking().SingleAsync(s => s.PartNo == "VL-BFY-DN100", Ct)).QtyOnHand); // 5 + 10
    }

    [Fact]
    public async Task Revoked_token_is_refreshed_transparently()
    {
        await using var h = await IntegrationHarness.CreateAsync(sql, "token");
        var request = await SubmitAsync(h, ("ST-AH36-08", 1));
        (await h.Erp.AdminPostAsync("/mock-admin/tokens/revoke")).EnsureSuccessStatusCode();

        var result = await h.Outbox.ProcessBatchAsync(Ct);

        Assert.Equal(1, result.Succeeded);
        await using var db = h.Db();
        Assert.Equal(MaterialRequestStatus.SentToErp, (await db.MaterialRequests.SingleAsync(r => r.Id == request.Id, Ct)).Status);
    }

    private static async Task<MaterialRequest> SubmitAsync(IntegrationHarness h, params (string PartNo, decimal Qty)[] lines)
    {
        await using var db = h.Db();
        var request = await TestData.CreateDraftAsync(db, lines);
        await new TersaneSql(db).SubmitMaterialRequestAsync(request.Id, "test.user", null, Ct);
        return request;
    }

    private static async Task<List<JsonElement>> ErpRequisitionsAsync(IntegrationHarness h, string externalRef)
    {
        using var client = await h.Erp.AuthorizedClientAsync();
        var json = await client.GetFromJsonAsync<JsonElement>(
            $"/mock-erp/projection/v1/PurchaseRequisitionHandling.svc/PurchaseRequisitionSet?$filter=ExternalRef eq '{externalRef}'", Ct);
        return json.GetProperty("value").EnumerateArray().ToList();
    }
}
