-- Upsert ERP stock balances from a table-valued parameter (one round trip per page).
-- MERGE ... WITH (HOLDLOCK) avoids the classic MERGE race (two sessions inserting the same key).
-- Idempotent: replaying the same page changes nothing (rows are only touched when values differ).
CREATE OR ALTER PROCEDURE dbo.usp_StockBalance_Upsert
    @Rows dbo.StockBalanceRow READONLY
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Actions TABLE (Action nvarchar(10));
    DECLARE @Now datetime2(3) = SYSUTCDATETIME();

    MERGE dbo.StockBalances WITH (HOLDLOCK) AS t
    USING
    (
        SELECT  m.Id AS MaterialId, r.Site, r.LocationNo, r.QtyOnHand, r.QtyReserved, r.ErpChangedAt
        FROM    @Rows AS r
        JOIN    dbo.Materials AS m ON m.PartNo = r.PartNo
    ) AS s
        ON  t.MaterialId = s.MaterialId
        AND t.Site       = s.Site
        AND t.LocationNo = s.LocationNo
    WHEN MATCHED AND (t.QtyOnHand <> s.QtyOnHand OR t.QtyReserved <> s.QtyReserved OR t.ErpChangedAt < s.ErpChangedAt) THEN
        UPDATE SET QtyOnHand = s.QtyOnHand, QtyReserved = s.QtyReserved, ErpChangedAt = s.ErpChangedAt, SyncedAt = @Now
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (MaterialId, Site, LocationNo, QtyOnHand, QtyReserved, ErpChangedAt, SyncedAt)
        VALUES (s.MaterialId, s.Site, s.LocationNo, s.QtyOnHand, s.QtyReserved, s.ErpChangedAt, @Now)
    OUTPUT $action INTO @Actions;

    SELECT  (SELECT COUNT(*) FROM @Actions WHERE Action = N'INSERT') AS InsertedCount,
            (SELECT COUNT(*) FROM @Actions WHERE Action = N'UPDATE') AS UpdatedCount,
            (SELECT COUNT(*) FROM @Rows AS r
              WHERE NOT EXISTS (SELECT 1 FROM dbo.Materials AS m WHERE m.PartNo = r.PartNo)) AS UnknownPartCount;
END;
GO

-- Upsert the ERP part catalogue from a JSON array (OPENJSON shreds it into rows).
CREATE OR ALTER PROCEDURE dbo.usp_Material_UpsertFromErp
    @Json nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Actions TABLE (Action nvarchar(10));
    DECLARE @Now datetime2(3) = SYSUTCDATETIME();

    MERGE dbo.Materials WITH (HOLDLOCK) AS t
    USING
    (
        SELECT  j.PartNo,
                j.Description,
                j.UnitMeas,
                j.PartCategory,
                j.SafetyStock,
                CASE WHEN j.PartStatus = N'A' THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS IsBlocked,
                -- ISO 8601 with "Z" is read as datetimeoffset, then normalised to UTC datetime2
                CONVERT(datetime2(3), SWITCHOFFSET(j.ChangedAt, N'+00:00')) AS ChangedAt
        FROM    OPENJSON(@Json)
                WITH (PartNo       nvarchar(40)   '$.PartNo',
                      Description  nvarchar(200)  '$.Description',
                      UnitMeas     nvarchar(10)   '$.UnitMeas',
                      PartCategory nvarchar(50)   '$.PartCategory',
                      SafetyStock  decimal(18, 3) '$.SafetyStock',
                      PartStatus   nvarchar(10)   '$.PartStatus',
                      ChangedAt    datetimeoffset '$.ChangedAt') AS j
    ) AS s
        ON t.PartNo = s.PartNo
    WHEN MATCHED AND t.ErpChangedAt < s.ChangedAt THEN
        UPDATE SET Description = s.Description, Unit = s.UnitMeas, Category = s.PartCategory,
                   SafetyStock = s.SafetyStock, IsBlockedInErp = s.IsBlocked, ErpChangedAt = s.ChangedAt, SyncedAt = @Now
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (PartNo, Description, Unit, Category, SafetyStock, IsBlockedInErp, ErpChangedAt, SyncedAt)
        VALUES (s.PartNo, s.Description, s.UnitMeas, s.PartCategory, s.SafetyStock, s.IsBlocked, s.ChangedAt, @Now)
    OUTPUT $action INTO @Actions;

    SELECT  (SELECT COUNT(*) FROM @Actions WHERE Action = N'INSERT') AS InsertedCount,
            (SELECT COUNT(*) FROM @Actions WHERE Action = N'UPDATE') AS UpdatedCount,
            0 AS UnknownPartCount;
END;
