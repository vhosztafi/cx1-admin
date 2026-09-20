using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialUnderwritingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_UnderwritingEvidenceAssociation_CommercialSubject ON UnderwritingEvidenceAssociation AFTER INSERT AS BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM inserted i JOIN UnderwritingCycle c ON c.Id=i.CycleId
                  JOIN QuoteRevision r ON r.Id=c.QuoteRevisionId JOIN Product p ON p.Id=c.ProductId
                  WHERE i.RequirementCode LIKE 'cc-%' AND (p.Code<>'commercial-combined' OR
                    (i.RequirementCode IN ('cc-location-proof','cc-wage-proof','cc-electrical-proof','cc-alarm-proof','cc-structural-proof') AND
                      (i.RiskItemId IS NULL OR NOT EXISTS(SELECT 1 FROM OPENJSON(r.ProposalJson,
                        CASE WHEN i.RequirementCode='cc-wage-proof' THEN '$.risk.wages' ELSE '$.risk.locations' END)
                        WITH(Id uniqueidentifier '$.id') subject WHERE subject.Id=i.RiskItemId))) OR
                    (i.RequirementCode NOT IN ('cc-location-proof','cc-wage-proof','cc-electrical-proof','cc-alarm-proof','cc-structural-proof') AND i.RiskItemId IS NOT NULL)))
                  THROW 51410, 'Commercial proof requires an owned, typed revision subject.', 1;
                END;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_UnderwritingEvidenceAssociation_Purpose",
                table: "UnderwritingEvidenceAssociation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_QuoteCondition_Definition",
                table: "QuoteCondition");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UnderwritingEvidenceAssociation_Purpose",
                table: "UnderwritingEvidenceAssociation",
                sql: "[RequirementCode] IN ('motor-trader-proof','no-claims-proof','photocard-both-sides','driving-record','premises-security','trading-history','signed-statement','warranty-acknowledgement','acceptance-proof','capacity-response','cc-property-proof','cc-location-proof','cc-liability-proof','cc-wage-proof','cc-bi-proof','cc-business-proof','cc-claims-experience-proof','cc-health-safety-proof','cc-electrical-proof','cc-alarm-proof','cc-structural-proof')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_QuoteCondition_Definition",
                table: "QuoteCondition",
                sql: "COALESCE(JSON_VALUE([DefinitionJson],'$.code'),'')=[Code] AND (([Kind]='documentary' AND [Code] IN ('provide-driver-proof','provide-premises-security','provide-signed-statement','provide-trading-history','provide-cc-property-proof','provide-cc-location-proof','provide-cc-liability-proof','provide-cc-wage-proof','provide-cc-bi-proof','provide-cc-business-proof','provide-cc-claims-experience-proof','provide-cc-health-safety-proof','provide-cc-electrical-proof','provide-cc-alarm-proof','provide-cc-structural-proof') AND [EndorsementCode] IS NULL) OR ([Kind]='warranty' AND [Code] IN ('overnight-security','named-drivers-only','any-driver-minimum-licence') AND [EndorsementCode] IS NOT NULL AND LEN(TRIM([Wording]))>0) OR ([Kind]='risk-change' AND [Code] IN ('revise-stock-limit','revise-vehicle-limit') AND [EndorsementCode] IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER TR_UnderwritingEvidenceAssociation_CommercialSubject;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_UnderwritingEvidenceAssociation_Purpose",
                table: "UnderwritingEvidenceAssociation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_QuoteCondition_Definition",
                table: "QuoteCondition");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UnderwritingEvidenceAssociation_Purpose",
                table: "UnderwritingEvidenceAssociation",
                sql: "[RequirementCode] IN ('motor-trader-proof','no-claims-proof','photocard-both-sides','driving-record','premises-security','trading-history','signed-statement','warranty-acknowledgement','acceptance-proof','capacity-response')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_QuoteCondition_Definition",
                table: "QuoteCondition",
                sql: "COALESCE(JSON_VALUE([DefinitionJson],'$.code'),'')=[Code] AND (([Kind]='documentary' AND [Code] IN ('provide-driver-proof','provide-premises-security','provide-signed-statement','provide-trading-history') AND [EndorsementCode] IS NULL) OR ([Kind]='warranty' AND [Code] IN ('overnight-security','named-drivers-only','any-driver-minimum-licence') AND [EndorsementCode] IS NOT NULL AND LEN(TRIM([Wording]))>0) OR ([Kind]='risk-change' AND [Code] IN ('revise-stock-limit','revise-vehicle-limit') AND [EndorsementCode] IS NULL))");
        }
    }
}
