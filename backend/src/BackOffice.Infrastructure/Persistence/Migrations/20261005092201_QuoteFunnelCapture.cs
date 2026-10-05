using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteFunnelCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FunnelStateJson",
                table: "QuoteRevision",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrokerContactJson",
                table: "Quote",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_QuoteRevision_FunnelStateJson",
                table: "QuoteRevision",
                sql: "[FunnelStateJson] IS NULL OR (ISJSON([FunnelStateJson], OBJECT)=1 AND DATALENGTH([FunnelStateJson])<=2097152)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Quote_BrokerContactJson",
                table: "Quote",
                sql: "[BrokerContactJson] IS NULL OR ISJSON([BrokerContactJson], OBJECT)=1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_QuoteRevision_FunnelStateJson",
                table: "QuoteRevision");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Quote_BrokerContactJson",
                table: "Quote");

            migrationBuilder.DropColumn(
                name: "FunnelStateJson",
                table: "QuoteRevision");

            migrationBuilder.DropColumn(
                name: "BrokerContactJson",
                table: "Quote");
        }
    }
}
