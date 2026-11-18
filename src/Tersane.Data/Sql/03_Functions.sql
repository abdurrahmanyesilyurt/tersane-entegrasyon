-- Inline table-valued function: material demand of one ship project.
-- Inline (single SELECT) TVFs are expanded into the caller's plan like a view, unlike
-- multi-statement TVFs or scalar UDFs; EF Core maps it with HasDbFunction and can compose on it.
CREATE OR ALTER FUNCTION dbo.fn_ProjectMaterialSummary (@ProjectId int)
RETURNS TABLE
AS
RETURN
(
    SELECT  m.PartNo,
            m.Description,
            m.Unit,
            SUM(l.Quantity)                                                                         AS QtyRequested,
            SUM(CASE WHEN r.Status IN (N'Submitted', N'SentToErp', N'ApprovedInErp', N'Failed')
                     THEN l.Quantity ELSE 0 END)                                                    AS QtyOpen,
            SUM(CASE WHEN r.Status = N'Received' THEN l.Quantity ELSE 0 END)                        AS QtyReceived,
            COUNT(DISTINCT r.Id)                                                                    AS RequestCount
    FROM    dbo.MaterialRequests     AS r
    JOIN    dbo.MaterialRequestLines AS l ON l.MaterialRequestId = r.Id
    JOIN    dbo.Materials            AS m ON m.Id = l.MaterialId
    WHERE   r.ProjectId = @ProjectId
      AND   r.Status NOT IN (N'Draft', N'Rejected')
    GROUP BY m.PartNo, m.Description, m.Unit
);
