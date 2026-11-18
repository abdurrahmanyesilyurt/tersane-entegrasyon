using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tersane.Data;
using Tersane.Data.Entities;
using Tersane.Web.Infrastructure;
using Tersane.Web.Models;

namespace Tersane.Web.Controllers;

[Route("malzeme-talepleri")]
public sealed class MaterialRequestsController(TersaneDbContext db, TersaneSql sql, IOptions<DemoOptions> demo, ILogger<MaterialRequestsController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(MaterialRequestStatus? status, string? q, CancellationToken ct)
    {
        var query = db.MaterialRequests.AsNoTracking()
            .Include(r => r.Project).Include(r => r.Block).Include(r => r.WorkOrder).Include(r => r.Lines)
            .AsSplitQuery();
        if (status is not null) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(r => r.No.Contains(term) || r.Project!.HullNo.Contains(term) || (r.ErpRequisitionNo != null && r.ErpRequisitionNo.Contains(term)));
        }

        return View(new MaterialRequestListVm
        {
            Items = await query.OrderByDescending(r => r.Id).Take(200).ToListAsync(ct),
            Status = status,
            Search = q,
        });
    }

    [HttpGet("yeni")]
    public async Task<IActionResult> Create(int? projectId, CancellationToken ct)
    {
        await FillProjectsAsync(projectId, ct);
        return View(new MaterialRequestInput { ProjectId = projectId, NeedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14)) });
    }

    [HttpPost("yeni")]
    public async Task<IActionResult> Create(MaterialRequestInput input, bool submitNow, CancellationToken ct)
    {
        input.Lines = input.Lines.Where(l => l.MaterialId is not null || l.Quantity is not null).ToList();
        if (input.Lines.Count == 0)
            ModelState.AddModelError("", "En az bir malzeme kalemi giriniz.");
        if (input.NeedDate < DateOnly.FromDateTime(DateTime.Today))
            ModelState.AddModelError(nameof(input.NeedDate), "İhtiyaç tarihi geçmişte olamaz.");

        var materialIds = input.Lines.Where(l => l.MaterialId is not null).Select(l => l.MaterialId!.Value).Distinct().ToList();
        var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        if (materials.Count != materialIds.Count)
            ModelState.AddModelError("", "Seçilen malzemelerden biri bulunamadı.");
        if (input.BlockId is not null && !await db.Blocks.AnyAsync(b => b.Id == input.BlockId && b.ProjectId == input.ProjectId, ct))
            ModelState.AddModelError(nameof(input.BlockId), "Blok seçilen projeye ait değil.");

        if (!ModelState.IsValid)
        {
            if (input.Lines.Count == 0) input.Lines.Add(new MaterialRequestLineInput());
            await FillProjectsAsync(input.ProjectId, ct);
            return View(input);
        }

        var request = new MaterialRequest
        {
            ProjectId = input.ProjectId!.Value,
            BlockId = input.BlockId,
            WorkOrderId = input.WorkOrderId,
            NeedDate = input.NeedDate!.Value,
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
            RequestedBy = demo.Value.UserName,
            Status = MaterialRequestStatus.Draft,
            Lines = input.Lines.Select((l, i) => new MaterialRequestLine
            {
                LineNo = i + 1,
                MaterialId = l.MaterialId!.Value,
                Quantity = l.Quantity!.Value,
                Unit = materials[l.MaterialId!.Value].Unit,
                Note = string.IsNullOrWhiteSpace(l.Note) ? null : l.Note.Trim(),
            }).ToList(),
        };
        db.MaterialRequests.Add(request);
        await db.SaveChangesAsync(ct);

        if (submitNow)
            return await SubmitInternalAsync(request.Id, ajax: false, ct);

        TempData["Flash"] = $"{request.No} taslak olarak kaydedildi.";
        return RedirectToAction(nameof(Detail), new { id = request.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var request = await db.MaterialRequests.AsNoTracking()
            .Include(r => r.Project).Include(r => r.Block).Include(r => r.WorkOrder)
            .Include(r => r.Lines.OrderBy(l => l.LineNo)).ThenInclude(l => l.Material)
            .AsSplitQuery()
            .SingleOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return NotFound();

        var aggregateId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var messages = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.AggregateType == "MaterialRequest" && m.AggregateId == aggregateId)
            .OrderBy(m => m.Id).ToListAsync(ct);
        var messageIds = messages.Select(m => (Guid?)m.MessageId).ToList();
        var materialIds = request.Lines.Select(l => l.MaterialId).ToList();

        return View(new MaterialRequestDetailVm
        {
            Request = request,
            Messages = messages,
            Logs = await db.IntegrationLogs.AsNoTracking()
                .Where(l => messageIds.Contains(l.MessageId) || (request.ErpRequisitionNo != null && l.Message.Contains(request.No)))
                .OrderByDescending(l => l.Id).Take(50).ToListAsync(ct),
            Stock = await db.StockStatus.AsNoTracking().Where(s => materialIds.Contains(s.MaterialId)).ToDictionaryAsync(s => s.MaterialId, ct),
        });
    }

    [HttpPost("{id:int}/gonder")]
    public Task<IActionResult> Submit(int id, CancellationToken ct)
        => SubmitInternalAsync(id, ajax: Request.Headers.XRequestedWith == "XMLHttpRequest", ct);

    private async Task<IActionResult> SubmitInternalAsync(int id, bool ajax, CancellationToken ct)
    {
        string message;
        var ok = true;
        try
        {
            var correlationId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
            var result = await sql.SubmitMaterialRequestAsync(id, demo.Value.UserName, correlationId, ct);
            message = result.AlreadyQueued
                ? "Talep zaten ERP kuyruğunda (tekrar gönderim engellendi)."
                : "Talep onaylandı ve ERP kuyruğuna alındı. Entegrasyon servisi birkaç saniye içinde iletecek.";
        }
        catch (BusinessRuleException ex)
        {
            ok = false;
            message = ex.Message;
            logger.LogInformation("Gönderim reddedildi ({Code}): {Message}", ex.Code, ex.Message);
        }

        if (ajax)
            return Json(new { ok, message });

        TempData[ok ? "Flash" : "FlashError"] = message;
        return RedirectToAction(nameof(Detail), new { id });
    }

    private async Task FillProjectsAsync(int? selected, CancellationToken ct)
        => ViewBag.Projects = new SelectList(await db.Projects.AsNoTracking()
            .Where(p => p.Status != ProjectStatus.Delivered).OrderBy(p => p.HullNo)
            .Select(p => new { p.Id, Text = p.HullNo + " — " + p.Name }).ToListAsync(ct), "Id", "Text", selected);
}
