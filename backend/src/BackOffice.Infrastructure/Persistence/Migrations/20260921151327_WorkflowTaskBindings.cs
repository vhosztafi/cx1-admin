using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowTaskBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_OperationalTask_Id_SubjectId",
                table: "OperationalTask",
                columns: new[] { "Id", "SubjectId" });

            migrationBuilder.CreateTable(
                name: "WorkflowTaskBinding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServicingReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuoteQueryDecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServicingQueryDecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MatchInformationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AgencyFollowUpId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobExceptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RuleSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RuleHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTaskBinding", x => x.Id);
                    table.CheckConstraint("CK_WorkflowTaskBinding_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_WorkflowTaskBinding_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_WorkflowTaskBinding_Hashes", "LEN([SourceHash])=64 AND LEN([RuleHash])=64 AND [SourceHash] NOT LIKE '%[^0-9A-F]%' COLLATE Latin1_General_100_BIN2 AND [RuleHash] NOT LIKE '%[^0-9A-F]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_WorkflowTaskBinding_Snapshots", "ISJSON([SourceSnapshotJson],OBJECT)=1 AND ISJSON([RuleSnapshotJson],OBJECT)=1");
                    table.CheckConstraint("CK_WorkflowTaskBinding_Source", "([SourceKind]='quote-referral' AND [QuoteReferralId] IS NOT NULL AND [SourceEventId]=[QuoteReferralId] AND [ServicingReferralId] IS NULL AND [QuoteQueryDecisionId] IS NULL AND [ServicingQueryDecisionId] IS NULL AND [MatchInformationRequestId] IS NULL AND [PolicyTermId] IS NULL AND [AgencyFollowUpId] IS NULL AND [JobExceptionId] IS NULL) OR ([SourceKind]='servicing-referral' AND [ServicingReferralId] IS NOT NULL AND [SourceEventId]=[ServicingReferralId] AND [QuoteReferralId] IS NULL AND [QuoteQueryDecisionId] IS NULL AND [ServicingQueryDecisionId] IS NULL AND [MatchInformationRequestId] IS NULL AND [PolicyTermId] IS NULL AND [AgencyFollowUpId] IS NULL AND [JobExceptionId] IS NULL) OR ([SourceKind]='quote-query' AND [QuoteQueryDecisionId] IS NOT NULL AND [SourceEventId]=[QuoteQueryDecisionId] AND [QuoteReferralId] IS NULL AND [ServicingReferralId] IS NULL AND [ServicingQueryDecisionId] IS NULL AND [MatchInformationRequestId] IS NULL AND [PolicyTermId] IS NULL AND [AgencyFollowUpId] IS NULL AND [JobExceptionId] IS NULL) OR ([SourceKind]='servicing-query' AND [ServicingQueryDecisionId] IS NOT NULL AND [SourceEventId]=[ServicingQueryDecisionId] AND [QuoteReferralId] IS NULL AND [ServicingReferralId] IS NULL AND [QuoteQueryDecisionId] IS NULL AND [MatchInformationRequestId] IS NULL AND [PolicyTermId] IS NULL AND [AgencyFollowUpId] IS NULL AND [JobExceptionId] IS NULL) OR ([SourceKind]='match-information-request' AND [MatchInformationRequestId] IS NOT NULL AND [SourceEventId]=[MatchInformationRequestId] AND [QuoteReferralId] IS NULL AND [ServicingReferralId] IS NULL AND [QuoteQueryDecisionId] IS NULL AND [ServicingQueryDecisionId] IS NULL AND [PolicyTermId] IS NULL AND [AgencyFollowUpId] IS NULL AND [JobExceptionId] IS NULL) OR ([SourceKind]='policy-term' AND [PolicyTermId] IS NOT NULL AND [SourceEventId]=[PolicyTermId] AND [QuoteReferralId] IS NULL AND [ServicingReferralId] IS NULL AND [QuoteQueryDecisionId] IS NULL AND [ServicingQueryDecisionId] IS NULL AND [MatchInformationRequestId] IS NULL AND [AgencyFollowUpId] IS NULL AND [JobExceptionId] IS NULL) OR ([SourceKind]='agency-follow-up' AND [AgencyFollowUpId] IS NOT NULL AND [SourceEventId]=[AgencyFollowUpId] AND [QuoteReferralId] IS NULL AND [ServicingReferralId] IS NULL AND [QuoteQueryDecisionId] IS NULL AND [ServicingQueryDecisionId] IS NULL AND [MatchInformationRequestId] IS NULL AND [PolicyTermId] IS NULL AND [JobExceptionId] IS NULL) OR ([SourceKind]='job-exception' AND [JobExceptionId] IS NOT NULL AND [SourceEventId]=[JobExceptionId] AND [QuoteReferralId] IS NULL AND [ServicingReferralId] IS NULL AND [QuoteQueryDecisionId] IS NULL AND [ServicingQueryDecisionId] IS NULL AND [MatchInformationRequestId] IS NULL AND [PolicyTermId] IS NULL AND [AgencyFollowUpId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_WorkflowTaskBinding_AgencyFollowUp_AgencyFollowUpId",
                        column: x => x.AgencyFollowUpId,
                        principalTable: "AgencyFollowUp",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowTaskBinding_JobException_JobExceptionId",
                        column: x => x.JobExceptionId,
                        principalTable: "JobException",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowTaskBinding_MatchInformationRequest_MatchInformationRequestId",
                        column: x => x.MatchInformationRequestId,
                        principalTable: "MatchInformationRequest",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowTaskBinding_OperationalTask_TaskId_SubjectId",
                        columns: x => new { x.TaskId, x.SubjectId },
                        principalTable: "OperationalTask",
                        principalColumns: new[] { "Id", "SubjectId" });
                    table.ForeignKey(
                        name: "FK_WorkflowTaskBinding_PolicyTerm_PolicyTermId",
                        column: x => x.PolicyTermId,
                        principalTable: "PolicyTerm",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowTaskBinding_QuoteReferralDecision_QuoteQueryDecisionId",
                        column: x => x.QuoteQueryDecisionId,
                        principalTable: "QuoteReferralDecision",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowTaskBinding_QuoteReferral_QuoteReferralId",
                        column: x => x.QuoteReferralId,
                        principalTable: "QuoteReferral",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowTaskBinding_ServicingReferralDecision_ServicingQueryDecisionId",
                        column: x => x.ServicingQueryDecisionId,
                        principalTable: "ServicingReferralDecision",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowTaskBinding_ServicingReferral_ServicingReferralId",
                        column: x => x.ServicingReferralId,
                        principalTable: "ServicingReferral",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowTaskBinding_SettingVersion_RuleVersionId",
                        column: x => x.RuleVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowTaskBinding_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_AgencyFollowUpId",
                table: "WorkflowTaskBinding",
                column: "AgencyFollowUpId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_CreatedBy",
                table: "WorkflowTaskBinding",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_JobExceptionId",
                table: "WorkflowTaskBinding",
                column: "JobExceptionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_MatchInformationRequestId",
                table: "WorkflowTaskBinding",
                column: "MatchInformationRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_PolicyTermId",
                table: "WorkflowTaskBinding",
                column: "PolicyTermId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_QuoteQueryDecisionId",
                table: "WorkflowTaskBinding",
                column: "QuoteQueryDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_QuoteReferralId",
                table: "WorkflowTaskBinding",
                column: "QuoteReferralId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_RuleCode_SourceKind_SourceEventId",
                table: "WorkflowTaskBinding",
                columns: new[] { "RuleCode", "SourceKind", "SourceEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_RuleVersionId",
                table: "WorkflowTaskBinding",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_ServicingQueryDecisionId",
                table: "WorkflowTaskBinding",
                column: "ServicingQueryDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_ServicingReferralId",
                table: "WorkflowTaskBinding",
                column: "ServicingReferralId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_TaskId",
                table: "WorkflowTaskBinding",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTaskBinding_TaskId_SubjectId",
                table: "WorkflowTaskBinding",
                columns: new[] { "TaskId", "SubjectId" });
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_WorkflowTaskBinding_Immutable ON WorkflowTaskBinding AFTER UPDATE,DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted) THROW 51985,'Workflow task provenance is append-only.',1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM WorkflowTaskBinding) THROW 51986,'Workflow task history cannot be discarded by downgrade.',1;");
            migrationBuilder.DropTable(
                name: "WorkflowTaskBinding");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OperationalTask_Id_SubjectId",
                table: "OperationalTask");
        }
    }
}
