using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tersane.Data;
using Tersane.Data.Entities;
using Tersane.Web.Models;

namespace Tersane.Web.Controllers;

public sealed class HomeController(TersaneDbContext db) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new DashboardVm
        {
            Health = await db.IntegrationHealth.AsNoTracking().SingleAsync(ct),
            ActiveProjects = await db.Projects.CountAsync(p => p.Status != ProjectStatus.Delivered, ct),
            OpenWorkOrders = await db.WorkOrders.CountAsync(w => w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress, ct),
            OpenRequests = await db.MaterialRequests.CountAsync(r => r.Status != MaterialRequestStatus.Received && r.Status != MaterialRequestStatus.Rejected, ct),
            CriticalStockItems = await db.StockStatus.CountAsync(s => s.StockLevel != "OK", ct),
            RecentRequests = await db.MaterialRequests.AsNoTracking().Include(r => r.Project)
                .OrderByDescending(r => r.Id).Take(6).ToListAsync(ct),
            RecentErrors = await db.IntegrationLogs.AsNoTracking().Where(l => l.Level == "Error")
                .OrderByDescending(l => l.Id).Take(5).ToListAsync(ct),
            SyncStates = await db.SyncStates.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct),
        };
        return View(vm);
    }

    [HttpGet("/hata")]
    public IActionResult Error() => View();
}
