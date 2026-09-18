using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingCapacityResponses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentResponseId",
                table: "ServicingCapacityCase",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServicingCapacityResponse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Provenance = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ProviderUnderwriter = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProviderReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProviderEventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProviderOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EvidenceAssociationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EvidenceReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApplicationState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingCapacityResponse", x => x.Id);
                    table.UniqueConstraint("AK_ServicingCapacityResponse_Id_SubmissionId_CaseId_CycleId_DraftId_RevisionId_RatingId", x => new { x.Id, x.SubmissionId, x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
                    table.CheckConstraint("CK_ServicingCapacityResponse_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RecordedBy] AND [RecordedAt]=[CreatedAt] AND [ReceivedAt]<=[RecordedAt]");
                    table.CheckConstraint("CK_ServicingCapacityResponse_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingCapacityResponse_DefinitionJson", "ISJSON([DefinitionJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[DefinitionJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_ServicingCapacityResponse_Hash", "[ContentHash]=HASHBYTES('SHA2_256',CONVERT(varchar(max),[DefinitionJson] COLLATE Latin1_General_100_BIN2_UTF8))");
                    table.CheckConstraint("CK_ServicingCapacityResponse_Outcome", "[Outcome] IN ('approve','approve-with-conditions','query','decline')");
                    table.CheckConstraint("CK_ServicingCapacityResponse_Provenance", "([Provenance]='supplied-response' AND [EvidenceAssociationId] IS NOT NULL AND [EvidenceReviewId] IS NOT NULL AND [ProviderEventId] IS NULL AND [ProviderOperationId] IS NULL AND [InboxId] IS NULL AND [ApplicationState]='applied') OR ([Provenance]='demo-provider' AND [EvidenceAssociationId] IS NULL AND [EvidenceReviewId] IS NULL AND [ProviderEventId] IS NOT NULL AND [ProviderOperationId] IS NOT NULL AND [InboxId] IS NOT NULL AND [ApplicationState] IN ('applied','superseded'))");
                    table.CheckConstraint("CK_ServicingCapacityResponse_ReceivedAt_Utc", "DATEPART(TZOFFSET,[ReceivedAt]) = 0");
                    table.CheckConstraint("CK_ServicingCapacityResponse_RecordedAt_Utc", "DATEPART(TZOFFSET,[RecordedAt]) = 0");
                    table.CheckConstraint("CK_ServicingCapacityResponse_Sequence", "[Sequence]>0");
                    table.CheckConstraint("CK_ServicingCapacityResponse_Text", "LEN(TRIM([Body]))>0 AND DATALENGTH([Body])<=20000 AND LEN(TRIM([ProviderUnderwriter]))>0 AND LEN(TRIM([ProviderReference]))>0");
                    table.ForeignKey(
                        name: "FK_ServicingCapacityResponse_AdapterInbox_InboxId",
                        column: x => x.InboxId,
                        principalTable: "AdapterInbox",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCapacityResponse_CapacityProvider_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "CapacityProvider",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCapacityResponse_DemoProviderOperation_ProviderOperationId",
                        column: x => x.ProviderOperationId,
                        principalTable: "DemoProviderOperation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCapacityResponse_ServicingCapacitySubmission_SubmissionId_CaseId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.SubmissionId, x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingCapacitySubmission",
                        principalColumns: new[] { "Id", "CaseId", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingCapacityResponse_ServicingEvidenceEvent_EvidenceReviewId_EvidenceAssociationId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.EvidenceReviewId, x.EvidenceAssociationId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingEvidenceEvent",
                        principalColumns: new[] { "Id", "AssociationId", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingCapacityResponse_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCapacityResponse_User_RecordedBy",
                        column: x => x.RecordedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityCase_CurrentResponseId_CurrentSubmissionId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacityCase",
                columns: new[] { "CurrentResponseId", "CurrentSubmissionId", "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingCapacityCase_ResponseOwner",
                table: "ServicingCapacityCase",
                sql: "[CurrentResponseId] IS NULL OR [CurrentSubmissionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityResponse_CaseId_Sequence",
                table: "ServicingCapacityResponse",
                columns: new[] { "CaseId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityResponse_CreatedBy",
                table: "ServicingCapacityResponse",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityResponse_EvidenceReviewId_EvidenceAssociationId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacityResponse",
                columns: new[] { "EvidenceReviewId", "EvidenceAssociationId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityResponse_InboxId",
                table: "ServicingCapacityResponse",
                column: "InboxId",
                unique: true,
                filter: "[InboxId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityResponse_ProviderId_ProviderEventId",
                table: "ServicingCapacityResponse",
                columns: new[] { "ProviderId", "ProviderEventId" },
                unique: true,
                filter: "[ProviderEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityResponse_ProviderOperationId",
                table: "ServicingCapacityResponse",
                column: "ProviderOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityResponse_RecordedBy",
                table: "ServicingCapacityResponse",
                column: "RecordedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityResponse_SubmissionId_CaseId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacityResponse",
                columns: new[] { "SubmissionId", "CaseId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingCapacityCase_ServicingCapacityResponse_CurrentResponseId_CurrentSubmissionId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacityCase",
                columns: new[] { "CurrentResponseId", "CurrentSubmissionId", "Id", "CycleId", "DraftId", "RevisionId", "RatingId" },
                principalTable: "ServicingCapacityResponse",
                principalColumns: new[] { "Id", "SubmissionId", "CaseId", "CycleId", "DraftId", "RevisionId", "RatingId" });
            AddResponseGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ServicingCapacityResponse_Source; DROP TRIGGER IF EXISTS TR_ServicingCapacityResponse_Immutable; DROP TRIGGER IF EXISTS TR_ServicingCapacityCase_History;");
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingCapacityCase_ServicingCapacityResponse_CurrentResponseId_CurrentSubmissionId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacityCase");

            migrationBuilder.DropTable(
                name: "ServicingCapacityResponse");

            migrationBuilder.DropIndex(
                name: "IX_ServicingCapacityCase_CurrentResponseId_CurrentSubmissionId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacityCase");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingCapacityCase_ResponseOwner",
                table: "ServicingCapacityCase");

            migrationBuilder.DropColumn(
                name: "CurrentResponseId",
                table: "ServicingCapacityCase");
            RestorePriorCaseGuard(migrationBuilder);
        }
    }
}
