using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tersane.Data;
using Tersane.Web.Models;

namespace Tersane.Web.Controllers;

[Route("stok")]
public sealed class StockController(TersaneDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var states = await db.SyncStates.AsNoTracking().ToListAsync(ct);
        return View(new StockVm
        {
            // Keyless entity over dbo.vw_StockStatus.
            Rows = await db.StockStatus.AsNoTracking().OrderBy(s => s.Category).ThenBy(s => s.PartNo).ToListAsync(ct),
            StockSync = states.SingleOrDefault(s => s.Name == "ErpStock"),
            PartsSync = states.SingleOrDefault(s => s.Name == "ErpParts"),
        });
    }
}
