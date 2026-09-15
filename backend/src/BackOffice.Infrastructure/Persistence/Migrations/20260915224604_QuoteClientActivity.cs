using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteClientActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ClientActivity_RecordKind",
                table: "ClientActivity");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClientActivity_RecordKind",
                table: "ClientActivity",
                sql: "[RecordKind] IS NULL OR [RecordKind] IN ('client','contact','match','quote')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ClientActivity_RecordKind",
                table: "ClientActivity");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClientActivity_RecordKind",
                table: "ClientActivity",
                sql: "[RecordKind] IS NULL OR [RecordKind] IN ('client','contact','match')");
        }
    }
}
