namespace Tersane.Data.Entities;

public enum ProjectStatus { Planning, InProduction, Delivered }

public enum BlockStatus { Planned, Fabrication, Assembly, Erected }

public enum WorkOrderStatus { Open, InProgress, Completed, Cancelled }

/// <summary>Bir yeni inşa projesi (gemi). Tersanede proje = tekne numarası (hull no).</summary>
public class Project
{
    public int Id { get; set; }
    public required string HullNo { get; set; }
    public required string Name { get; set; }
    public required string ShipType { get; set; }
    public required string Owner { get; set; }
    public ProjectStatus Status { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly PlannedDelivery { get; set; }

    public List<Block> Blocks { get; set; } = [];
    public List<WorkOrder> WorkOrders { get; set; } = [];
}

/// <summary>Gemi gövdesinin ayrı imal edilip kızakta birleştirilen bloğu.</summary>
public class Block
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Zone { get; set; }
    public decimal WeightTon { get; set; }
    public BlockStatus Status { get; set; }
}

public class WorkOrder
{
    public int Id { get; set; }

    /// <summary>Filled by SQL Server from <c>dbo.seq_WorkOrder</c>.</summary>
    public int SeqNo { get; set; }

    /// <summary>Persisted computed column, e.g. IE-2026-0007.</summary>
    public string No { get; set; } = "";

    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int? BlockId { get; set; }
    public Block? Block { get; set; }
    public required string Title { get; set; }
    public required string Department { get; set; }
    public WorkOrderStatus Status { get; set; }
    public DateOnly PlannedStart { get; set; }
    public DateOnly PlannedEnd { get; set; }
    public decimal EstimatedManHours { get; set; }
    public DateTime CreatedAt { get; set; }
}
