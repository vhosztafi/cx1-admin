using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingEvidenceAssociations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServicingEvidenceAssociation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequirementCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RiskItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    LatestReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithdrawnEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingEvidenceAssociation", x => x.Id);
                    table.UniqueConstraint("AK_ServicingEvidenceAssociation_Id_CycleId_DraftId_RevisionId_RatingId_InputFingerprint", x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId, x.RatingId, x.InputFingerprint });
                    table.CheckConstraint("CK_ServicingEvidenceAssociation_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingEvidenceAssociation_Fingerprint", "LEN([InputFingerprint])=64 AND [InputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_ServicingEvidenceAssociation_Purpose", "([RequirementCode] IN ('motor-trader-proof','no-claims-proof','trading-history') AND [RiskItemId] IS NULL) OR ([RequirementCode] IN ('photocard-both-sides','driving-record','premises-security') AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_ServicingEvidenceAssociation_Reason", "LEN(TRIM([Reason]))>=10 AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_ServicingEvidenceAssociation_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ServicingEvidenceAssociation_ServicingEvidenceFile_FileId_DraftId",
                        columns: x => new { x.FileId, x.DraftId },
                        principalTable: "ServicingEvidenceFile",
                        principalColumns: new[] { "Id", "DraftId" });
                    table.ForeignKey(
                        name: "FK_ServicingEvidenceAssociation_ServicingRatingResult_RatingId_CycleId_DraftId_RevisionId",
                        columns: x => new { x.RatingId, x.CycleId, x.DraftId, x.RevisionId },
                        principalTable: "ServicingRatingResult",
                        principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId" });
                    table.ForeignKey(
                        name: "FK_ServicingEvidenceAssociation_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ServicingEvidenceEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssociationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingEvidenceEvent", x => x.Id);
                    table.UniqueConstraint("AK_ServicingEvidenceEvent_Id_AssociationId_CycleId_DraftId_RevisionId_RatingId", x => new { x.Id, x.AssociationId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
                    table.CheckConstraint("CK_ServicingEvidenceEvent_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingEvidenceEvent_Outcome", "([Kind]='review' AND [Outcome] IS NOT NULL AND [Outcome] IN ('accepted','rejected') AND [AuthorityVersionId] IS NOT NULL) OR ([Kind]='withdrawal' AND [Outcome] IS NULL AND [AuthorityVersionId] IS NULL)");
                    table.CheckConstraint("CK_ServicingEvidenceEvent_Reason", "LEN(TRIM([Reason]))>=10 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND [RecordedAt]>=[CreatedAt]");
                    table.CheckConstraint("CK_ServicingEvidenceEvent_RecordedAt_Utc", "DATEPART(TZOFFSET,[RecordedAt]) = 0");
                    table.CheckConstraint("CK_ServicingEvidenceEvent_Sequence", "[Sequence]>0");
                    table.ForeignKey(
                        name: "FK_ServicingEvidenceEvent_AuthorityVersion_AuthorityVersionId",
                        column: x => x.AuthorityVersionId,
                        principalTable: "AuthorityVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingEvidenceEvent_ServicingEvidenceAssociation_AssociationId_CycleId_DraftId_RevisionId_RatingId_InputFingerprint",
                        columns: x => new { x.AssociationId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId, x.InputFingerprint },
                        principalTable: "ServicingEvidenceAssociation",
                        principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId", "InputFingerprint" });
                    table.ForeignKey(
                        name: "FK_ServicingEvidenceEvent_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingEvidenceEvent_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceAssociation_CreatedBy",
                table: "ServicingEvidenceAssociation",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceAssociation_DraftId_CycleId_CreatedAt_Id",
                table: "ServicingEvidenceAssociation",
                columns: new[] { "DraftId", "CycleId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceAssociation_FileId_DraftId",
                table: "ServicingEvidenceAssociation",
                columns: new[] { "FileId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceAssociation_LatestReviewId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation",
                columns: new[] { "LatestReviewId", "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceAssociation_RatingId_CycleId_DraftId_RevisionId",
                table: "ServicingEvidenceAssociation",
                columns: new[] { "RatingId", "CycleId", "DraftId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceAssociation_WithdrawnEventId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation",
                columns: new[] { "WithdrawnEventId", "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceEvent_ActorId",
                table: "ServicingEvidenceEvent",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceEvent_AssociationId_CycleId_DraftId_RevisionId_RatingId_InputFingerprint",
                table: "ServicingEvidenceEvent",
                columns: new[] { "AssociationId", "CycleId", "DraftId", "RevisionId", "RatingId", "InputFingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceEvent_AssociationId_Sequence",
                table: "ServicingEvidenceEvent",
                columns: new[] { "AssociationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceEvent_AuthorityVersionId",
                table: "ServicingEvidenceEvent",
                column: "AuthorityVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceEvent_CreatedBy",
                table: "ServicingEvidenceEvent",
                column: "CreatedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingEvidenceAssociation_ServicingEvidenceEvent_LatestReviewId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation",
                columns: new[] { "LatestReviewId", "Id", "CycleId", "DraftId", "RevisionId", "RatingId" },
                principalTable: "ServicingEvidenceEvent",
                principalColumns: new[] { "Id", "AssociationId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingEvidenceAssociation_ServicingEvidenceEvent_WithdrawnEventId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation",
                columns: new[] { "WithdrawnEventId", "Id", "CycleId", "DraftId", "RevisionId", "RatingId" },
                principalTable: "ServicingEvidenceEvent",
                principalColumns: new[] { "Id", "AssociationId", "CycleId", "DraftId", "RevisionId", "RatingId" });
            AddEvidenceGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ServicingEvidenceAssociation_Source; DROP TRIGGER IF EXISTS TR_ServicingEvidenceAssociation_History; DROP TRIGGER IF EXISTS TR_ServicingEvidenceEvent_AppendOnly; DROP TRIGGER IF EXISTS TR_ServicingEvidenceEvent_Source;");
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingEvidenceAssociation_ServicingEvidenceEvent_LatestReviewId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.DropForeignKey(
                name: "FK_ServicingEvidenceAssociation_ServicingEvidenceEvent_WithdrawnEventId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.DropTable(
                name: "ServicingEvidenceEvent");

            migrationBuilder.DropTable(
                name: "ServicingEvidenceAssociation");
        }
    }
}
