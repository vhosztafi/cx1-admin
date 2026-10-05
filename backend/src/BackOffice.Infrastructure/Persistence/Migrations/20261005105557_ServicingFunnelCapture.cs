using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingFunnelCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FunnelStateJson",
                table: "ServicingRevision",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingRevision_FunnelState",
                table: "ServicingRevision",
                sql: "[FunnelStateJson] IS NULL OR (ISJSON([FunnelStateJson],OBJECT)=1 AND DATALENGTH([FunnelStateJson])<=2097152)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingRevision_FunnelState",
                table: "ServicingRevision");

            migrationBuilder.DropColumn(
                name: "FunnelStateJson",
                table: "ServicingRevision");
        }
    }
}
