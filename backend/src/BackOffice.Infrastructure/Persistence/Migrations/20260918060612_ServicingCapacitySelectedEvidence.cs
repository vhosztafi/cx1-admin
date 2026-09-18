using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingCapacitySelectedEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServicingCapacitySubmissionEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssociationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingCapacitySubmissionEvidence", x => x.Id);
                    table.CheckConstraint("CK_ServicingCapacitySubmissionEvidence_Actor", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_ServicingCapacitySubmissionEvidence_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ServicingCapacitySubmissionEvidence_ServicingCapacitySubmission_SubmissionId_CaseId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.SubmissionId, x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingCapacitySubmission",
                        principalColumns: new[] { "Id", "CaseId", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingCapacitySubmissionEvidence_ServicingEvidenceEvent_ReviewId_AssociationId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.ReviewId, x.AssociationId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingEvidenceEvent",
                        principalColumns: new[] { "Id", "AssociationId", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingCapacitySubmissionEvidence_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacitySubmissionEvidence_CreatedBy",
                table: "ServicingCapacitySubmissionEvidence",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacitySubmissionEvidence_ReviewId_AssociationId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacitySubmissionEvidence",
                columns: new[] { "ReviewId", "AssociationId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacitySubmissionEvidence_SubmissionId_AssociationId",
                table: "ServicingCapacitySubmissionEvidence",
                columns: new[] { "SubmissionId", "AssociationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacitySubmissionEvidence_SubmissionId_CaseId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacitySubmissionEvidence",
                columns: new[] { "SubmissionId", "CaseId", "CycleId", "DraftId", "RevisionId", "RatingId" });
            AddEvidenceGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveEvidenceGuards(migrationBuilder);
            migrationBuilder.DropTable(
                name: "ServicingCapacitySubmissionEvidence");
        }
    }
}
