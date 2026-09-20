using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialServicingDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddCommercialDecisionGuards(migrationBuilder);
            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingCondition_Code",
                table: "ServicingCondition");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingCapacityCondition_Code",
                table: "ServicingCapacityCondition");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation",
                sql: "([RequirementCode] IN ('motor-trader-proof','no-claims-proof','trading-history','warranty-acknowledgement','capacity-response','signed-statement','acceptance-proof','cc-property-proof','cc-liability-proof','cc-claims-experience-proof','cc-health-safety-proof','cc-bi-proof','cc-business-proof') AND [RiskItemId] IS NULL) OR ([RequirementCode] IN ('photocard-both-sides','driving-record','premises-security','cc-location-proof','cc-electrical-proof','cc-alarm-proof','cc-structural-proof','cc-wage-proof') AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingCondition_Code",
                table: "ServicingCondition",
                sql: "JSON_VALUE([DefinitionJson],'$.code') IS NOT NULL AND [Code]=JSON_VALUE([DefinitionJson],'$.code') AND [Code] IN ('provide-driver-proof','provide-premises-security','provide-signed-statement','provide-trading-history','overnight-security','named-drivers-only','any-driver-minimum-licence','revise-stock-limit','revise-vehicle-limit','provide-cc-property-proof','provide-cc-liability-proof','provide-cc-claims-experience-proof','provide-cc-health-safety-proof','provide-cc-bi-proof','provide-cc-business-proof','provide-cc-location-proof','provide-cc-electrical-proof','provide-cc-alarm-proof','provide-cc-structural-proof','provide-cc-wage-proof')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingCapacityCondition_Code",
                table: "ServicingCapacityCondition",
                sql: "JSON_VALUE([DefinitionJson],'$.code') IS NOT NULL AND [Code]=JSON_VALUE([DefinitionJson],'$.code') AND [Code] IN ('provide-driver-proof','provide-premises-security','provide-signed-statement','provide-trading-history','overnight-security','named-drivers-only','any-driver-minimum-licence','revise-stock-limit','revise-vehicle-limit','provide-cc-property-proof','provide-cc-liability-proof','provide-cc-claims-experience-proof','provide-cc-health-safety-proof','provide-cc-bi-proof','provide-cc-business-proof','provide-cc-location-proof','provide-cc-electrical-proof','provide-cc-alarm-proof','provide-cc-structural-proof','provide-cc-wage-proof')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveCommercialDecisionGuards(migrationBuilder);
            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingCondition_Code",
                table: "ServicingCondition");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingCapacityCondition_Code",
                table: "ServicingCapacityCondition");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingEvidenceAssociation_Purpose",
                table: "ServicingEvidenceAssociation",
                sql: "([RequirementCode] IN ('motor-trader-proof','no-claims-proof','trading-history','warranty-acknowledgement','capacity-response','signed-statement','acceptance-proof') AND [RiskItemId] IS NULL) OR ([RequirementCode] IN ('photocard-both-sides','driving-record','premises-security') AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingCondition_Code",
                table: "ServicingCondition",
                sql: "JSON_VALUE([DefinitionJson],'$.code') IS NOT NULL AND [Code]=JSON_VALUE([DefinitionJson],'$.code') AND [Code] IN ('provide-driver-proof','provide-premises-security','provide-signed-statement','provide-trading-history','overnight-security','named-drivers-only','any-driver-minimum-licence','revise-stock-limit','revise-vehicle-limit')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingCapacityCondition_Code",
                table: "ServicingCapacityCondition",
                sql: "JSON_VALUE([DefinitionJson],'$.code') IS NOT NULL AND [Code]=JSON_VALUE([DefinitionJson],'$.code') AND [Code] IN ('provide-driver-proof','provide-premises-security','provide-signed-statement','provide-trading-history','overnight-security','named-drivers-only','any-driver-minimum-licence','revise-stock-limit','revise-vehicle-limit')");
        }
    }
}
