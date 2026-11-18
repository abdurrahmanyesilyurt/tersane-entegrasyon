-- Submit a draft material request to the ERP.
-- The status change and the outbox message are written in ONE transaction (transactional outbox):
-- either both happen or neither does, so no request is ever "approved but never sent".
-- Idempotent: calling it twice for the same request returns the already queued message.
-- Note: user procedures use the usp_ prefix; sp_ is reserved for system procedures (master lookup first).
CREATE OR ALTER PROCEDURE dbo.usp_MaterialRequest_Submit
    @MaterialRequestId int,
    @SubmittedBy       nvarchar(100),
    @CorrelationId     nvarchar(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;   -- any runtime error rolls back the whole transaction

    DECLARE @No             nvarchar(20),
            @Status         nvarchar(20),
            @IdempotencyKey nvarchar(200),
            @MessageId      uniqueidentifier = NEWID(),
            @Payload        nvarchar(max);

    BEGIN TRY
        BEGIN TRANSACTION;

        -- UPDLOCK + HOLDLOCK: two concurrent submits of the same request are serialised here.
        SELECT  @No = No, @Status = Status
        FROM    dbo.MaterialRequests WITH (UPDLOCK, HOLDLOCK)
        WHERE   Id = @MaterialRequestId;

        IF @No IS NULL
            THROW 50404, N'Malzeme talebi bulunamadı.', 1;

        SET @IdempotencyKey = N'MaterialRequest.Submit:' + @No;

        IF EXISTS (SELECT 1 FROM integration.OutboxMessages WHERE IdempotencyKey = @IdempotencyKey)
        BEGIN
            COMMIT TRANSACTION;
            SELECT  MessageId, CAST(1 AS bit) AS AlreadyQueued
            FROM    integration.OutboxMessages
            WHERE   IdempotencyKey = @IdempotencyKey;
            RETURN;
        END;

        IF @Status <> N'Draft'
            THROW 50409, N'Yalnızca taslak durumundaki talepler ERP''ye gönderilebilir.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.MaterialRequestLines WHERE MaterialRequestId = @MaterialRequestId)
            THROW 50422, N'Talepte en az bir malzeme kalemi olmalı.', 1;

        UPDATE  dbo.MaterialRequests
        SET     Status = N'Submitted', SubmittedAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME()
        WHERE   Id = @MaterialRequestId;

        -- The message body is built in T-SQL with FOR JSON (nested lines array).
        SET @Payload =
        (
            SELECT  r.Id            AS materialRequestId,
                    r.No            AS requestNo,
                    p.HullNo        AS hullNo,
                    b.Code          AS blockCode,
                    w.No            AS workOrderNo,
                    r.RequestedBy   AS requestedBy,
                    r.NeedDate      AS needDate,
                    r.Note          AS note,
                    (
                        SELECT  l.[LineNo]  AS [lineNo],
                                m.PartNo    AS partNo,
                                l.Quantity  AS quantity,
                                l.Unit      AS unit,
                                l.Note      AS note
                        FROM    dbo.MaterialRequestLines AS l
                        JOIN    dbo.Materials            AS m ON m.Id = l.MaterialId
                        WHERE   l.MaterialRequestId = r.Id
                        ORDER BY l.[LineNo]
                        FOR JSON PATH
                    )               AS lines
            FROM    dbo.MaterialRequests AS r
            JOIN    dbo.Projects         AS p ON p.Id = r.ProjectId
            LEFT JOIN dbo.Blocks         AS b ON b.Id = r.BlockId
            LEFT JOIN dbo.WorkOrders     AS w ON w.Id = r.WorkOrderId
            WHERE   r.Id = @MaterialRequestId
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        );

        INSERT integration.OutboxMessages
            (MessageId, MessageType, AggregateType, AggregateId, IdempotencyKey, Payload,
             Status, AttemptCount, MaxAttempts, NextAttemptAt, CorrelationId)
        VALUES
            (@MessageId, N'MaterialRequest.Submitted', N'MaterialRequest', CONVERT(nvarchar(50), @MaterialRequestId),
             @IdempotencyKey, @Payload, N'Pending', 0, 5, SYSUTCDATETIME(), @CorrelationId);

        INSERT integration.IntegrationLogs (Direction, Operation, Level, MessageId, CorrelationId, Message)
        VALUES (N'SYS', N'MaterialRequest.Submit', N'Info', @MessageId, @CorrelationId,
                CONCAT(@No, N' ERP kuyruğuna alındı (', @SubmittedBy, N').'));

        COMMIT TRANSACTION;

        SELECT @MessageId AS MessageId, CAST(0 AS bit) AS AlreadyQueued;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;   -- re-raise the original error number and message to the caller
    END CATCH;
END;
GO

-- Apply requisition states read from the ERP (JSON array) to internal requests.
-- Status only moves forward, so an old/replayed ERP event can never roll a request back.
CREATE OR ALTER PROCEDURE dbo.usp_MaterialRequest_ApplyErpStates
    @Json nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Changes TABLE (RequestNo nvarchar(20), OldStatus nvarchar(20), NewStatus nvarchar(20), ErpNo nvarchar(30));

    WITH Src AS
    (
        SELECT  j.RequisitionNo,
                j.ExternalRef,
                j.Objstate,
                CASE j.Objstate
                    WHEN N'Released'   THEN N'SentToErp'
                    WHEN N'Authorized' THEN N'ApprovedInErp'
                    WHEN N'Closed'     THEN N'Received'
                    WHEN N'Cancelled'  THEN N'Rejected'
                END AS NewStatus
        FROM    OPENJSON(@Json)
                WITH (RequisitionNo nvarchar(30)  '$.RequisitionNo',
                      ExternalRef   nvarchar(20)  '$.ExternalRef',
                      Objstate      nvarchar(30)  '$.Objstate') AS j
    ),
    Ranked AS
    (
        SELECT  s.*,
                CASE s.NewStatus WHEN N'SentToErp' THEN 2 WHEN N'ApprovedInErp' THEN 3
                                 WHEN N'Received' THEN 4 WHEN N'Rejected' THEN 4 END AS NewRank
        FROM    Src AS s
        WHERE   s.NewStatus IS NOT NULL
    )
    UPDATE  r
    SET     r.Status           = x.NewStatus,
            r.ErpState         = x.Objstate,
            r.ErpRequisitionNo = x.RequisitionNo,
            r.UpdatedAt        = SYSUTCDATETIME()
    OUTPUT  inserted.No, deleted.Status, inserted.Status, inserted.ErpRequisitionNo INTO @Changes
    FROM    dbo.MaterialRequests AS r
    JOIN    Ranked               AS x ON x.ExternalRef = r.No      -- business key, survives a lost ERP number
    WHERE   r.Status <> N'Draft'
      AND   x.NewRank > CASE r.Status WHEN N'Failed' THEN 0 WHEN N'Submitted' THEN 1 WHEN N'SentToErp' THEN 2
                                      WHEN N'ApprovedInErp' THEN 3 ELSE 4 END;

    INSERT integration.IntegrationLogs (Direction, Operation, Level, Message)
    SELECT N'IN', N'Requisition.StateSync', N'Info',
           CONCAT(RequestNo, N': ', OldStatus, N' → ', NewStatus, N' (ERP ', ErpNo, N')')
    FROM   @Changes;

    SELECT COUNT(*) AS UpdatedCount FROM @Changes;
END;
