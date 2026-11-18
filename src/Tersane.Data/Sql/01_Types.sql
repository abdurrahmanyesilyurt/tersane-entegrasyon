-- Table-valued parameter type for the ERP stock sync.
-- The worker sends one batch of rows in a single round trip instead of N INSERT/UPDATE calls.
IF TYPE_ID(N'dbo.StockBalanceRow') IS NULL
    CREATE TYPE dbo.StockBalanceRow AS TABLE
    (
        PartNo        nvarchar(40)   NOT NULL,
        Site          nvarchar(10)   NOT NULL,
        LocationNo    nvarchar(20)   NOT NULL,
        QtyOnHand     decimal(18, 3) NOT NULL,
        QtyReserved   decimal(18, 3) NOT NULL,
        ErpChangedAt  datetime2(3)   NOT NULL,
        PRIMARY KEY (PartNo, Site, LocationNo)
    );
