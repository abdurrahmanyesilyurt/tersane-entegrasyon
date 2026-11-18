using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tersane.Data;
using Tersane.Web.Models;

namespace Tersane.Web.Controllers;

[Route("projeler")]
public sealed class ProjectsController(TersaneDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var projects = await db.Projects.AsNoTracking()
            .Include(p => p.Blocks)
            .Include(p => p.WorkOrders)
            .AsSplitQuery()
            .OrderBy(p => p.HullNo)
            .ToListAsync(ct);
        return View(projects);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking()
            .Include(p => p.Blocks.OrderBy(b => b.Code))
            .Include(p => p.WorkOrders.OrderBy(w => w.PlannedStart)).ThenInclude(w => w.Block)
            .AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();

        return View(new ProjectDetailVm
        {
            Project = project,
            // Inline TVF, composed with LINQ (ORDER BY is added to the SQL by EF Core).
            MaterialSummary = await db.ProjectMaterialSummary(id).OrderByDescending(x => x.QtyOpen).ThenBy(x => x.PartNo).ToListAsync(ct),
            Requests = await db.MaterialRequests.AsNoTracking().Where(r => r.ProjectId == id)
                .OrderByDescending(r => r.Id).Take(10).ToListAsync(ct),
        });
    }
}
