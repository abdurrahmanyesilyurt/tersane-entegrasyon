using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tersane.Data.Migrations
{
    /// <summary>
    /// Applies the T-SQL programmability scripts in Sql/*.sql (types, views, functions, procedures).
    /// Scripts use CREATE OR ALTER, so a later migration can simply re-run them after a change.
    /// </summary>
    public partial class Programmability : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (_, sql) in SqlScripts.All())
                migrationBuilder.Sql(sql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP PROCEDURE IF EXISTS dbo.usp_Material_UpsertFromErp;
                DROP PROCEDURE IF EXISTS dbo.usp_StockBalance_Upsert;
                DROP PROCEDURE IF EXISTS integration.usp_IntegrationLog_Purge;
                DROP PROCEDURE IF EXISTS integration.usp_Outbox_Requeue;
                DROP PROCEDURE IF EXISTS integration.usp_Outbox_Claim;
                DROP PROCEDURE IF EXISTS dbo.usp_MaterialRequest_ApplyErpStates;
                DROP PROCEDURE IF EXISTS dbo.usp_MaterialRequest_Submit;
                DROP FUNCTION IF EXISTS dbo.fn_ProjectMaterialSummary;
                DROP VIEW IF EXISTS integration.vw_IntegrationHealth;
                DROP VIEW IF EXISTS dbo.vw_StockStatus;
                DROP TYPE IF EXISTS dbo.StockBalanceRow;
                """);
        }
    }
}
