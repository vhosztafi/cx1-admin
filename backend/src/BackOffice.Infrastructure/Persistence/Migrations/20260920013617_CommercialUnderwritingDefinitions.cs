using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialUnderwritingDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddCommercialProductGuards(migrationBuilder);
            migrationBuilder.DropCheckConstraint(
                name: "CK_RatingRuleVersion_Definition",
                table: "RatingRuleVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_annualPremiumLimit",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_Definition",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_stockLimit",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_vehicleLimit",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_annualPremiumLimit",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_Definition",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_stockLimit",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_vehicleLimit",
                table: "AuthorityVersion");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RatingRuleVersion_Definition",
                table: "RatingRuleVersion",
                sql: "(COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='1' OR (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined')) AND COALESCE(JSON_VALUE([DefinitionJson],'$.kind'),'')='rating' AND COALESCE(JSON_VALUE([DefinitionJson],'$.version'),'')=[Version] AND LEN(TRIM([Version]))>0 AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom'))=[EffectiveFrom] AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo'))=[EffectiveTo]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_annualPremiumLimit",
                table: "BinderVersion",
                sql: "(COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremiumLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremiumLimit'))>=0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_Commercial_annualPremium",
                table: "BinderVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremium')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremium'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_Commercial_businessInterruption",
                table: "BinderVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.businessInterruption')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.businessInterruption'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_Commercial_contractWorks",
                table: "BinderVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.contractWorks')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.contractWorks'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_Commercial_districtProperty",
                table: "BinderVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.districtProperty')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.districtProperty'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_Commercial_employersLiability",
                table: "BinderVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.employersLiability')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.employersLiability'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_Commercial_maximumEstimatedLoss",
                table: "BinderVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.maximumEstimatedLoss')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.maximumEstimatedLoss'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_Commercial_productsLiability",
                table: "BinderVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.productsLiability')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.productsLiability'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_Commercial_publicLiability",
                table: "BinderVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.publicLiability')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.publicLiability'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_Commercial_singleLocation",
                table: "BinderVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.singleLocation')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.singleLocation'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_Definition",
                table: "BinderVersion",
                sql: "(COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='1' OR (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined')) AND COALESCE(JSON_VALUE([DefinitionJson],'$.kind'),'')='binder' AND COALESCE(JSON_VALUE([DefinitionJson],'$.version'),'')=[Version] AND LEN(TRIM([Version]))>0 AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom'))=[EffectiveFrom] AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo'))=[EffectiveTo]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_stockLimit",
                table: "BinderVersion",
                sql: "(COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.stockLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.stockLimit'))>=0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_vehicleLimit",
                table: "BinderVersion",
                sql: "(COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.vehicleLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.vehicleLimit'))>=0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_annualPremiumLimit",
                table: "AuthorityVersion",
                sql: "(COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremiumLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremiumLimit'))>=0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_annualPremium",
                table: "AuthorityVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremium')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremium'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_businessInterruption",
                table: "AuthorityVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.businessInterruption')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.businessInterruption'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_contractWorks",
                table: "AuthorityVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.contractWorks')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.contractWorks'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_districtProperty",
                table: "AuthorityVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.districtProperty')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.districtProperty'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_employersLiability",
                table: "AuthorityVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.employersLiability')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.employersLiability'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_maximumEstimatedLoss",
                table: "AuthorityVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.maximumEstimatedLoss')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.maximumEstimatedLoss'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_productsLiability",
                table: "AuthorityVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.productsLiability')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.productsLiability'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_publicLiability",
                table: "AuthorityVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.publicLiability')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.publicLiability'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_singleLocation",
                table: "AuthorityVersion",
                sql: "NOT (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.singleLocation')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.singleLocation'))>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_Definition",
                table: "AuthorityVersion",
                sql: "(COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='1' OR (COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined')) AND COALESCE(JSON_VALUE([DefinitionJson],'$.kind'),'')='authority' AND COALESCE(JSON_VALUE([DefinitionJson],'$.version'),'')=[Version] AND LEN(TRIM([Version]))>0 AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom'))=[EffectiveFrom] AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo'))=[EffectiveTo]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_stockLimit",
                table: "AuthorityVersion",
                sql: "(COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.stockLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.stockLimit'))>=0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_vehicleLimit",
                table: "AuthorityVersion",
                sql: "(COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined') OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.vehicleLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.vehicleLimit'))>=0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveCommercialProductGuards(migrationBuilder);
            migrationBuilder.DropCheckConstraint(
                name: "CK_RatingRuleVersion_Definition",
                table: "RatingRuleVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_annualPremiumLimit",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_Commercial_annualPremium",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_Commercial_businessInterruption",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_Commercial_contractWorks",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_Commercial_districtProperty",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_Commercial_employersLiability",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_Commercial_maximumEstimatedLoss",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_Commercial_productsLiability",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_Commercial_publicLiability",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_Commercial_singleLocation",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_Definition",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_stockLimit",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BinderVersion_vehicleLimit",
                table: "BinderVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_annualPremiumLimit",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_annualPremium",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_businessInterruption",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_contractWorks",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_districtProperty",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_employersLiability",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_maximumEstimatedLoss",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_productsLiability",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_publicLiability",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_Commercial_singleLocation",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_Definition",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_stockLimit",
                table: "AuthorityVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorityVersion_vehicleLimit",
                table: "AuthorityVersion");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RatingRuleVersion_Definition",
                table: "RatingRuleVersion",
                sql: "COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.kind'),'')='rating' AND COALESCE(JSON_VALUE([DefinitionJson],'$.version'),'')=[Version] AND LEN(TRIM([Version]))>0 AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom'))=[EffectiveFrom] AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo'))=[EffectiveTo]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_annualPremiumLimit",
                table: "BinderVersion",
                sql: "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremiumLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremiumLimit'))>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_Definition",
                table: "BinderVersion",
                sql: "COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.kind'),'')='binder' AND COALESCE(JSON_VALUE([DefinitionJson],'$.version'),'')=[Version] AND LEN(TRIM([Version]))>0 AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom'))=[EffectiveFrom] AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo'))=[EffectiveTo]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_stockLimit",
                table: "BinderVersion",
                sql: "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.stockLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.stockLimit'))>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BinderVersion_vehicleLimit",
                table: "BinderVersion",
                sql: "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.vehicleLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.vehicleLimit'))>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_annualPremiumLimit",
                table: "AuthorityVersion",
                sql: "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremiumLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremiumLimit'))>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_Definition",
                table: "AuthorityVersion",
                sql: "COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.kind'),'')='authority' AND COALESCE(JSON_VALUE([DefinitionJson],'$.version'),'')=[Version] AND LEN(TRIM([Version]))>0 AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom'))=[EffectiveFrom] AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo'))=[EffectiveTo]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_stockLimit",
                table: "AuthorityVersion",
                sql: "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.stockLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.stockLimit'))>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorityVersion_vehicleLimit",
                table: "AuthorityVersion",
                sql: "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.vehicleLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.vehicleLimit'))>=0");
        }
    }
}
