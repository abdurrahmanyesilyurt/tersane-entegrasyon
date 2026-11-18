using System.Net;

namespace Tersane.Worker.Erp;

public sealed class ErpOptions
{
    public const string Section = "Erp";

    /// <summary>Base address of the (mock) ERP, e.g. http://erp-mock:8080/.</summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:5101/");
    public string ClientId { get; set; } = "tersane-entegrasyon";
    public string ClientSecret { get; set; } = "";
    public string Site { get; set; } = "TRS1";

    /// <summary>Polly: in-process retries for one call (seconds-level, transient faults).</summary>
    public int RetryCount { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 1000;

    /// <summary>Wait as long as the ERP's Retry-After header says instead of the computed backoff.</summary>
    public bool HonourRetryAfter { get; set; } = true;
    public int AttemptTimeoutSeconds { get; set; } = 10;
    public int TotalTimeoutSeconds { get; set; } = 60;
}

public sealed class IntegrationOptions
{
    public const string Section = "Integration";

    public int OutboxPollSeconds { get; set; } = 2;
    public int OutboxBatchSize { get; set; } = 10;
    public int OutboxLeaseSeconds { get; set; } = 120;

    /// <summary>Outbox-level backoff after Polly gave up: base × 2^(attempt-1), capped.</summary>
    public int OutboxBackoffBaseSeconds { get; set; } = 15;
    public int OutboxBackoffMaxSeconds { get; set; } = 600;

    public int SyncIntervalSeconds { get; set; } = 10;

    /// <summary>Delta sync re-reads this window before the watermark; upserts are idempotent so overlap is safe.</summary>
    public int SyncOverlapSeconds { get; set; } = 5;
}

// ── ERP payloads (mock ERP field names, PascalCase like the projections) ──

public sealed record ODataPage<T>(List<T> Value, string? NextLink);

public sealed record ErpPart(string PartNo, string Description, string UnitMeas, string PartCategory, decimal SafetyStock, string PartStatus, DateTimeOffset ChangedAt);

public sealed record ErpStock(string Objkey, string Contract, string PartNo, string LocationNo, decimal QtyOnhand, decimal QtyReserved, string UnitMeas, DateTimeOffset ChangedAt);

public sealed record ErpRequisitionState(string RequisitionNo, string ExternalRef, string Objstate, DateTimeOffset ChangedAt);

public sealed record ErpRequisitionLine(int LineNo, string PartNo, decimal OriginalQty, string UnitMeas, DateTimeOffset? WantedReceiptDate, string? ActivityRef, string? Note);

public sealed record ErpRequisitionCreate(string ExternalRef, string? ProjectId, string? RequisitionerCode, string? Note, List<ErpRequisitionLine> Lines);

public sealed record ErpRequisition(string RequisitionNo, string ExternalRef, string Objstate, DateTimeOffset ChangedAt);

/// <summary>Context that travels with each HTTP request so the audit handler can log it properly.</summary>
public sealed record ErpCallContext(string Operation, Guid? MessageId = null, string? CorrelationId = null, bool AlwaysLog = true);

public enum ErpFailureKind
{
    /// <summary>Network error, timeout, 5xx, 429, open circuit: try again later.</summary>
    Transient,

    /// <summary>The ERP rejected the data (4xx). Retrying the same message will not help.</summary>
    Permanent,

    /// <summary>409: the document already exists in the ERP.</summary>
    Conflict,
}

public sealed class ErpException(ErpFailureKind kind, string message, HttpStatusCode? status = null, string? errorCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public ErpFailureKind Kind { get; } = kind;
    public HttpStatusCode? Status { get; } = status;
    public string? ErrorCode { get; } = errorCode;
}
