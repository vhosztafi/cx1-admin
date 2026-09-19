using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CancellationReviewStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CancellationEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    NoticeDeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationEvidence", x => x.Id);
                    table.UniqueConstraint("AK_CancellationEvidence_Id_DraftId_RevisionId", x => new { x.Id, x.DraftId, x.RevisionId });
                    table.CheckConstraint("CK_CancellationEvidence_Actor", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_CancellationEvidence_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CancellationEvidence_Delivery", "[NoticeDeliveredAt] IS NULL OR ([Purpose]='cancellation-notice' AND [NoticeDeliveredAt]<=[CreatedAt] AND DATEPART(TZOFFSET,[NoticeDeliveredAt])=0)");
                    table.CheckConstraint("CK_CancellationEvidence_NoticeDeliveredAt_Utc", "DATEPART(TZOFFSET,[NoticeDeliveredAt]) = 0");
                    table.CheckConstraint("CK_CancellationEvidence_Purpose", "[Purpose] IN ('cancellation-request','cancellation-notice','cancellation-reason','insurer-instruction')");
                    table.ForeignKey(
                        name: "FK_CancellationEvidence_ServicingEvidenceFile_FileId_DraftId",
                        columns: x => new { x.FileId, x.DraftId },
                        principalTable: "ServicingEvidenceFile",
                        principalColumns: new[] { "Id", "DraftId" });
                    table.ForeignKey(
                        name: "FK_CancellationEvidence_ServicingRevision_RevisionId_DraftId",
                        columns: x => new { x.RevisionId, x.DraftId },
                        principalTable: "ServicingRevision",
                        principalColumns: new[] { "Id", "DraftId" });
                    table.ForeignKey(
                        name: "FK_CancellationEvidence_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CancellationPreview",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleSettingVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleVersion = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InputHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    InputJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationPreview", x => x.Id);
                    table.UniqueConstraint("AK_CancellationPreview_Id_DraftId_RevisionId_InputHash", x => new { x.Id, x.DraftId, x.RevisionId, x.InputHash });
                    table.CheckConstraint("CK_CancellationPreview_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CancellationPreview_Effective", "DATEPART(TZOFFSET,[EffectiveAt])=0");
                    table.CheckConstraint("CK_CancellationPreview_EffectiveAt_Utc", "DATEPART(TZOFFSET,[EffectiveAt]) = 0");
                    table.CheckConstraint("CK_CancellationPreview_Input", "ISJSON([InputJson],OBJECT)=1 AND DATALENGTH([InputJson])<=8388608 AND [InputHash]=HASHBYTES('SHA2_256',CONVERT(varchar(max),[InputJson] COLLATE Latin1_General_100_BIN2_UTF8))");
                    table.CheckConstraint("CK_CancellationPreview_Reason", "[ReasonCode] IN ('insured-request','non-payment','non-disclosure','trade-ceased','insurer-instruction') AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_CancellationPreview_Result", "ISJSON([ResultJson],OBJECT)=1 AND DATALENGTH([ResultJson])<=8388608 AND JSON_QUERY([InputJson],'$.amounts') IS NOT NULL AND CONVERT(varbinary(max),[ResultJson])=CONVERT(varbinary(max),JSON_QUERY([InputJson],'$.amounts'))");
                    table.ForeignKey(
                        name: "FK_CancellationPreview_ServicingDraft_DraftId_PolicyId_BaseTermId_BaseVersionId",
                        columns: x => new { x.DraftId, x.PolicyId, x.BaseTermId, x.BaseVersionId },
                        principalTable: "ServicingDraft",
                        principalColumns: new[] { "Id", "PolicyId", "BaseTermId", "BaseVersionId" });
                    table.ForeignKey(
                        name: "FK_CancellationPreview_ServicingRevision_RevisionId_DraftId",
                        columns: x => new { x.RevisionId, x.DraftId },
                        principalTable: "ServicingRevision",
                        principalColumns: new[] { "Id", "DraftId" });
                    table.ForeignKey(
                        name: "FK_CancellationPreview_SettingVersion_RuleSettingVersionId",
                        column: x => x.RuleSettingVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CancellationPreview_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CancellationEvidenceReview",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AuthorityGrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationEvidenceReview", x => x.Id);
                    table.CheckConstraint("CK_CancellationEvidenceReview_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CancellationEvidenceReview_Decision", "[Outcome] IN ('accepted','rejected') AND [Sequence]>0 AND [CreatedBy] IS NOT NULL AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");
                    table.ForeignKey(
                        name: "FK_CancellationEvidenceReview_CancellationEvidence_EvidenceId_DraftId_RevisionId",
                        columns: x => new { x.EvidenceId, x.DraftId, x.RevisionId },
                        principalTable: "CancellationEvidence",
                        principalColumns: new[] { "Id", "DraftId", "RevisionId" });
                    table.ForeignKey(
                        name: "FK_CancellationEvidenceReview_UserAuthorityGrant_AuthorityGrantId_CreatedBy_AuthorityVersionId",
                        columns: x => new { x.AuthorityGrantId, x.CreatedBy, x.AuthorityVersionId },
                        principalTable: "UserAuthorityGrant",
                        principalColumns: new[] { "Id", "UserId", "AuthorityVersionId" });
                    table.ForeignKey(
                        name: "FK_CancellationEvidenceReview_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CancellationApproval",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviewHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    AuthorityGrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationApproval", x => x.Id);
                    table.CheckConstraint("CK_CancellationApproval_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CancellationApproval_Decision", "[CreatedBy] IS NOT NULL AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");
                    table.ForeignKey(
                        name: "FK_CancellationApproval_CancellationPreview_PreviewId_DraftId_RevisionId_PreviewHash",
                        columns: x => new { x.PreviewId, x.DraftId, x.RevisionId, x.PreviewHash },
                        principalTable: "CancellationPreview",
                        principalColumns: new[] { "Id", "DraftId", "RevisionId", "InputHash" });
                    table.ForeignKey(
                        name: "FK_CancellationApproval_UserAuthorityGrant_AuthorityGrantId_CreatedBy_AuthorityVersionId",
                        columns: x => new { x.AuthorityGrantId, x.CreatedBy, x.AuthorityVersionId },
                        principalTable: "UserAuthorityGrant",
                        principalColumns: new[] { "Id", "UserId", "AuthorityVersionId" });
                    table.ForeignKey(
                        name: "FK_CancellationApproval_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationApproval_AuthorityGrantId_CreatedBy_AuthorityVersionId",
                table: "CancellationApproval",
                columns: new[] { "AuthorityGrantId", "CreatedBy", "AuthorityVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationApproval_CreatedBy",
                table: "CancellationApproval",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationApproval_PreviewId",
                table: "CancellationApproval",
                column: "PreviewId");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationApproval_PreviewId_DraftId_RevisionId_PreviewHash",
                table: "CancellationApproval",
                columns: new[] { "PreviewId", "DraftId", "RevisionId", "PreviewHash" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationEvidence_CreatedBy",
                table: "CancellationEvidence",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationEvidence_DraftId_RevisionId_CreatedAt",
                table: "CancellationEvidence",
                columns: new[] { "DraftId", "RevisionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationEvidence_FileId_DraftId",
                table: "CancellationEvidence",
                columns: new[] { "FileId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationEvidence_RevisionId_DraftId",
                table: "CancellationEvidence",
                columns: new[] { "RevisionId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationEvidenceReview_AuthorityGrantId_CreatedBy_AuthorityVersionId",
                table: "CancellationEvidenceReview",
                columns: new[] { "AuthorityGrantId", "CreatedBy", "AuthorityVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationEvidenceReview_CreatedBy",
                table: "CancellationEvidenceReview",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationEvidenceReview_EvidenceId_DraftId_RevisionId",
                table: "CancellationEvidenceReview",
                columns: new[] { "EvidenceId", "DraftId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationEvidenceReview_EvidenceId_Sequence",
                table: "CancellationEvidenceReview",
                columns: new[] { "EvidenceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CancellationPreview_CreatedBy",
                table: "CancellationPreview",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationPreview_DraftId_CreatedAt",
                table: "CancellationPreview",
                columns: new[] { "DraftId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationPreview_DraftId_PolicyId_BaseTermId_BaseVersionId",
                table: "CancellationPreview",
                columns: new[] { "DraftId", "PolicyId", "BaseTermId", "BaseVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationPreview_RevisionId_DraftId",
                table: "CancellationPreview",
                columns: new[] { "RevisionId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationPreview_RuleSettingVersionId",
                table: "CancellationPreview",
                column: "RuleSettingVersionId");
            AddReviewGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CancellationApproval");

            migrationBuilder.DropTable(
                name: "CancellationEvidenceReview");

            migrationBuilder.DropTable(
                name: "CancellationPreview");

            migrationBuilder.DropTable(
                name: "CancellationEvidence");
        }
    }
}
