using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AtomicServicingIssue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingDraft_State",
                table: "ServicingDraft");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyDocumentRequest_Purpose",
                table: "PolicyDocumentRequest");

            migrationBuilder.AddColumn<Guid>(
                name: "IssuedTransactionId",
                table: "ServicingDraft",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServicingIssueDecisionId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PolicyTransaction_Id_PolicyId",
                table: "PolicyTransaction",
                columns: new[] { "Id", "PolicyId" });

            migrationBuilder.CreateTable(
                name: "PolicyMidIntent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyMidIntent", x => x.Id);
                    table.CheckConstraint("CK_PolicyMidIntent_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_PolicyMidIntent_PayloadJson", "ISJSON([PayloadJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[PayloadJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_PolicyMidIntent_Purpose", "[Purpose]='adjustment'");
                    table.ForeignKey(
                        name: "FK_PolicyMidIntent_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PolicyMidIntent_PolicyVersion_VersionId_TransactionId_TermId_PolicyId",
                        columns: x => new { x.VersionId, x.TransactionId, x.TermId, x.PolicyId },
                        principalTable: "PolicyVersion",
                        principalColumns: new[] { "Id", "TransactionId", "TermId", "PolicyId" });
                    table.ForeignKey(
                        name: "FK_PolicyMidIntent_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ServicingIssueDecision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    TermsHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AssuranceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingIssueDecision", x => x.Id);
                    table.UniqueConstraint("AK_ServicingIssueDecision_Id_DraftId_PolicyId_CycleId_RevisionId_RatingId_AcceptanceId", x => new { x.Id, x.DraftId, x.PolicyId, x.CycleId, x.RevisionId, x.RatingId, x.AcceptanceId });
                    table.CheckConstraint("CK_ServicingIssueDecision_AssuranceHash", "LEN([AssuranceHash])=64 AND [AssuranceHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_ServicingIssueDecision_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingIssueDecision_EffectiveAt_Utc", "DATEPART(TZOFFSET,[EffectiveAt]) = 0");
                    table.CheckConstraint("CK_ServicingIssueDecision_Provenance", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
                    table.CheckConstraint("CK_ServicingIssueDecision_TermsHash", "LEN([TermsHash])=64 AND [TermsHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.ForeignKey(
                        name: "FK_ServicingIssueDecision_ServicingAcceptance_AcceptanceId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.AcceptanceId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingAcceptance",
                        principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingIssueDecision_ServicingCycle_CycleId_DraftId_PolicyId",
                        columns: x => new { x.CycleId, x.DraftId, x.PolicyId },
                        principalTable: "ServicingCycle",
                        principalColumns: new[] { "Id", "DraftId", "PolicyId" });
                    table.ForeignKey(
                        name: "FK_ServicingIssueDecision_ServicingDraft_DraftId_PolicyId_BaseTermId_BaseVersionId",
                        columns: x => new { x.DraftId, x.PolicyId, x.BaseTermId, x.BaseVersionId },
                        principalTable: "ServicingDraft",
                        principalColumns: new[] { "Id", "PolicyId", "BaseTermId", "BaseVersionId" });
                    table.ForeignKey(
                        name: "FK_ServicingIssueDecision_ServicingTermsVersion_TermsVersionId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.TermsVersionId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingTermsVersion",
                        principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingIssueDecision_UserAuthorityGrant_GrantId_ActorId_AuthorityVersionId",
                        columns: x => new { x.GrantId, x.ActorId, x.AuthorityVersionId },
                        principalTable: "UserAuthorityGrant",
                        principalColumns: new[] { "Id", "UserId", "AuthorityVersionId" });
                    table.ForeignKey(
                        name: "FK_ServicingIssueDecision_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingDraft_IssuedTransactionId_PolicyId",
                table: "ServicingDraft",
                columns: new[] { "IssuedTransactionId", "PolicyId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingDraft_IssuedTransaction",
                table: "ServicingDraft",
                sql: "([State]='issued' AND [IssuedTransactionId] IS NOT NULL) OR ([State]<>'issued' AND [IssuedTransactionId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingDraft_State",
                table: "ServicingDraft",
                sql: "[State] IN ('draft','abandoned','issued')");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_ServicingIssueDecisionId",
                table: "PolicyTransaction",
                column: "ServicingIssueDecisionId",
                unique: true,
                filter: "[ServicingIssueDecisionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_ServicingIssueDecisionId_ServicingDraftId_PolicyId_ServicingCycleId_ServicingRevisionId_ServicingRatingId_~",
                table: "PolicyTransaction",
                columns: new[] { "ServicingIssueDecisionId", "ServicingDraftId", "PolicyId", "ServicingCycleId", "ServicingRevisionId", "ServicingRatingId", "ServicingAcceptanceId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_IssueDecision",
                table: "PolicyTransaction",
                sql: "([Kind]='new-business' AND [ServicingIssueDecisionId] IS NULL) OR ([Kind]='adjustment' AND [ServicingIssueDecisionId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyDocumentRequest_Purpose",
                table: "PolicyDocumentRequest",
                sql: "[Purpose] IN ('first-issue','adjustment')");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyMidIntent_CreatedBy",
                table: "PolicyMidIntent",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyMidIntent_VersionId_Purpose",
                table: "PolicyMidIntent",
                columns: new[] { "VersionId", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyMidIntent_VersionId_TransactionId_TermId_PolicyId",
                table: "PolicyMidIntent",
                columns: new[] { "VersionId", "TransactionId", "TermId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyMidIntent_WorkId",
                table: "PolicyMidIntent",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingIssueDecision_AcceptanceId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingIssueDecision",
                columns: new[] { "AcceptanceId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingIssueDecision_CreatedBy",
                table: "ServicingIssueDecision",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingIssueDecision_CycleId_DraftId_PolicyId",
                table: "ServicingIssueDecision",
                columns: new[] { "CycleId", "DraftId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingIssueDecision_DraftId",
                table: "ServicingIssueDecision",
                column: "DraftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingIssueDecision_DraftId_PolicyId_BaseTermId_BaseVersionId",
                table: "ServicingIssueDecision",
                columns: new[] { "DraftId", "PolicyId", "BaseTermId", "BaseVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingIssueDecision_GrantId_ActorId_AuthorityVersionId",
                table: "ServicingIssueDecision",
                columns: new[] { "GrantId", "ActorId", "AuthorityVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingIssueDecision_TermsVersionId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingIssueDecision",
                columns: new[] { "TermsVersionId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PolicyTransaction_ServicingIssueDecision_ServicingIssueDecisionId_ServicingDraftId_PolicyId_ServicingCycleId_ServicingRevisi~",
                table: "PolicyTransaction",
                columns: new[] { "ServicingIssueDecisionId", "ServicingDraftId", "PolicyId", "ServicingCycleId", "ServicingRevisionId", "ServicingRatingId", "ServicingAcceptanceId" },
                principalTable: "ServicingIssueDecision",
                principalColumns: new[] { "Id", "DraftId", "PolicyId", "CycleId", "RevisionId", "RatingId", "AcceptanceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingDraft_PolicyTransaction_IssuedTransactionId_PolicyId",
                table: "ServicingDraft",
                columns: new[] { "IssuedTransactionId", "PolicyId" },
                principalTable: "PolicyTransaction",
                principalColumns: new[] { "Id", "PolicyId" });
            AtomicServicingIssueGuards.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            AtomicServicingIssueGuards.Down(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_PolicyTransaction_ServicingIssueDecision_ServicingIssueDecisionId_ServicingDraftId_PolicyId_ServicingCycleId_ServicingRevisi~",
                table: "PolicyTransaction");

            migrationBuilder.DropForeignKey(
                name: "FK_ServicingDraft_PolicyTransaction_IssuedTransactionId_PolicyId",
                table: "ServicingDraft");

            migrationBuilder.DropTable(
                name: "PolicyMidIntent");

            migrationBuilder.DropTable(
                name: "ServicingIssueDecision");

            migrationBuilder.DropIndex(
                name: "IX_ServicingDraft_IssuedTransactionId_PolicyId",
                table: "ServicingDraft");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingDraft_IssuedTransaction",
                table: "ServicingDraft");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingDraft_State",
                table: "ServicingDraft");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PolicyTransaction_Id_PolicyId",
                table: "PolicyTransaction");

            migrationBuilder.DropIndex(
                name: "IX_PolicyTransaction_ServicingIssueDecisionId",
                table: "PolicyTransaction");

            migrationBuilder.DropIndex(
                name: "IX_PolicyTransaction_ServicingIssueDecisionId_ServicingDraftId_PolicyId_ServicingCycleId_ServicingRevisionId_ServicingRatingId_~",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_IssueDecision",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyDocumentRequest_Purpose",
                table: "PolicyDocumentRequest");

            migrationBuilder.DropColumn(
                name: "IssuedTransactionId",
                table: "ServicingDraft");

            migrationBuilder.DropColumn(
                name: "ServicingIssueDecisionId",
                table: "PolicyTransaction");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingDraft_State",
                table: "ServicingDraft",
                sql: "[State] IN ('draft','abandoned')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyDocumentRequest_Purpose",
                table: "PolicyDocumentRequest",
                sql: "[Purpose]='first-issue'");
        }
    }
}
