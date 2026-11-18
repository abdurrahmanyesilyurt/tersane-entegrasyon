using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tersane.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integration");

            migrationBuilder.CreateSequence<int>(
                name: "seq_MaterialRequest");

            migrationBuilder.CreateSequence<int>(
                name: "seq_WorkOrder");

            migrationBuilder.CreateTable(
                name: "IntegrationLogs",
                schema: "integration",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    Direction = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Level = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    HttpMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    Attempt = table.Column<int>(type: "int", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestBody = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Materials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SafetyStock = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    IsBlockedInErp = table.Column<bool>(type: "bit", nullable: false),
                    ErpChangedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "integration",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AggregateType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LockedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExternalRef = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HullNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ShipType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedDelivery = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncStates",
                schema: "integration",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Watermark = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastSuccessAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastRowCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "StockBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaterialId = table.Column<int>(type: "int", nullable: false),
                    Site = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LocationNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    QtyOnHand = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    QtyReserved = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ErpChangedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockBalances_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Blocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Zone = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WeightTon = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Blocks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeqNo = table.Column<int>(type: "int", nullable: false, defaultValueSql: "NEXT VALUE FOR dbo.seq_WorkOrder"),
                    No = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, computedColumnSql: "N'IE-' + CONVERT(nchar(4), YEAR([CreatedAt])) + N'-' + RIGHT(N'0000' + CONVERT(nvarchar(10), [SeqNo]), 4)", stored: true),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    BlockId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PlannedStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    EstimatedManHours = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaterialRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeqNo = table.Column<int>(type: "int", nullable: false, defaultValueSql: "NEXT VALUE FOR dbo.seq_MaterialRequest"),
                    No = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, computedColumnSql: "N'MT-' + CONVERT(nchar(4), YEAR([CreatedAt])) + N'-' + RIGHT(N'00000' + CONVERT(nvarchar(10), [SeqNo]), 5)", stored: true),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    BlockId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    RequestedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NeedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ErpRequisitionNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ErpState = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialRequests_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialRequests_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialRequests_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaterialRequestLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaterialRequestId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    MaterialId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialRequestLines", x => x.Id);
                    table.CheckConstraint("CK_MaterialRequestLines_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_MaterialRequestLines_MaterialRequests_MaterialRequestId",
                        column: x => x.MaterialRequestId,
                        principalTable: "MaterialRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaterialRequestLines_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_ProjectId_Code",
                table: "Blocks",
                columns: new[] { "ProjectId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationLogs_MessageId",
                schema: "integration",
                table: "IntegrationLogs",
                column: "MessageId",
                filter: "[MessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationLogs_OccurredAt",
                schema: "integration",
                table: "IntegrationLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequestLines_MaterialId",
                table: "MaterialRequestLines",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequestLines_MaterialRequestId_LineNo",
                table: "MaterialRequestLines",
                columns: new[] { "MaterialRequestId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequests_BlockId",
                table: "MaterialRequests",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequests_ErpRequisitionNo",
                table: "MaterialRequests",
                column: "ErpRequisitionNo",
                filter: "[ErpRequisitionNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequests_No",
                table: "MaterialRequests",
                column: "No",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequests_ProjectId",
                table: "MaterialRequests",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequests_Status_CreatedAt",
                table: "MaterialRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequests_WorkOrderId",
                table: "MaterialRequests",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_PartNo",
                table: "Materials",
                column: "PartNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_AggregateType_AggregateId",
                schema: "integration",
                table: "OutboxMessages",
                columns: new[] { "AggregateType", "AggregateId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_IdempotencyKey",
                schema: "integration",
                table: "OutboxMessages",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_MessageId",
                schema: "integration",
                table: "OutboxMessages",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending",
                schema: "integration",
                table: "OutboxMessages",
                column: "NextAttemptAt",
                filter: "[Status] = N'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_HullNo",
                table: "Projects",
                column: "HullNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockBalances_MaterialId_Site_LocationNo",
                table: "StockBalances",
                columns: new[] { "MaterialId", "Site", "LocationNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_BlockId",
                table: "WorkOrders",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_No",
                table: "WorkOrders",
                column: "No",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ProjectId_Status",
                table: "WorkOrders",
                columns: new[] { "ProjectId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegrationLogs",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "MaterialRequestLines");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "StockBalances");

            migrationBuilder.DropTable(
                name: "SyncStates",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "MaterialRequests");

            migrationBuilder.DropTable(
                name: "Materials");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.DropTable(
                name: "Blocks");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropSequence(
                name: "seq_MaterialRequest");

            migrationBuilder.DropSequence(
                name: "seq_WorkOrder");
        }
    }
}
