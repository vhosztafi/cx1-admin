using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingCapacityResponseEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.AddColumn<Guid>(
                name: "CapacitySubmissionId",
                table: "ServicingEvidenceAssociation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ServicingCapacitySubmission_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacitySubmission",
                columns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceAssociation_CapacitySubmissionId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation",
                columns: new[] { "CapacitySubmissionId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_CapacityPurpose",
                table: "ServicingEvidenceAssociation",
                sql: "([RequirementCode]='capacity-response' AND [CapacitySubmissionId] IS NOT NULL AND [RiskItemId] IS NULL) OR ([RequirementCode]<>'capacity-response' AND [CapacitySubmissionId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation",
                sql: "([RequirementCode] IN ('motor-trader-proof','no-claims-proof','trading-history','warranty-acknowledgement','capacity-response') AND [RiskItemId] IS NULL) OR ([RequirementCode] IN ('photocard-both-sides','driving-record','premises-security') AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000')");

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingEvidenceAssociation_ServicingCapacitySubmission_CapacitySubmissionId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation",
                columns: new[] { "CapacitySubmissionId", "CycleId", "DraftId", "RevisionId", "RatingId" },
                principalTable: "ServicingCapacitySubmission",
                principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });
            AddResponseEvidenceGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ServicingEvidenceAssociation_CapacitySource; DROP TRIGGER IF EXISTS TR_ServicingEvidenceAssociation_CapacityHistory;");
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingEvidenceAssociation_ServicingCapacitySubmission_CapacitySubmissionId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.DropIndex(
                name: "IX_ServicingEvidenceAssociation_CapacitySubmissionId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_CapacityPurpose",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ServicingCapacitySubmission_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacitySubmission");

            migrationBuilder.DropColumn(
                name: "CapacitySubmissionId",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation",
                sql: "([RequirementCode] IN ('motor-trader-proof','no-claims-proof','trading-history','warranty-acknowledgement') AND [RiskItemId] IS NULL) OR ([RequirementCode] IN ('photocard-both-sides','driving-record','premises-security') AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000')");
        }
    }
}
