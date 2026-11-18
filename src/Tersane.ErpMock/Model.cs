using System.ComponentModel.DataAnnotations;

namespace Tersane.ErpMock;

// MOCK ERP DATA MODEL
// The shapes below only imitate the *style* of IFS Cloud projections (entity sets, PascalCase fields,
// Objstate, Contract = site). They are NOT the real IFS Cloud schema and must not be read as such.

/// <summary>Part catalogue (PartHandling.svc/InventoryPartSet).</summary>
public sealed class InventoryPart
{
    [Key] public string PartNo { get; set; } = "";
    public string Description { get; set; } = "";
    public string UnitMeas { get; set; } = "";
    public string PartCategory { get; set; } = "";
    public decimal SafetyStock { get; set; }

    /// <summary>"A" = active, "O" = obsolete / blocked for purchasing.</summary>
    public string PartStatus { get; set; } = "A";

    public DateTimeOffset ChangedAt { get; set; }

    public InventoryPart Clone() => (InventoryPart)MemberwiseClone();
}

/// <summary>Stock per site and location (InventoryPartInStockHandling.svc/InventoryPartInStockSet).</summary>
public sealed class InventoryPartInStock
{
    /// <summary>Opaque row key, written as Contract^PartNo^LocationNo.</summary>
    [Key] public string Objkey { get; set; } = "";
    public string Contract { get; set; } = "";
    public string PartNo { get; set; } = "";
    public string LocationNo { get; set; } = "";
    public decimal QtyOnhand { get; set; }
    public decimal QtyReserved { get; set; }
    public string UnitMeas { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; }

    public InventoryPartInStock Clone() => (InventoryPartInStock)MemberwiseClone();
}

/// <summary>Purchase requisition header with its lines (PurchaseRequisitionHandling.svc/PurchaseRequisitionSet).</summary>
public sealed class PurchaseRequisition
{
    [Key] public string RequisitionNo { get; set; } = "";
    public string Contract { get; set; } = "";

    /// <summary>Caller's business key. Unique: a second create with the same value returns 409.</summary>
    public string ExternalRef { get; set; } = "";

    public string? ProjectId { get; set; }
    public string? RequisitionerCode { get; set; }
    public string? Note { get; set; }

    /// <summary>Released → Authorized → Closed (received), or Cancelled.</summary>
    public string Objstate { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public List<PurchaseRequisitionLine> Lines { get; set; } = [];

    public PurchaseRequisition Clone()
    {
        var copy = (PurchaseRequisition)MemberwiseClone();
        copy.Lines = Lines.Select(l => l.Clone()).ToList();
        return copy;
    }
}

public sealed class PurchaseRequisitionLine
{
    public int LineNo { get; set; }
    public string PartNo { get; set; } = "";
    public decimal OriginalQty { get; set; }
    public string UnitMeas { get; set; } = "";
    public DateTimeOffset? WantedReceiptDate { get; set; }
    public string? ActivityRef { get; set; }
    public string? Note { get; set; }

    public PurchaseRequisitionLine Clone() => (PurchaseRequisitionLine)MemberwiseClone();
}

public sealed class ChaosSettings
{
    private readonly Lock _gate = new();

    /// <summary>Every projection call returns 503 (ERP down / maintenance window).</summary>
    public bool Outage { get; set; }

    /// <summary>0..1 share of calls that fail with 503.</summary>
    public double FailureRate { get; set; }

    /// <summary>Extra latency added to every projection call.</summary>
    public int LatencyMs { get; set; }

    /// <summary>The next N calls fail with 503, then the ERP recovers (deterministic, used by tests).</summary>
    public int FailNextRequests { get; set; }

    public bool ShouldFail(Random random)
    {
        lock (_gate)
        {
            if (Outage) return true;
            if (FailNextRequests > 0) { FailNextRequests--; return true; }
            return FailureRate > 0 && random.NextDouble() < FailureRate;
        }
    }

    public object Snapshot()
    {
        lock (_gate) return new { Outage, FailureRate, LatencyMs, FailNextRequests };
    }

    public void Apply(ChaosUpdate update)
    {
        lock (_gate)
        {
            if (update.Outage is { } o) Outage = o;
            if (update.FailureRate is { } f) FailureRate = Math.Clamp(f, 0, 1);
            if (update.LatencyMs is { } l) LatencyMs = Math.Clamp(l, 0, 30_000);
            if (update.FailNextRequests is { } n) FailNextRequests = Math.Max(0, n);
        }
    }
}

public sealed record ChaosUpdate(bool? Outage, double? FailureRate, int? LatencyMs, int? FailNextRequests);

public sealed class SimulationOptions
{
    /// <summary>Move requisitions forward on their own (Released → Authorized → Closed).</summary>
    public bool Enabled { get; set; } = true;
    public int AutoAuthorizeSeconds { get; set; } = 15;
    public int AutoCloseSeconds { get; set; } = 30;

    /// <summary>Randomly consume stock now and then, as production would.</summary>
    public bool ConsumptionEnabled { get; set; } = true;
    public int ConsumptionIntervalSeconds { get; set; } = 45;
}

public sealed class AuthOptions
{
    public string ClientId { get; set; } = "tersane-entegrasyon";
    public string ClientSecret { get; set; } = "mock-erp-demo-secret";
    public int TokenLifetimeSeconds { get; set; } = 300;
}
