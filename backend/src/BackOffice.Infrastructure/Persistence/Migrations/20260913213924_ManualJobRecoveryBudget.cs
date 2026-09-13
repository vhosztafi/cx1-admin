using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManualJobRecoveryBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptLimit",
                table: "OutboxWork",
                type: "int",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxWork_AttemptLimit",
                table: "OutboxWork",
                sql: "[AttemptLimit] IN (6,12,18) AND [Attempts] <= [AttemptLimit]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxWork_AttemptLimit",
                table: "OutboxWork");

            migrationBuilder.DropColumn(
                name: "AttemptLimit",
                table: "OutboxWork");
        }
    }
}
