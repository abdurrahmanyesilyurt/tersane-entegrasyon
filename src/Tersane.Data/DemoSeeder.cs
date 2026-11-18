using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tersane.Data.Entities;

namespace Tersane.Data;

/// <summary>
/// Fictional demo data (projects, blocks, work orders). Materials and stock are NOT seeded here:
/// they arrive from the mock ERP through the integration worker, exactly like in production.
/// </summary>
public static class DemoSeeder
{
    public static async Task MigrateAndSeedAsync(TersaneDbContext db, ILogger logger, CancellationToken ct = default)
    {
        // SQL Server in a container needs a few seconds after start; retry instead of failing the deploy.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                break;
            }
            catch (Exception ex) when (attempt < 30 && ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
            {
                logger.LogWarning("Veritabanı hazır değil (deneme {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        // One transaction for the whole seed, so a failure never leaves half the demo data behind.
        // The execution strategy (EnableRetryOnFailure) requires the transaction to run inside it.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (await db.Projects.AnyAsync(ct))
                return;
            await SeedAsync(db, ct);
            await tx.CommitAsync(ct);
            logger.LogInformation("Demo verisi yüklendi: 3 proje, 13 blok, 10 iş emri.");
        });
    }

    private static async Task SeedAsync(TersaneDbContext db, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var tanker = new Project
        {
            HullNo = "NB-1042", Name = "7.999 DWT Kimyasal Tanker", ShipType = "Kimyasal / ürün tankeri",
            Owner = "Demo Armatör A.Ş. (kurgusal)", Status = ProjectStatus.InProduction,
            StartDate = today.AddMonths(-7), PlannedDelivery = today.AddMonths(11),
        };
        var trawler = new Project
        {
            HullNo = "NB-1043", Name = "62 m Balıkçı Gemisi (Trol)", ShipType = "Balıkçı gemisi",
            Owner = "Örnek Balıkçılık Ltd. (kurgusal)", Status = ProjectStatus.InProduction,
            StartDate = today.AddMonths(-3), PlannedDelivery = today.AddMonths(14),
        };
        var ferry = new Project
        {
            HullNo = "NB-1051", Name = "120 m Yolcu-Araç Feribotu", ShipType = "Ro-Pax feribot",
            Owner = "Kurgu Feribot İşletmeleri (kurgusal)", Status = ProjectStatus.Planning,
            StartDate = today.AddMonths(1), PlannedDelivery = today.AddMonths(26),
        };

        AddBlocks(tanker, [
            ("B101", "Dip blok (kıç)", "Dip", 86.4m, BlockStatus.Erected),
            ("B102", "Dip blok (orta)", "Dip", 92.1m, BlockStatus.Erected),
            ("S201", "Borda bloğu (sancak)", "Borda", 64.0m, BlockStatus.Assembly),
            ("S202", "Borda bloğu (iskele)", "Borda", 63.2m, BlockStatus.Fabrication),
            ("D301", "Ana güverte bloğu", "Güverte", 48.7m, BlockStatus.Fabrication),
            ("E401", "Makine dairesi bloğu", "Makine dairesi", 112.5m, BlockStatus.Assembly),
            ("U501", "Üst bina (süperstrüktür)", "Üst bina", 71.3m, BlockStatus.Planned),
        ]);
        AddBlocks(trawler, [
            ("B101", "Dip blok (baş)", "Dip", 38.2m, BlockStatus.Fabrication),
            ("B102", "Dip blok (kıç)", "Dip", 41.0m, BlockStatus.Fabrication),
            ("E401", "Makine dairesi bloğu", "Makine dairesi", 55.6m, BlockStatus.Planned),
            ("D301", "Trol güvertesi", "Güverte", 29.9m, BlockStatus.Planned),
        ]);
        AddBlocks(ferry, [
            ("B101", "Dip blok 1", "Dip", 120.0m, BlockStatus.Planned),
            ("C201", "Araç güvertesi bloğu", "Güverte", 98.4m, BlockStatus.Planned),
        ]);

        db.Projects.AddRange(tanker, trawler, ferry);
        await db.SaveChangesAsync(ct);

        Block Blk(Project p, string code) => p.Blocks.Single(b => b.Code == code);

        db.WorkOrders.AddRange(
            Wo(tanker, Blk(tanker, "S201"), "Borda bloğu sac kesim ve kaynak", "Çelik İmalat", WorkOrderStatus.InProgress, -20, 15, 640),
            Wo(tanker, Blk(tanker, "S202"), "Borda bloğu panel hattı imalatı", "Çelik İmalat", WorkOrderStatus.Open, 5, 40, 720),
            Wo(tanker, Blk(tanker, "E401"), "Makine dairesi soğutma suyu boru devresi", "Boru Atölyesi", WorkOrderStatus.InProgress, -10, 25, 410),
            Wo(tanker, Blk(tanker, "E401"), "Ana makine kaidesi montajı", "Makine Montaj", WorkOrderStatus.Open, 12, 30, 260),
            Wo(tanker, Blk(tanker, "D301"), "Güverte kablo tavaları ve buatlar", "Elektrik", WorkOrderStatus.Open, 20, 45, 180),
            Wo(tanker, Blk(tanker, "B101"), "Dip blok astar boya", "Boya", WorkOrderStatus.Completed, -60, -45, 150),
            Wo(trawler, Blk(trawler, "B101"), "Dip blok sac kesim", "Çelik İmalat", WorkOrderStatus.InProgress, -5, 20, 380),
            Wo(trawler, Blk(trawler, "B102"), "Dip blok profil montajı", "Çelik İmalat", WorkOrderStatus.Open, 10, 35, 300),
            Wo(trawler, Blk(trawler, "E401"), "Yakıt hattı boru imalatı", "Boru Atölyesi", WorkOrderStatus.Open, 30, 60, 220),
            Wo(ferry, null, "Detay tasarım çıktılarının imalata aktarımı", "Planlama", WorkOrderStatus.Open, 15, 40, 90));

        // Sync watermarks (integration.SyncStates) are created by the worker on its first run.
        await db.SaveChangesAsync(ct);

        WorkOrder Wo(Project p, Block? b, string title, string dept, WorkOrderStatus status, int startOffset, int endOffset, decimal hours) => new()
        {
            ProjectId = p.Id, BlockId = b?.Id, Title = title, Department = dept, Status = status,
            PlannedStart = today.AddDays(startOffset), PlannedEnd = today.AddDays(endOffset), EstimatedManHours = hours,
        };
    }

    private static void AddBlocks(Project project, (string Code, string Name, string Zone, decimal Weight, BlockStatus Status)[] blocks)
    {
        foreach (var (code, name, zone, weight, status) in blocks)
            project.Blocks.Add(new Block { Code = code, Name = name, Zone = zone, WeightTon = weight, Status = status });
    }
}
