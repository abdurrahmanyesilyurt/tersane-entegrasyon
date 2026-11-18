namespace Tersane.Data.Entities;

/// <summary>Malzeme kartı. Kaynağı ERP'dir; iç sistemde yalnızca senkronize kopyası tutulur.</summary>
public class Material
{
    public int Id { get; set; }
    public required string PartNo { get; set; }
    public required string Description { get; set; }
    public required string Unit { get; set; }
    public required string Category { get; set; }
    public decimal SafetyStock { get; set; }
    public bool IsBlockedInErp { get; set; }
    public DateTime ErpChangedAt { get; set; }
    public DateTime SyncedAt { get; set; }
}

/// <summary>ERP'den gelen depo/lokasyon bazlı stok bakiyesi.</summary>
public class StockBalance
{
    public int Id { get; set; }
    public int MaterialId { get; set; }
    public Material? Material { get; set; }
    public required string Site { get; set; }
    public required string LocationNo { get; set; }
    public decimal QtyOnHand { get; set; }
    public decimal QtyReserved { get; set; }
    public DateTime ErpChangedAt { get; set; }
    public DateTime SyncedAt { get; set; }
}

public enum MaterialRequestStatus
{
    Draft,          // Taslak, düzenlenebilir
    Submitted,      // Onaylandı, ERP kuyruğunda (outbox)
    SentToErp,      // ERP satınalma talebi oluştu
    ApprovedInErp,  // ERP'de onaylandı
    Received,       // Malzeme ambara girdi
    Rejected,       // ERP'de iptal/red
    Failed,         // Entegrasyon hatası, arayüzden yeniden işlenebilir
}

public class MaterialRequest
{
    public int Id { get; set; }

    /// <summary>Filled by SQL Server from <c>dbo.seq_MaterialRequest</c>.</summary>
    public int SeqNo { get; set; }

    /// <summary>Persisted computed column, e.g. MT-2026-00012.</summary>
    public string No { get; set; } = "";

    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int? BlockId { get; set; }
    public Block? Block { get; set; }
    public int? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public required string RequestedBy { get; set; }
    public DateOnly NeedDate { get; set; }
    public MaterialRequestStatus Status { get; set; }
    public string? ErpRequisitionNo { get; set; }
    public string? ErpState { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>SQL Server rowversion: optimistic concurrency for edits.</summary>
    public byte[] RowVersion { get; set; } = [];

    public List<MaterialRequestLine> Lines { get; set; } = [];
}

public class MaterialRequestLine
{
    public int Id { get; set; }
    public int MaterialRequestId { get; set; }
    public int LineNo { get; set; }
    public int MaterialId { get; set; }
    public Material? Material { get; set; }
    public decimal Quantity { get; set; }
    public required string Unit { get; set; }
    public string? Note { get; set; }
}
