-- Stock picture per material: ERP on-hand minus ERP reservations, plus what is already on order
-- through open internal material requests (not yet received).
CREATE OR ALTER VIEW dbo.vw_StockStatus
AS
WITH Stock AS
(
    SELECT  sb.MaterialId,
            SUM(sb.QtyOnHand)   AS QtyOnHand,
            SUM(sb.QtyReserved) AS QtyReserved,
            MAX(sb.SyncedAt)    AS LastSyncedAt
    FROM    dbo.StockBalances AS sb
    GROUP BY sb.MaterialId
),
OnOrder AS
(
    SELECT  l.MaterialId,
            SUM(l.Quantity) AS QtyOnOrder
    FROM    dbo.MaterialRequestLines AS l
    JOIN    dbo.MaterialRequests     AS r ON r.Id = l.MaterialRequestId
    WHERE   r.Status IN (N'Submitted', N'SentToErp', N'ApprovedInErp')
    GROUP BY l.MaterialId
)
SELECT  m.Id                                                AS MaterialId,
        m.PartNo,
        m.Description,
        m.Unit,
        m.Category,
        m.IsBlockedInErp,
        m.SafetyStock,
        ISNULL(s.QtyOnHand, 0)                              AS QtyOnHand,
        ISNULL(s.QtyReserved, 0)                            AS QtyReservedInErp,
        ISNULL(s.QtyOnHand, 0) - ISNULL(s.QtyReserved, 0)   AS QtyAvailable,
        ISNULL(o.QtyOnOrder, 0)                             AS QtyOnOrder,
        CASE
            WHEN ISNULL(s.QtyOnHand, 0) - ISNULL(s.QtyReserved, 0) <= 0            THEN N'OUT'
            WHEN ISNULL(s.QtyOnHand, 0) - ISNULL(s.QtyReserved, 0) < m.SafetyStock THEN N'LOW'
            ELSE N'OK'
        END                                                 AS StockLevel,
        s.LastSyncedAt
FROM    dbo.Materials AS m
LEFT JOIN Stock   AS s ON s.MaterialId = m.Id
LEFT JOIN OnOrder AS o ON o.MaterialId = m.Id;
GO

-- One-row health summary for the dashboard and the monitoring panel.
CREATE OR ALTER VIEW integration.vw_IntegrationHealth
AS
SELECT
    (SELECT COUNT(*) FROM integration.OutboxMessages WHERE Status = N'Pending')    AS PendingCount,
    (SELECT COUNT(*) FROM integration.OutboxMessages WHERE Status = N'Processing') AS ProcessingCount,
    (SELECT COUNT(*) FROM integration.OutboxMessages WHERE Status = N'Failed')     AS FailedCount,
    (SELECT COUNT(*) FROM integration.OutboxMessages
      WHERE Status = N'Processed' AND ProcessedAt >= DATEADD(HOUR, -24, SYSUTCDATETIME()))       AS ProcessedLast24h,
    (SELECT DATEDIFF(SECOND, MIN(CreatedAt), SYSUTCDATETIME()) FROM integration.OutboxMessages
      WHERE Status IN (N'Pending', N'Processing'))                                                AS OldestPendingAgeSeconds,
    (SELECT COUNT(*) FROM integration.IntegrationLogs
      WHERE Level = N'Error' AND OccurredAt >= DATEADD(HOUR, -1, SYSUTCDATETIME()))               AS ErrorsLastHour,
    (SELECT MAX(OccurredAt) FROM integration.IntegrationLogs WHERE Level = N'Error')             AS LastErrorAt,
    (SELECT LastSuccessAt FROM integration.SyncStates WHERE Name = N'ErpStock')                   AS LastStockSyncAt;
