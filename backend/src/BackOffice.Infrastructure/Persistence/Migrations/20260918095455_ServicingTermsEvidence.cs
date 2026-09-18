using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingTermsEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.AddColumn<Guid>(
                name: "TermsVersionId",
                table: "ServicingEvidenceAssociation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceAssociation_TermsVersionId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation",
                columns: new[] { "TermsVersionId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation",
                sql: "([RequirementCode] IN ('motor-trader-proof','no-claims-proof','trading-history','warranty-acknowledgement','capacity-response','signed-statement','acceptance-proof') AND [RiskItemId] IS NULL) OR ([RequirementCode] IN ('photocard-both-sides','driving-record','premises-security') AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_TermsPurpose",
                table: "ServicingEvidenceAssociation",
                sql: "([RequirementCode] IN ('signed-statement','acceptance-proof') AND [TermsVersionId] IS NOT NULL AND [RiskItemId] IS NULL) OR ([RequirementCode] NOT IN ('signed-statement','acceptance-proof') AND [TermsVersionId] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingEvidenceAssociation_ServicingTermsVersion_TermsVersionId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation",
                columns: new[] { "TermsVersionId", "CycleId", "DraftId", "RevisionId", "RatingId" },
                principalTable: "ServicingTermsVersion",
                principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });
            ServicingTermsEvidenceGuards.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ServicingTermsEvidenceGuards.Down(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingEvidenceAssociation_ServicingTermsVersion_TermsVersionId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.DropIndex(
                name: "IX_ServicingEvidenceAssociation_TermsVersionId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_TermsPurpose",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.DropColumn(
                name: "TermsVersionId",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation",
                sql: "([RequirementCode] IN ('motor-trader-proof','no-claims-proof','trading-history','warranty-acknowledgement','capacity-response') AND [RiskItemId] IS NULL) OR ([RequirementCode] IN ('photocard-both-sides','driving-record','premises-security') AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000')");
        }
    }
}
