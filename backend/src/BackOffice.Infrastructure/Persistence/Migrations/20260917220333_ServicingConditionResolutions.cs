using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingConditionResolutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServicingConditionResolution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConditionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssociationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingConditionResolution", x => x.Id);
                    table.CheckConstraint("CK_ServicingConditionResolution_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingConditionResolution_Outcome", "[Outcome] IN ('satisfied','rejected')");
                    table.CheckConstraint("CK_ServicingConditionResolution_Reason", "LEN(TRIM([Reason]))>=10 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND [RecordedAt]>=[CreatedAt]");
                    table.CheckConstraint("CK_ServicingConditionResolution_RecordedAt_Utc", "DATEPART(TZOFFSET,[RecordedAt]) = 0");
                    table.CheckConstraint("CK_ServicingConditionResolution_Sequence", "[Sequence]>0");
                    table.ForeignKey(
                        name: "FK_ServicingConditionResolution_ServicingCondition_ConditionId_ReferralId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.ConditionId, x.ReferralId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingCondition",
                        principalColumns: new[] { "Id", "ReferralId", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingConditionResolution_ServicingEvidenceAssociation_AssociationId_CycleId_DraftId_RevisionId_RatingId_InputFingerprint",
                        columns: x => new { x.AssociationId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId, x.InputFingerprint },
                        principalTable: "ServicingEvidenceAssociation",
                        principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId", "InputFingerprint" });
                    table.ForeignKey(
                        name: "FK_ServicingConditionResolution_ServicingEvidenceEvent_ReviewId_AssociationId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.ReviewId, x.AssociationId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingEvidenceEvent",
                        principalColumns: new[] { "Id", "AssociationId", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingConditionResolution_UserAuthorityGrant_GrantId_ActorId_AuthorityVersionId",
                        columns: x => new { x.GrantId, x.ActorId, x.AuthorityVersionId },
                        principalTable: "UserAuthorityGrant",
                        principalColumns: new[] { "Id", "UserId", "AuthorityVersionId" });
                    table.ForeignKey(
                        name: "FK_ServicingConditionResolution_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingConditionResolution_AssociationId_CycleId_DraftId_RevisionId_RatingId_InputFingerprint",
                table: "ServicingConditionResolution",
                columns: new[] { "AssociationId", "CycleId", "DraftId", "RevisionId", "RatingId", "InputFingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingConditionResolution_ConditionId_ReferralId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingConditionResolution",
                columns: new[] { "ConditionId", "ReferralId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingConditionResolution_ConditionId_Sequence",
                table: "ServicingConditionResolution",
                columns: new[] { "ConditionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingConditionResolution_CreatedBy",
                table: "ServicingConditionResolution",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingConditionResolution_GrantId_ActorId_AuthorityVersionId",
                table: "ServicingConditionResolution",
                columns: new[] { "GrantId", "ActorId", "AuthorityVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingConditionResolution_ReviewId_AssociationId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingConditionResolution",
                columns: new[] { "ReviewId", "AssociationId", "CycleId", "DraftId", "RevisionId", "RatingId" });
            ServicingResolutionGuards.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServicingConditionResolution");
        }
    }
}
