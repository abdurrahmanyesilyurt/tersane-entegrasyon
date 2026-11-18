namespace Tersane.Data.Entities;

public enum OutboxStatus { Pending, Processing, Processed, Failed }

/// <summary>
/// Transactional outbox: the business change and the message that must reach the ERP are written
/// in the same SQL transaction, so a message is never lost and never sent for a rolled-back change.
/// </summary>
public class OutboxMessage
{
    public long Id { get; set; }
    public Guid MessageId { get; set; }
    public required string MessageType { get; set; }
    public required string AggregateType { get; set; }
    public required string AggregateId { get; set; }

    /// <summary>Unique: the same business event can only be queued once.</summary>
    public required string IdempotencyKey { get; set; }

    public required string Payload { get; set; }
    public OutboxStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public string? ExternalRef { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

/// <summary>Every call to and from the ERP, with timing and payloads, for support and audit.</summary>
public class IntegrationLog
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }

    /// <summary>OUT (iç sistem → ERP), IN (ERP → iç sistem), SYS (kuyruk/operatör olayı).</summary>
    public required string Direction { get; set; }

    public required string Operation { get; set; }

    /// <summary>Info, Warning, Error.</summary>
    public required string Level { get; set; }

    public Guid? MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public string? HttpMethod { get; set; }
    public string? Url { get; set; }
    public int? HttpStatus { get; set; }
    public int? DurationMs { get; set; }
    public int? Attempt { get; set; }
    public required string Message { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
}

/// <summary>Delta sync watermark per feed (parts, stock, requisition states).</summary>
public class SyncState
{
    public required string Name { get; set; }
    public DateTime Watermark { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public int LastRowCount { get; set; }
}
