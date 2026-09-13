using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DiagnosticInboxReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "OutboxWork",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "AdapterQuarantine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObservedHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdapterQuarantine", x => x.Id);
                    table.CheckConstraint("CK_AdapterQuarantine_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AdapterQuarantine_ReceivedAt_Utc", "DATEPART(TZOFFSET,[ReceivedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_AdapterQuarantine_AdapterInbox_InboxId",
                        column: x => x.InboxId,
                        principalTable: "AdapterInbox",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdapterQuarantine_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DiagnosticReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticReceipt", x => x.Id);
                    table.CheckConstraint("CK_DiagnosticReceipt_CompletedAt_Utc", "DATEPART(TZOFFSET,[CompletedAt]) = 0");
                    table.CheckConstraint("CK_DiagnosticReceipt_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_DiagnosticReceipt_DemoProviderOperation_ProviderOperationId",
                        column: x => x.ProviderOperationId,
                        principalTable: "DemoProviderOperation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DiagnosticReceipt_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DiagnosticReceipt_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdapterQuarantine_CreatedBy",
                table: "AdapterQuarantine",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AdapterQuarantine_InboxId_ObservedHash",
                table: "AdapterQuarantine",
                columns: new[] { "InboxId", "ObservedHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticReceipt_CreatedBy",
                table: "DiagnosticReceipt",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticReceipt_ProviderOperationId",
                table: "DiagnosticReceipt",
                column: "ProviderOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticReceipt_WorkId",
                table: "DiagnosticReceipt",
                column: "WorkId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdapterQuarantine");

            migrationBuilder.DropTable(
                name: "DiagnosticReceipt");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "OutboxWork");
        }
    }
}
