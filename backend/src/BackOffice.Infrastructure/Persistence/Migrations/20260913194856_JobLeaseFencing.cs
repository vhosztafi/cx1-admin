using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobLeaseFencing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "OutboxWork",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "OutboxWork",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "OutboxWork",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScenarioVersionId",
                table: "OutboxWork",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobException",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobException", x => x.Id);
                    table.CheckConstraint("CK_JobException_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_JobException_OccurredAt_Utc", "DATEPART(TZOFFSET,[OccurredAt]) = 0");
                    table.ForeignKey(
                        name: "FK_JobException_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_JobException_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxWork_ScenarioVersionId",
                table: "OutboxWork",
                column: "ScenarioVersionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxWork_CompletedAt_Utc",
                table: "OutboxWork",
                sql: "DATEPART(TZOFFSET,[CompletedAt]) = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxWork_DiagnosticScenario",
                table: "OutboxWork",
                sql: "[Kind] <> 'diagnostic-probe' OR [ScenarioVersionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JobException_CreatedBy",
                table: "JobException",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_JobException_WorkId",
                table: "JobException",
                column: "WorkId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OutboxWork_SettingVersion_ScenarioVersionId",
                table: "OutboxWork",
                column: "ScenarioVersionId",
                principalTable: "SettingVersion",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OutboxWork_SettingVersion_ScenarioVersionId",
                table: "OutboxWork");

            migrationBuilder.DropTable(
                name: "JobException");

            migrationBuilder.DropIndex(
                name: "IX_OutboxWork_ScenarioVersionId",
                table: "OutboxWork");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxWork_CompletedAt_Utc",
                table: "OutboxWork");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxWork_DiagnosticScenario",
                table: "OutboxWork");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "OutboxWork");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "OutboxWork");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "OutboxWork");

            migrationBuilder.DropColumn(
                name: "ScenarioVersionId",
                table: "OutboxWork");
        }
    }
}
