using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Tersane.Data;
using Tersane.Data.Entities;
using Tersane.Web.Models;

namespace Tersane.Web.Controllers;

[Route("is-emirleri")]
public sealed class WorkOrdersController(TersaneDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int? projectId, WorkOrderStatus? status, CancellationToken ct)
    {
        var query = db.WorkOrders.AsNoTracking().Include(w => w.Project).Include(w => w.Block).AsQueryable();
        if (projectId is not null) query = query.Where(w => w.ProjectId == projectId);
        if (status is not null) query = query.Where(w => w.Status == status);

        return View(new WorkOrderListVm
        {
            Items = await query.OrderBy(w => w.Status).ThenBy(w => w.PlannedStart).ToListAsync(ct),
            ProjectId = projectId,
            Status = status,
            Projects = await ProjectList(projectId, ct),
        });
    }

    [HttpGet("yeni")]
    public async Task<IActionResult> Create(int? projectId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        await FillListsAsync(projectId, ct);
        return View(new WorkOrderInput { ProjectId = projectId, PlannedStart = today.AddDays(7), PlannedEnd = today.AddDays(30) });
    }

    [HttpPost("yeni")]
    public async Task<IActionResult> Create(WorkOrderInput input, CancellationToken ct)
    {
        if (input.PlannedEnd < input.PlannedStart)
            ModelState.AddModelError(nameof(input.PlannedEnd), "Bitiş tarihi başlangıçtan önce olamaz.");
        if (input.BlockId is not null && !await db.Blocks.AnyAsync(b => b.Id == input.BlockId && b.ProjectId == input.ProjectId, ct))
            ModelState.AddModelError(nameof(input.BlockId), "Blok seçilen projeye ait değil.");

        if (!ModelState.IsValid)
        {
            await FillListsAsync(input.ProjectId, ct);
            return View(input);
        }

        var workOrder = new WorkOrder
        {
            ProjectId = input.ProjectId!.Value,
            BlockId = input.BlockId,
            Title = input.Title!.Trim(),
            Department = input.Department!,
            Status = WorkOrderStatus.Open,
            PlannedStart = input.PlannedStart!.Value,
            PlannedEnd = input.PlannedEnd!.Value,
            EstimatedManHours = input.EstimatedManHours,
        };
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync(ct); // No and SeqNo come back from SQL Server (sequence + computed column)

        TempData["Flash"] = $"{workOrder.No} numaralı iş emri oluşturuldu.";
        return RedirectToAction(nameof(Index), new { projectId = workOrder.ProjectId });
    }

    [HttpPost("{id:int}/durum")]
    public async Task<IActionResult> ChangeStatus(int id, WorkOrderStatus status, CancellationToken ct)
    {
        var updated = await db.WorkOrders.Where(w => w.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, status), ct);
        return updated == 0 ? NotFound() : Json(new { ok = true });
    }

    private async Task FillListsAsync(int? projectId, CancellationToken ct)
    {
        ViewBag.Projects = await ProjectList(projectId, ct);
        ViewBag.Departments = new SelectList(Infrastructure.Ui.Departments);
    }

    private async Task<SelectList> ProjectList(int? selected, CancellationToken ct)
        => new(await db.Projects.AsNoTracking().OrderBy(p => p.HullNo)
            .Select(p => new { p.Id, Text = p.HullNo + " — " + p.Name }).ToListAsync(ct), "Id", "Text", selected);
}
