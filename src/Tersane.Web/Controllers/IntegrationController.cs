using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tersane.Data;
using Tersane.Data.Entities;
using Tersane.Web.Infrastructure;
using Tersane.Web.Models;

namespace Tersane.Web.Controllers;

[Route("entegrasyon")]
public sealed class IntegrationController(TersaneDbContext db, TersaneSql sql, ErpMockAdminClient erpMock,
    IOptions<DemoOptions> demo, IConfiguration config) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(OutboxStatus? status, string? level, CancellationToken ct)
    {
        var messages = db.OutboxMessages.AsNoTracking();
        if (status is not null) messages = messages.Where(m => m.Status == status);
        var logs = db.IntegrationLogs.AsNoTracking();
        if (!string.IsNullOrEmpty(level)) logs = logs.Where(l => l.Level == level);

        return View(new IntegrationVm
        {
            Health = await db.IntegrationHealth.AsNoTracking().SingleAsync(ct),
            Messages = await messages.OrderByDescending(m => m.Id).Take(100).ToListAsync(ct),
            Logs = await logs.OrderByDescending(l => l.Id).Take(150).ToListAsync(ct),
            SyncStates = await db.SyncStates.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct),
            Status = status,
            Level = level,
            ErpMockUrl = config["ErpMock:PublicUrl"] ?? config["ErpMock:BaseUrl"] ?? "",
        });
    }

    /// <summary>"Yeniden işle": puts one failed message back in the queue (integration.usp_Outbox_Requeue).</summary>
    [HttpPost("mesajlar/{messageId:guid}/yeniden-isle")]
    public async Task<IActionResult> Requeue(Guid messageId, CancellationToken ct)
    {
        var count = await sql.RequeueAsync(messageId, demo.Value.UserName, ct);
        return Json(new
        {
            ok = count > 0,
            message = count > 0 ? "Mesaj yeniden kuyruğa alındı." : "Mesaj bulunamadı ya da hatalı durumda değil.",
        });
    }

    [HttpPost("mesajlar/yeniden-isle")]
    public async Task<IActionResult> RequeueAll(CancellationToken ct)
    {
        var count = await sql.RequeueAsync(null, demo.Value.UserName, ct);
        return Json(new { ok = true, message = $"{count} hatalı mesaj yeniden kuyruğa alındı." });
    }

    [HttpGet("log/{id:long}")]
    public async Task<IActionResult> LogDetail(long id, CancellationToken ct)
    {
        var log = await db.IntegrationLogs.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, ct);
        return log is null ? NotFound() : Json(new { log.Id, log.Operation, log.Url, log.HttpStatus, log.Message, log.RequestBody, log.ResponseBody });
    }

    /// <summary>Polled by the page every few seconds (jQuery) and by the React panel.</summary>
    [HttpGet("ozet")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var health = await db.IntegrationHealth.AsNoTracking().SingleAsync(ct);
        var chaos = await erpMock.GetChaosAsync(ct);
        return Json(new { health, chaos, serverTime = DateTime.UtcNow });
    }

    /// <summary>Demo only: flips the mock ERP's failure switches.</summary>
    [HttpPost("erp-simulasyon")]
    public async Task<IActionResult> Chaos(ChaosInput input, CancellationToken ct)
    {
        var ok = await erpMock.SetChaosAsync(input.Outage, input.FailurePercent / 100.0, input.LatencyMs, ct);
        return Json(new { ok, chaos = await erpMock.GetChaosAsync(ct) });
    }
}
