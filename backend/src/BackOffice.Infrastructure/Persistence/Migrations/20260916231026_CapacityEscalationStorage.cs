using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CapacityEscalationStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CapacitySubmissionId",
                table: "UnderwritingEvidenceAssociation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CapacityEscalation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BinderVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RaisedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrentSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentResponseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityEscalation", x => x.Id);
                    table.UniqueConstraint("AK_CapacityEscalation_Id_CycleId_QuoteId", x => new { x.Id, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_CapacityEscalation_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CapacityEscalation_Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[RaisedBy]");
                    table.CheckConstraint("CK_CapacityEscalation_ResponseOwner", "[CurrentResponseId] IS NULL OR [CurrentSubmissionId] IS NOT NULL");
                    table.CheckConstraint("CK_CapacityEscalation_State", "[State] IN ('draft','queued','sent','queried','approved','conditional','declined','superseded','failed')");
                    table.CheckConstraint("CK_CapacityEscalation_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_CapacityEscalation_BinderVersion_BinderVersionId",
                        column: x => x.BinderVersionId,
                        principalTable: "BinderVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CapacityEscalation_CapacityProvider_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "CapacityProvider",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CapacityEscalation_QuoteReferral_ReferralId_CycleId_QuoteId",
                        columns: x => new { x.ReferralId, x.CycleId, x.QuoteId },
                        principalTable: "QuoteReferral",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_CapacityEscalation_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CapacityEscalation_User_RaisedBy",
                        column: x => x.RaisedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CapacitySubmission",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EscalationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ContextJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContextHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResponseDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacitySubmission", x => x.Id);
                    table.UniqueConstraint("AK_CapacitySubmission_Id_CycleId_QuoteId", x => new { x.Id, x.CycleId, x.QuoteId });
                    table.UniqueConstraint("AK_CapacitySubmission_Id_EscalationId_CycleId_QuoteId", x => new { x.Id, x.EscalationId, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_CapacitySubmission_ContextHash", "LEN([ContextHash])=64 AND [ContextHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_CapacitySubmission_ContextJson", "ISJSON([ContextJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[ContextJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_CapacitySubmission_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CapacitySubmission_Provenance", "LEN(TRIM([Body]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[SubmittedBy] AND [SubmittedAt]>=[CreatedAt] AND [ResponseDueAt]>[SubmittedAt]");
                    table.CheckConstraint("CK_CapacitySubmission_ResponseDueAt_Utc", "DATEPART(TZOFFSET,[ResponseDueAt]) = 0");
                    table.CheckConstraint("CK_CapacitySubmission_Sequence", "[Sequence]>0");
                    table.CheckConstraint("CK_CapacitySubmission_SubmittedAt_Utc", "DATEPART(TZOFFSET,[SubmittedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_CapacitySubmission_CapacityEscalation_EscalationId_CycleId_QuoteId",
                        columns: x => new { x.EscalationId, x.CycleId, x.QuoteId },
                        principalTable: "CapacityEscalation",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_CapacitySubmission_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CapacitySubmission_SettingVersion_ScenarioVersionId",
                        column: x => x.ScenarioVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CapacitySubmission_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CapacitySubmission_User_SubmittedBy",
                        column: x => x.SubmittedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CapacityMessage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EscalationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Provenance = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ProviderUnderwriter = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProviderReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProviderEventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecordedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceAssociationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EvidenceReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApplicationState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityMessage", x => x.Id);
                    table.UniqueConstraint("AK_CapacityMessage_Id_SubmissionId_EscalationId_CycleId_QuoteId", x => new { x.Id, x.SubmissionId, x.EscalationId, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_CapacityMessage_Application", "[ApplicationState] IN ('applied','superseded')");
                    table.CheckConstraint("CK_CapacityMessage_ContentHash", "DATALENGTH([ContentHash])=32");
                    table.CheckConstraint("CK_CapacityMessage_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CapacityMessage_DefinitionJson", "ISJSON([DefinitionJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[DefinitionJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_CapacityMessage_Direction", "([Direction]='outbound' AND [Provenance]='staff-submission' AND [Outcome] IS NULL AND [ReceivedAt] IS NULL AND [EvidenceAssociationId] IS NULL AND [EvidenceReviewId] IS NULL AND [ProviderEventId] IS NULL AND [InboxId] IS NULL AND [DecisionId] IS NULL) OR ([Direction]='inbound' AND [Provenance] IN ('demo-provider','supplied-response') AND [Outcome] IN ('approve','approve-with-conditions','query','decline') AND [ReceivedAt] IS NOT NULL AND [ReceivedAt]<=[RecordedAt] AND [ProviderUnderwriter] IS NOT NULL AND LEN(TRIM([ProviderUnderwriter]))>0 AND [ProviderReference] IS NOT NULL AND LEN(TRIM([ProviderReference]))>0)");
                    table.CheckConstraint("CK_CapacityMessage_Evidence", "([Provenance]='supplied-response' AND [EvidenceAssociationId] IS NOT NULL AND [EvidenceReviewId] IS NOT NULL AND [ProviderEventId] IS NULL AND [InboxId] IS NULL) OR ([Provenance]<>'supplied-response' AND [EvidenceAssociationId] IS NULL AND [EvidenceReviewId] IS NULL)");
                    table.CheckConstraint("CK_CapacityMessage_Provenance", "LEN(TRIM([Body]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[RecordedBy] AND [RecordedAt]>=[CreatedAt]");
                    table.CheckConstraint("CK_CapacityMessage_ProviderEvent", "([Provenance]='demo-provider' AND [ProviderEventId] IS NOT NULL AND [InboxId] IS NOT NULL) OR ([Provenance]<>'demo-provider' AND [ProviderEventId] IS NULL AND [InboxId] IS NULL)");
                    table.CheckConstraint("CK_CapacityMessage_ReceivedAt_Utc", "DATEPART(TZOFFSET,[ReceivedAt]) = 0");
                    table.CheckConstraint("CK_CapacityMessage_RecordedAt_Utc", "DATEPART(TZOFFSET,[RecordedAt]) = 0");
                    table.CheckConstraint("CK_CapacityMessage_Sequence", "[Sequence]>0");
                    table.ForeignKey(
                        name: "FK_CapacityMessage_AdapterInbox_InboxId",
                        column: x => x.InboxId,
                        principalTable: "AdapterInbox",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CapacityMessage_CapacityProvider_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "CapacityProvider",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CapacityMessage_CapacitySubmission_SubmissionId_EscalationId_CycleId_QuoteId",
                        columns: x => new { x.SubmissionId, x.EscalationId, x.CycleId, x.QuoteId },
                        principalTable: "CapacitySubmission",
                        principalColumns: new[] { "Id", "EscalationId", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_CapacityMessage_QuoteReferralDecision_DecisionId_ReferralId_CycleId_QuoteId",
                        columns: x => new { x.DecisionId, x.ReferralId, x.CycleId, x.QuoteId },
                        principalTable: "QuoteReferralDecision",
                        principalColumns: new[] { "Id", "ReferralId", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_CapacityMessage_QuoteReferral_ReferralId_CycleId_QuoteId",
                        columns: x => new { x.ReferralId, x.CycleId, x.QuoteId },
                        principalTable: "QuoteReferral",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_CapacityMessage_UnderwritingEvidenceEvent_EvidenceReviewId_EvidenceAssociationId_CycleId_QuoteId",
                        columns: x => new { x.EvidenceReviewId, x.EvidenceAssociationId, x.CycleId, x.QuoteId },
                        principalTable: "UnderwritingEvidenceEvent",
                        principalColumns: new[] { "Id", "AssociationId", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_CapacityMessage_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CapacityMessage_User_RecordedBy",
                        column: x => x.RecordedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CapacitySubmissionEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceAssociationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacitySubmissionEvidence", x => x.Id);
                    table.CheckConstraint("CK_CapacitySubmissionEvidence_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_CapacitySubmissionEvidence_CapacitySubmission_SubmissionId_CycleId_QuoteId",
                        columns: x => new { x.SubmissionId, x.CycleId, x.QuoteId },
                        principalTable: "CapacitySubmission",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_CapacitySubmissionEvidence_UnderwritingEvidenceAssociation_EvidenceAssociationId_CycleId_QuoteId",
                        columns: x => new { x.EvidenceAssociationId, x.CycleId, x.QuoteId },
                        principalTable: "UnderwritingEvidenceAssociation",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_CapacitySubmissionEvidence_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceAssociation_CapacitySubmissionId_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation",
                columns: new[] { "CapacitySubmissionId", "CycleId", "QuoteId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_UnderwritingEvidenceAssociation_CapacityPurpose",
                table: "UnderwritingEvidenceAssociation",
                sql: "([CapacitySubmissionId] IS NULL AND [RequirementCode]<>'capacity-response') OR ([CapacitySubmissionId] IS NOT NULL AND [RequirementCode]='capacity-response' AND [ConditionId] IS NULL AND [RiskItemId] IS NULL AND [TermsVersionId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityEscalation_BinderVersionId",
                table: "CapacityEscalation",
                column: "BinderVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityEscalation_CreatedBy",
                table: "CapacityEscalation",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityEscalation_CurrentResponseId_CurrentSubmissionId_Id_CycleId_QuoteId",
                table: "CapacityEscalation",
                columns: new[] { "CurrentResponseId", "CurrentSubmissionId", "Id", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityEscalation_CurrentSubmissionId_Id_CycleId_QuoteId",
                table: "CapacityEscalation",
                columns: new[] { "CurrentSubmissionId", "Id", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityEscalation_ProviderId",
                table: "CapacityEscalation",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityEscalation_RaisedBy",
                table: "CapacityEscalation",
                column: "RaisedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityEscalation_ReferralId",
                table: "CapacityEscalation",
                column: "ReferralId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapacityEscalation_ReferralId_CycleId_QuoteId",
                table: "CapacityEscalation",
                columns: new[] { "ReferralId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityMessage_CreatedBy",
                table: "CapacityMessage",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityMessage_DecisionId_ReferralId_CycleId_QuoteId",
                table: "CapacityMessage",
                columns: new[] { "DecisionId", "ReferralId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityMessage_EscalationId_Sequence",
                table: "CapacityMessage",
                columns: new[] { "EscalationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapacityMessage_EvidenceReviewId_EvidenceAssociationId_CycleId_QuoteId",
                table: "CapacityMessage",
                columns: new[] { "EvidenceReviewId", "EvidenceAssociationId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityMessage_InboxId",
                table: "CapacityMessage",
                column: "InboxId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityMessage_ProviderId_ProviderEventId",
                table: "CapacityMessage",
                columns: new[] { "ProviderId", "ProviderEventId" },
                unique: true,
                filter: "[ProviderEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityMessage_RecordedBy",
                table: "CapacityMessage",
                column: "RecordedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityMessage_ReferralId_CycleId_QuoteId",
                table: "CapacityMessage",
                columns: new[] { "ReferralId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityMessage_SubmissionId_EscalationId_CycleId_QuoteId",
                table: "CapacityMessage",
                columns: new[] { "SubmissionId", "EscalationId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacitySubmission_CreatedBy",
                table: "CapacitySubmission",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CapacitySubmission_EscalationId_CycleId_QuoteId",
                table: "CapacitySubmission",
                columns: new[] { "EscalationId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacitySubmission_EscalationId_Sequence",
                table: "CapacitySubmission",
                columns: new[] { "EscalationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapacitySubmission_ScenarioVersionId",
                table: "CapacitySubmission",
                column: "ScenarioVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacitySubmission_SubmittedBy",
                table: "CapacitySubmission",
                column: "SubmittedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CapacitySubmission_WorkId",
                table: "CapacitySubmission",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapacitySubmissionEvidence_CreatedBy",
                table: "CapacitySubmissionEvidence",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CapacitySubmissionEvidence_EvidenceAssociationId_CycleId_QuoteId",
                table: "CapacitySubmissionEvidence",
                columns: new[] { "EvidenceAssociationId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacitySubmissionEvidence_SubmissionId_CycleId_QuoteId",
                table: "CapacitySubmissionEvidence",
                columns: new[] { "SubmissionId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacitySubmissionEvidence_SubmissionId_EvidenceAssociationId",
                table: "CapacitySubmissionEvidence",
                columns: new[] { "SubmissionId", "EvidenceAssociationId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UnderwritingEvidenceAssociation_CapacitySubmission_CapacitySubmissionId_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation",
                columns: new[] { "CapacitySubmissionId", "CycleId", "QuoteId" },
                principalTable: "CapacitySubmission",
                principalColumns: new[] { "Id", "CycleId", "QuoteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CapacityEscalation_CapacityMessage_CurrentResponseId_CurrentSubmissionId_Id_CycleId_QuoteId",
                table: "CapacityEscalation",
                columns: new[] { "CurrentResponseId", "CurrentSubmissionId", "Id", "CycleId", "QuoteId" },
                principalTable: "CapacityMessage",
                principalColumns: new[] { "Id", "SubmissionId", "EscalationId", "CycleId", "QuoteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CapacityEscalation_CapacitySubmission_CurrentSubmissionId_Id_CycleId_QuoteId",
                table: "CapacityEscalation",
                columns: new[] { "CurrentSubmissionId", "Id", "CycleId", "QuoteId" },
                principalTable: "CapacitySubmission",
                principalColumns: new[] { "Id", "EscalationId", "CycleId", "QuoteId" });
            AddCapacityGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveCapacityGuards(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_UnderwritingEvidenceAssociation_CapacitySubmission_CapacitySubmissionId_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation");

            migrationBuilder.DropForeignKey(
                name: "FK_CapacityEscalation_CapacityMessage_CurrentResponseId_CurrentSubmissionId_Id_CycleId_QuoteId",
                table: "CapacityEscalation");

            migrationBuilder.DropForeignKey(
                name: "FK_CapacityEscalation_CapacitySubmission_CurrentSubmissionId_Id_CycleId_QuoteId",
                table: "CapacityEscalation");

            migrationBuilder.DropTable(
                name: "CapacitySubmissionEvidence");

            migrationBuilder.DropTable(
                name: "CapacityMessage");

            migrationBuilder.DropTable(
                name: "CapacitySubmission");

            migrationBuilder.DropTable(
                name: "CapacityEscalation");

            migrationBuilder.DropIndex(
                name: "IX_UnderwritingEvidenceAssociation_CapacitySubmissionId_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UnderwritingEvidenceAssociation_CapacityPurpose",
                table: "UnderwritingEvidenceAssociation");

            migrationBuilder.DropColumn(
                name: "CapacitySubmissionId",
                table: "UnderwritingEvidenceAssociation");
        }
    }
}
