using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingWarrantyEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_ServicingEvidenceAssociation_Warranty ON ServicingEvidenceAssociation AFTER INSERT AS BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM inserted i WHERE i.RequirementCode='warranty-acknowledgement' AND NOT EXISTS(
                    SELECT 1 FROM ServicingCondition c JOIN ServicingReferral r ON r.Id=c.ReferralId
                    WHERE c.DraftId=i.DraftId AND c.CycleId=i.CycleId AND c.RevisionId=i.RevisionId AND c.RatingId=i.RatingId
                      AND c.Kind='warranty' AND r.State='conditional' AND r.LatestDecisionId=c.DecisionId))
                    THROW 51374,'Warranty acknowledgement requires current owned conditional warranty wording.',1;
                END;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation",
                sql: "([RequirementCode] IN ('motor-trader-proof','no-claims-proof','trading-history','warranty-acknowledgement') AND [RiskItemId] IS NULL) OR ([RequirementCode] IN ('photocard-both-sides','driving-record','premises-security') AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ServicingEvidenceAssociation_Warranty;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation",
                sql: "([RequirementCode] IN ('motor-trader-proof','no-claims-proof','trading-history') AND [RiskItemId] IS NULL) OR ([RequirementCode] IN ('photocard-both-sides','driving-record','premises-security') AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000')");
        }
    }
}
