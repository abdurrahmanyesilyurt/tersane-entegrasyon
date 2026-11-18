namespace Tersane.Data.Entities;

/// <summary>Row of the <c>dbo.vw_StockStatus</c> view (keyless).</summary>
public class StockStatusRow
{
    public int MaterialId { get; set; }
    public string PartNo { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsBlockedInErp { get; set; }
    public decimal SafetyStock { get; set; }
    public decimal QtyOnHand { get; set; }
    public decimal QtyReservedInErp { get; set; }
    public decimal QtyAvailable { get; set; }
    public decimal QtyOnOrder { get; set; }
    public string StockLevel { get; set; } = "";
    public DateTime? LastSyncedAt { get; set; }
}

/// <summary>Single-row <c>integration.vw_IntegrationHealth</c> view.</summary>
public class IntegrationHealth
{
    public int PendingCount { get; set; }
    public int ProcessingCount { get; set; }
    public int FailedCount { get; set; }
    public int ProcessedLast24h { get; set; }
    public int? OldestPendingAgeSeconds { get; set; }
    public int ErrorsLastHour { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public DateTime? LastStockSyncAt { get; set; }
}

/// <summary>Row of the inline table-valued function <c>dbo.fn_ProjectMaterialSummary</c>.</summary>
public class ProjectMaterialSummaryRow
{
    public string PartNo { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal QtyRequested { get; set; }
    public decimal QtyOpen { get; set; }
    public decimal QtyReceived { get; set; }
    public int RequestCount { get; set; }
}
