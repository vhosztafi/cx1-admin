using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenewalLapseLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RenewalLapseEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleSettingVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AutoLapseAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecipientSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalLapseEvent", x => x.Id);
                    table.UniqueConstraint("AK_RenewalLapseEvent_Id_WorkId", x => new { x.Id, x.WorkId });
                    table.CheckConstraint("CK_RenewalLapseEvent_AutoLapseAt_Utc", "DATEPART(TZOFFSET,[AutoLapseAt]) = 0");
                    table.CheckConstraint("CK_RenewalLapseEvent_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_RenewalLapseEvent_Dates", "[AutoLapseAt]>=[EffectiveAt] AND DATEPART(TZOFFSET,[EffectiveAt])=0 AND DATEPART(TZOFFSET,[AutoLapseAt])=0");
                    table.CheckConstraint("CK_RenewalLapseEvent_EffectiveAt_Utc", "DATEPART(TZOFFSET,[EffectiveAt]) = 0");
                    table.CheckConstraint("CK_RenewalLapseEvent_Mode", "([Mode]='manual' AND [CreatedBy] IS NOT NULL) OR ([Mode]='automatic' AND [CreatedBy] IS NULL AND [CreatedAt]>=[AutoLapseAt])");
                    table.CheckConstraint("CK_RenewalLapseEvent_Reason", "LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
                    table.CheckConstraint("CK_RenewalLapseEvent_Recipients", "ISJSON([RecipientSnapshotJson],ARRAY)=1");
                    table.ForeignKey(
                        name: "FK_RenewalLapseEvent_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RenewalLapseEvent_PolicyTerm_TermId_PolicyId",
                        columns: x => new { x.TermId, x.PolicyId },
                        principalTable: "PolicyTerm",
                        principalColumns: new[] { "Id", "PolicyId" });
                    table.ForeignKey(
                        name: "FK_RenewalLapseEvent_SettingVersion_RuleSettingVersionId",
                        column: x => x.RuleSettingVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RenewalLapseEvent_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RenewalLapseNotificationReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LapseEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalLapseNotificationReceipt", x => x.Id);
                    table.CheckConstraint("CK_RenewalLapseNotificationReceipt_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_RenewalLapseNotificationReceipt_Outcome", "[Outcome] IN ('demo-delivered','demo-no-recipient') AND DATALENGTH([PayloadHash])=32");
                    table.ForeignKey(
                        name: "FK_RenewalLapseNotificationReceipt_RenewalLapseEvent_LapseEventId_WorkId",
                        columns: x => new { x.LapseEventId, x.WorkId },
                        principalTable: "RenewalLapseEvent",
                        principalColumns: new[] { "Id", "WorkId" });
                    table.ForeignKey(
                        name: "FK_RenewalLapseNotificationReceipt_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalLapseEvent_CreatedBy",
                table: "RenewalLapseEvent",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalLapseEvent_RuleSettingVersionId",
                table: "RenewalLapseEvent",
                column: "RuleSettingVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalLapseEvent_TermId",
                table: "RenewalLapseEvent",
                column: "TermId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalLapseEvent_TermId_PolicyId",
                table: "RenewalLapseEvent",
                columns: new[] { "TermId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalLapseEvent_WorkId",
                table: "RenewalLapseEvent",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalLapseNotificationReceipt_CreatedBy",
                table: "RenewalLapseNotificationReceipt",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalLapseNotificationReceipt_LapseEventId",
                table: "RenewalLapseNotificationReceipt",
                column: "LapseEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalLapseNotificationReceipt_LapseEventId_WorkId",
                table: "RenewalLapseNotificationReceipt",
                columns: new[] { "LapseEventId", "WorkId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalLapseNotificationReceipt_WorkId",
                table: "RenewalLapseNotificationReceipt",
                column: "WorkId",
                unique: true);
            AddLapseGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM RenewalLapseEvent) THROW 51765,'Retained lapse history cannot be downgraded.',1;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_RenewalLapse_Acceptance; DROP TRIGGER IF EXISTS TR_RenewalLapse_IssueDecision; DROP TRIGGER IF EXISTS TR_RenewalLapse_WorkInput;");
            migrationBuilder.DropTable(
                name: "RenewalLapseNotificationReceipt");

            migrationBuilder.DropTable(
                name: "RenewalLapseEvent");
        }
    }
}
