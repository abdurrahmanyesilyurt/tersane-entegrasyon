-- Claim a batch of outbox messages for one worker.
-- READPAST skips rows another worker has locked, UPDLOCK keeps them ours until the UPDATE,
-- so several worker instances can run in parallel and never pick up the same message.
-- A message stuck in Processing (worker crashed) is reclaimed once its lease expires.
CREATE OR ALTER PROCEDURE integration.usp_Outbox_Claim
    @WorkerId     nvarchar(100),
    @BatchSize    int = 10,
    @LeaseSeconds int = 60
AS
BEGIN
    SET NOCOUNT ON;

    WITH NextBatch AS
    (
        SELECT TOP (@BatchSize) *
        FROM    integration.OutboxMessages WITH (ROWLOCK, UPDLOCK, READPAST)
        WHERE   (Status = N'Pending'    AND NextAttemptAt <= SYSUTCDATETIME())
           OR   (Status = N'Processing' AND LockedUntil   <  SYSUTCDATETIME())
        ORDER BY Id
    )
    UPDATE  NextBatch
    SET     Status       = N'Processing',
            LockedBy     = @WorkerId,
            LockedUntil  = DATEADD(SECOND, @LeaseSeconds, SYSUTCDATETIME()),
            AttemptCount = AttemptCount + 1
    OUTPUT  inserted.Id,
            inserted.MessageId,
            inserted.MessageType,
            inserted.AggregateType,
            inserted.AggregateId,
            inserted.IdempotencyKey,
            inserted.Payload,
            inserted.AttemptCount,
            inserted.MaxAttempts,
            inserted.CorrelationId;
END;
GO

-- Put failed messages back in the queue (one message, or all failed ones when @MessageId is NULL).
-- Called from the "Yeniden işle" button; the operator and the reason are written to the log.
CREATE OR ALTER PROCEDURE integration.usp_Outbox_Requeue
    @MessageId   uniqueidentifier = NULL,
    @RequestedBy nvarchar(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Requeued TABLE (MessageId uniqueidentifier, AggregateType nvarchar(50), AggregateId nvarchar(50), LastError nvarchar(2000));

    BEGIN TRANSACTION;

    UPDATE  integration.OutboxMessages
    SET     Status = N'Pending', AttemptCount = 0, NextAttemptAt = SYSUTCDATETIME(),
            LockedBy = NULL, LockedUntil = NULL
    OUTPUT  inserted.MessageId, inserted.AggregateType, inserted.AggregateId, deleted.LastError INTO @Requeued
    WHERE   Status = N'Failed'
      AND   (@MessageId IS NULL OR MessageId = @MessageId);

    UPDATE  r
    SET     r.Status = N'Submitted', r.UpdatedAt = SYSUTCDATETIME()
    FROM    dbo.MaterialRequests AS r
    JOIN    @Requeued            AS q ON q.AggregateType = N'MaterialRequest' AND q.AggregateId = CONVERT(nvarchar(50), r.Id)
    WHERE   r.Status = N'Failed';

    INSERT integration.IntegrationLogs (Direction, Operation, Level, MessageId, Message)
    SELECT N'SYS', N'Outbox.Requeue', N'Warning', MessageId,
           CONCAT(N'Mesaj yeniden kuyruğa alındı (', @RequestedBy, N'). Önceki hata: ', LEFT(ISNULL(LastError, N'-'), 300))
    FROM   @Requeued;

    COMMIT TRANSACTION;

    SELECT COUNT(*) AS RequeuedCount FROM @Requeued;
END;
GO

-- Housekeeping: delete old log rows in small batches so the log never holds long locks
-- or blows up the transaction log.
CREATE OR ALTER PROCEDURE integration.usp_IntegrationLog_Purge
    @KeepDays  int = 30,
    @BatchSize int = 5000
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Cutoff datetime2(3) = DATEADD(DAY, -@KeepDays, SYSUTCDATETIME()),
            @Deleted int = 1,
            @Total int = 0;

    WHILE @Deleted > 0
    BEGIN
        DELETE TOP (@BatchSize) FROM integration.IntegrationLogs WHERE OccurredAt < @Cutoff;
        SET @Deleted = @@ROWCOUNT;
        SET @Total += @Deleted;
    END;

    SELECT @Total AS DeletedCount;
END;
