using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tersane.Data.Entities;

namespace Tersane.Data;

public class TersaneDbContext(DbContextOptions<TersaneDbContext> options) : DbContext(options)
{
    public const string IntegrationSchema = "integration";

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<MaterialRequest> MaterialRequests => Set<MaterialRequest>();
    public DbSet<MaterialRequestLine> MaterialRequestLines => Set<MaterialRequestLine>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IntegrationLog> IntegrationLogs => Set<IntegrationLog>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();

    /// <summary>Keyless entity over the <c>dbo.vw_StockStatus</c> view.</summary>
    public DbSet<StockStatusRow> StockStatus => Set<StockStatusRow>();

    /// <summary>Keyless entity over the <c>integration.vw_IntegrationHealth</c> view.</summary>
    public DbSet<IntegrationHealth> IntegrationHealth => Set<IntegrationHealth>();

    /// <summary>Inline TVF, composable in LINQ: <c>db.ProjectMaterialSummary(id).OrderBy(...)</c>.</summary>
    public IQueryable<ProjectMaterialSummaryRow> ProjectMaterialSummary(int projectId)
        => FromExpression(() => ProjectMaterialSummary(projectId));

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(18, 3);
        builder.Properties<DateTime>().HaveColumnType("datetime2(3)");
        builder.Properties<string>().HaveMaxLength(200);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasSequence<int>("seq_WorkOrder").StartsAt(1);
        b.HasSequence<int>("seq_MaterialRequest").StartsAt(1);

        b.Entity<Project>(e =>
        {
            e.Property(x => x.HullNo).HasMaxLength(20);
            e.HasIndex(x => x.HullNo).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<Block>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20);
            e.HasIndex(x => new { x.ProjectId, x.Code }).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Project).WithMany(x => x.Blocks).HasForeignKey(x => x.ProjectId);
        });

        b.Entity<WorkOrder>(e =>
        {
            e.Property(x => x.SeqNo).HasDefaultValueSql("NEXT VALUE FOR dbo.seq_WorkOrder").ValueGeneratedOnAdd();
            // FORMAT() is non-deterministic and cannot be PERSISTED; RIGHT/CONVERT/YEAR can.
            e.Property(x => x.No).HasMaxLength(20)
                .HasComputedColumnSql("N'IE-' + CONVERT(nchar(4), YEAR([CreatedAt])) + N'-' + RIGHT(N'0000' + CONVERT(nvarchar(10), [SeqNo]), 4)", stored: true);
            e.HasIndex(x => x.No).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Department).HasMaxLength(50);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Project).WithMany(x => x.WorkOrders).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Block).WithMany().HasForeignKey(x => x.BlockId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProjectId, x.Status });
        });

        b.Entity<Material>(e =>
        {
            e.Property(x => x.PartNo).HasMaxLength(40);
            e.HasIndex(x => x.PartNo).IsUnique();
            e.Property(x => x.Unit).HasMaxLength(10);
            e.Property(x => x.Category).HasMaxLength(50);
        });

        b.Entity<StockBalance>(e =>
        {
            e.Property(x => x.Site).HasMaxLength(10);
            e.Property(x => x.LocationNo).HasMaxLength(20);
            e.HasIndex(x => new { x.MaterialId, x.Site, x.LocationNo }).IsUnique();
            e.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId);
        });

        b.Entity<MaterialRequest>(e =>
        {
            e.Property(x => x.SeqNo).HasDefaultValueSql("NEXT VALUE FOR dbo.seq_MaterialRequest").ValueGeneratedOnAdd();
            e.Property(x => x.No).HasMaxLength(20)
                .HasComputedColumnSql("N'MT-' + CONVERT(nchar(4), YEAR([CreatedAt])) + N'-' + RIGHT(N'00000' + CONVERT(nvarchar(10), [SeqNo]), 5)", stored: true);
            e.HasIndex(x => x.No).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ErpRequisitionNo).HasMaxLength(30);
            e.HasIndex(x => x.ErpRequisitionNo).HasFilter("[ErpRequisitionNo] IS NOT NULL");
            e.Property(x => x.ErpState).HasMaxLength(30);
            e.Property(x => x.RequestedBy).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Block).WithMany().HasForeignKey(x => x.BlockId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.MaterialRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MaterialRequestLine>(e =>
        {
            e.HasIndex(x => new { x.MaterialRequestId, x.LineNo }).IsUnique();
            e.Property(x => x.Unit).HasMaxLength(10);
            e.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_MaterialRequestLines_Quantity", "[Quantity] > 0"));
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OutboxMessages", IntegrationSchema);
            e.HasIndex(x => x.MessageId).IsUnique();
            e.Property(x => x.IdempotencyKey).HasMaxLength(200);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.Property(x => x.MessageType).HasMaxLength(100);
            e.Property(x => x.AggregateType).HasMaxLength(50);
            e.Property(x => x.AggregateId).HasMaxLength(50);
            e.Property(x => x.Payload).HasColumnType("nvarchar(max)");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LockedBy).HasMaxLength(100);
            e.Property(x => x.LastError).HasMaxLength(2000);
            e.Property(x => x.ExternalRef).HasMaxLength(50);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            // Filtered index: the claim query only ever looks at pending rows.
            e.HasIndex(x => x.NextAttemptAt).HasFilter("[Status] = N'Pending'").HasDatabaseName("IX_OutboxMessages_Pending");
            e.HasIndex(x => new { x.AggregateType, x.AggregateId });
        });

        b.Entity<IntegrationLog>(e =>
        {
            e.ToTable("IntegrationLogs", IntegrationSchema);
            e.Property(x => x.Direction).HasMaxLength(3);
            e.Property(x => x.Operation).HasMaxLength(100);
            e.Property(x => x.Level).HasMaxLength(10);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.Property(x => x.HttpMethod).HasMaxLength(10);
            e.Property(x => x.Url).HasMaxLength(500);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.RequestBody).HasColumnType("nvarchar(max)");
            e.Property(x => x.ResponseBody).HasColumnType("nvarchar(max)");
            e.Property(x => x.OccurredAt).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.MessageId).HasFilter("[MessageId] IS NOT NULL");
        });

        b.Entity<SyncState>(e =>
        {
            e.ToTable("SyncStates", IntegrationSchema);
            e.HasKey(x => x.Name);
            e.Property(x => x.Name).HasMaxLength(50);
            e.Property(x => x.LastError).HasMaxLength(2000);
        });

        b.Entity<StockStatusRow>(e => e.HasNoKey().ToView("vw_StockStatus", "dbo"));
        b.Entity<IntegrationHealth>(e => e.HasNoKey().ToView("vw_IntegrationHealth", IntegrationSchema));
        b.Entity<ProjectMaterialSummaryRow>().HasNoKey().ToTable((string?)null);
        b.HasDbFunction(typeof(TersaneDbContext).GetMethod(nameof(ProjectMaterialSummary), [typeof(int)])!)
            .HasName("fn_ProjectMaterialSummary").HasSchema("dbo");
    }
}

/// <summary>Used only by <c>dotnet ef</c> at design time.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<TersaneDbContext>
{
    public TersaneDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<TersaneDbContext>()
            .UseSqlServer("Server=localhost,14333;Database=Tersane;User Id=sa;Password=design-time-only;TrustServerCertificate=true")
            .Options);
}
