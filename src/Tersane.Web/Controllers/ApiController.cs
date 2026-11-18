using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tersane.Data;
using Tersane.Data.Entities;

namespace Tersane.Web.Controllers;

/// <summary>Small JSON endpoints for jQuery (dependent dropdowns, material picker) and the React panel.</summary>
[ApiController]
[Route("api")]
public sealed class ApiController(TersaneDbContext db) : ControllerBase
{
    [HttpGet("projeler/{projectId:int}/bloklar")]
    public async Task<IActionResult> Blocks(int projectId, CancellationToken ct)
        => Ok(await db.Blocks.AsNoTracking().Where(b => b.ProjectId == projectId).OrderBy(b => b.Code)
            .Select(b => new { b.Id, text = b.Code + " — " + b.Name }).ToListAsync(ct));

    [HttpGet("projeler/{projectId:int}/is-emirleri")]
    public async Task<IActionResult> WorkOrders(int projectId, int? blockId, CancellationToken ct)
    {
        var query = db.WorkOrders.AsNoTracking()
            .Where(w => w.ProjectId == projectId && w.Status != WorkOrderStatus.Completed && w.Status != WorkOrderStatus.Cancelled);
        if (blockId is not null) query = query.Where(w => w.BlockId == blockId);
        return Ok(await query.OrderBy(w => w.No).Select(w => new { w.Id, text = w.No + " — " + w.Title }).ToListAsync(ct));
    }

    /// <summary>Material catalogue with live stock (from the vw_StockStatus view).</summary>
    [HttpGet("malzemeler")]
    public async Task<IActionResult> Materials(CancellationToken ct)
        => Ok(await db.StockStatus.AsNoTracking().OrderBy(s => s.Category).ThenBy(s => s.PartNo)
            .Select(s => new
            {
                id = s.MaterialId, s.PartNo, s.Description, s.Unit, s.Category,
                blocked = s.IsBlockedInErp, available = s.QtyAvailable, onOrder = s.QtyOnOrder, level = s.StockLevel,
            }).ToListAsync(ct));

    [EnableCors("panel")]
    [HttpGet("entegrasyon/ozet")]
    public async Task<IActionResult> IntegrationSummary(CancellationToken ct)
    {
        var health = await db.IntegrationHealth.AsNoTracking().SingleAsync(ct);
        var sync = await db.SyncStates.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var since = DateTime.UtcNow.AddHours(-1);
        var perMinute = await db.IntegrationLogs.AsNoTracking()
            .Where(l => l.OccurredAt >= since && l.Direction != "SYS")
            .GroupBy(l => new { l.OccurredAt.Year, l.OccurredAt.Month, l.OccurredAt.Day, l.OccurredAt.Hour, l.OccurredAt.Minute })
            .Select(g => new
            {
                g.Key.Hour, g.Key.Minute,
                ok = g.Count(l => l.Level != "Error"),
                error = g.Count(l => l.Level == "Error"),
                avgMs = g.Average(l => (double?)l.DurationMs),
            })
            .ToListAsync(ct);
        return Ok(new { health, sync, perMinute, serverTime = DateTime.UtcNow });
    }

    [EnableCors("panel")]
    [HttpGet("entegrasyon/mesajlar")]
    public async Task<IActionResult> Messages(OutboxStatus? status, CancellationToken ct)
    {
        var query = db.OutboxMessages.AsNoTracking();
        if (status is not null) query = query.Where(m => m.Status == status);
        return Ok(await query.OrderByDescending(m => m.Id).Take(50)
            .Select(m => new { m.MessageId, m.MessageType, m.IdempotencyKey, status = m.Status.ToString(), m.AttemptCount, m.MaxAttempts, m.NextAttemptAt, m.LastError, m.ExternalRef, m.CreatedAt, m.ProcessedAt })
            .ToListAsync(ct));
    }
}
