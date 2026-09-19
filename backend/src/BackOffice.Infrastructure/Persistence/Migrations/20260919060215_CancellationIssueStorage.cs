using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CancellationIssueStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_CancellationApproval_Id_DraftId_RevisionId_PreviewId_PreviewHash",
                table: "CancellationApproval",
                columns: new[] { "Id", "DraftId", "RevisionId", "PreviewId", "PreviewHash" });

            migrationBuilder.CreateTable(
                name: "CancellationIssueDecision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviewHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    AuthorityGrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationIssueDecision", x => x.Id);
                    table.UniqueConstraint("AK_CancellationIssueDecision_Id_DraftId_PolicyId_RevisionId", x => new { x.Id, x.DraftId, x.PolicyId, x.RevisionId });
                    table.UniqueConstraint("AK_CancellationIssueDecision_Id_PolicyId_BaseTermId", x => new { x.Id, x.PolicyId, x.BaseTermId });
                    table.CheckConstraint("CK_CancellationIssueDecision_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");
                    table.CheckConstraint("CK_CancellationIssueDecision_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CancellationIssueDecision_Effective", "DATEPART(TZOFFSET,[EffectiveAt])=0");
                    table.CheckConstraint("CK_CancellationIssueDecision_EffectiveAt_Utc", "DATEPART(TZOFFSET,[EffectiveAt]) = 0");
                    table.ForeignKey(
                        name: "FK_CancellationIssueDecision_CancellationApproval_ApprovalId_DraftId_RevisionId_PreviewId_PreviewHash",
                        columns: x => new { x.ApprovalId, x.DraftId, x.RevisionId, x.PreviewId, x.PreviewHash },
                        principalTable: "CancellationApproval",
                        principalColumns: new[] { "Id", "DraftId", "RevisionId", "PreviewId", "PreviewHash" });
                    table.ForeignKey(
                        name: "FK_CancellationIssueDecision_ServicingDraft_DraftId_PolicyId_BaseTermId_BaseVersionId",
                        columns: x => new { x.DraftId, x.PolicyId, x.BaseTermId, x.BaseVersionId },
                        principalTable: "ServicingDraft",
                        principalColumns: new[] { "Id", "PolicyId", "BaseTermId", "BaseVersionId" });
                    table.ForeignKey(
                        name: "FK_CancellationIssueDecision_UserAuthorityGrant_AuthorityGrantId_ActorId_AuthorityVersionId",
                        columns: x => new { x.AuthorityGrantId, x.ActorId, x.AuthorityVersionId },
                        principalTable: "UserAuthorityGrant",
                        principalColumns: new[] { "Id", "UserId", "AuthorityVersionId" });
                    table.ForeignKey(
                        name: "FK_CancellationIssueDecision_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CancellationConsequence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationConsequence", x => x.Id);
                    table.UniqueConstraint("AK_CancellationConsequence_Id_WorkId_PayloadHash", x => new { x.Id, x.WorkId, x.PayloadHash });
                    table.CheckConstraint("CK_CancellationConsequence_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CancellationConsequence_Hash", "[PayloadHash]=HASHBYTES('SHA2_256',CONVERT(varchar(max),[PayloadJson] COLLATE Latin1_General_100_BIN2_UTF8))");
                    table.CheckConstraint("CK_CancellationConsequence_Kind", "[Kind] IN ('notice','certificate-withdrawal','mid-removal','task-close')");
                    table.CheckConstraint("CK_CancellationConsequence_PayloadJson", "ISJSON([PayloadJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[PayloadJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.ForeignKey(
                        name: "FK_CancellationConsequence_CancellationIssueDecision_DecisionId_PolicyId_TermId",
                        columns: x => new { x.DecisionId, x.PolicyId, x.TermId },
                        principalTable: "CancellationIssueDecision",
                        principalColumns: new[] { "Id", "PolicyId", "BaseTermId" });
                    table.ForeignKey(
                        name: "FK_CancellationConsequence_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CancellationConsequence_PolicyVersion_VersionId_TransactionId_TermId_PolicyId",
                        columns: x => new { x.VersionId, x.TransactionId, x.TermId, x.PolicyId },
                        principalTable: "PolicyVersion",
                        principalColumns: new[] { "Id", "TransactionId", "TermId", "PolicyId" });
                    table.ForeignKey(
                        name: "FK_CancellationConsequence_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CancellationNoticeReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsequenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationNoticeReceipt", x => x.Id);
                    table.CheckConstraint("CK_CancellationNoticeReceipt_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CancellationNoticeReceipt_Outcome", "[Outcome] IN ('demo-delivered','demo-no-recipient')");
                    table.ForeignKey(
                        name: "FK_CancellationNoticeReceipt_CancellationConsequence_ConsequenceId_WorkId_PayloadHash",
                        columns: x => new { x.ConsequenceId, x.WorkId, x.PayloadHash },
                        principalTable: "CancellationConsequence",
                        principalColumns: new[] { "Id", "WorkId", "PayloadHash" });
                    table.ForeignKey(
                        name: "FK_CancellationNoticeReceipt_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationConsequence_CreatedBy",
                table: "CancellationConsequence",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationConsequence_DecisionId_PolicyId_TermId",
                table: "CancellationConsequence",
                columns: new[] { "DecisionId", "PolicyId", "TermId" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationConsequence_TransactionId_Kind",
                table: "CancellationConsequence",
                columns: new[] { "TransactionId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CancellationConsequence_VersionId_TransactionId_TermId_PolicyId",
                table: "CancellationConsequence",
                columns: new[] { "VersionId", "TransactionId", "TermId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationConsequence_WorkId",
                table: "CancellationConsequence",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CancellationIssueDecision_ApprovalId_DraftId_RevisionId_PreviewId_PreviewHash",
                table: "CancellationIssueDecision",
                columns: new[] { "ApprovalId", "DraftId", "RevisionId", "PreviewId", "PreviewHash" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationIssueDecision_AuthorityGrantId_ActorId_AuthorityVersionId",
                table: "CancellationIssueDecision",
                columns: new[] { "AuthorityGrantId", "ActorId", "AuthorityVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationIssueDecision_CreatedBy",
                table: "CancellationIssueDecision",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationIssueDecision_DraftId",
                table: "CancellationIssueDecision",
                column: "DraftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CancellationIssueDecision_DraftId_PolicyId_BaseTermId_BaseVersionId",
                table: "CancellationIssueDecision",
                columns: new[] { "DraftId", "PolicyId", "BaseTermId", "BaseVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationNoticeReceipt_ConsequenceId",
                table: "CancellationNoticeReceipt",
                column: "ConsequenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CancellationNoticeReceipt_ConsequenceId_WorkId_PayloadHash",
                table: "CancellationNoticeReceipt",
                columns: new[] { "ConsequenceId", "WorkId", "PayloadHash" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationNoticeReceipt_CreatedBy",
                table: "CancellationNoticeReceipt",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationNoticeReceipt_WorkId",
                table: "CancellationNoticeReceipt",
                column: "WorkId",
                unique: true);
            AddIssueStorageGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM CancellationIssueDecision) THROW 51824,'Cancellation issue history cannot be downgraded.',1;");
            migrationBuilder.DropTable(
                name: "CancellationNoticeReceipt");

            migrationBuilder.DropTable(
                name: "CancellationConsequence");

            migrationBuilder.DropTable(
                name: "CancellationIssueDecision");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CancellationApproval_Id_DraftId_RevisionId_PreviewId_PreviewHash",
                table: "CancellationApproval");
        }
    }
}
