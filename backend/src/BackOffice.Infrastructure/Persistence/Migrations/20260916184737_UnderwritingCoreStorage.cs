using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnderwritingCoreStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Quote_State",
                table: "Quote");

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentUnderwritingCycleId",
                table: "Quote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_QuoteRevision_Id_QuoteId_AgencyId_ClientId_RelationshipId_ProductId_ProductVersionId_AgencyTermsVersionId",
                table: "QuoteRevision",
                columns: new[] { "Id", "QuoteId", "AgencyId", "ClientId", "RelationshipId", "ProductId", "ProductVersionId", "AgencyTermsVersionId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AdapterAttempt_Id_WorkId",
                table: "AdapterAttempt",
                columns: new[] { "Id", "WorkId" });

            migrationBuilder.CreateTable(
                name: "BinderVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BinderVersion", x => x.Id);
                    table.UniqueConstraint("AK_BinderVersion_Id_ProductId", x => new { x.Id, x.ProductId });
                    table.CheckConstraint("CK_BinderVersion_annualPremiumLimit", "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremiumLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremiumLimit'))>=0");
                    table.CheckConstraint("CK_BinderVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_BinderVersion_Definition", "COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.kind'),'')='binder' AND COALESCE(JSON_VALUE([DefinitionJson],'$.version'),'')=[Version] AND LEN(TRIM([Version]))>0 AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom'))=[EffectiveFrom] AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo'))=[EffectiveTo]");
                    table.CheckConstraint("CK_BinderVersion_DefinitionJson", "ISJSON([DefinitionJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[DefinitionJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_BinderVersion_EffectiveFrom_Utc", "DATEPART(TZOFFSET,[EffectiveFrom]) = 0");
                    table.CheckConstraint("CK_BinderVersion_EffectiveTo_Utc", "DATEPART(TZOFFSET,[EffectiveTo]) = 0");
                    table.CheckConstraint("CK_BinderVersion_Interval", "[EffectiveFrom]<[EffectiveTo]");
                    table.CheckConstraint("CK_BinderVersion_Provider", "TRY_CONVERT(uniqueidentifier,JSON_VALUE([DefinitionJson],'$.providerId')) IS NOT NULL AND TRY_CONVERT(uniqueidentifier,JSON_VALUE([DefinitionJson],'$.providerId'))=[ProviderId]");
                    table.CheckConstraint("CK_BinderVersion_State", "[State] IN ('published','retired')");
                    table.CheckConstraint("CK_BinderVersion_stockLimit", "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.stockLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.stockLimit'))>=0");
                    table.CheckConstraint("CK_BinderVersion_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.CheckConstraint("CK_BinderVersion_vehicleLimit", "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.vehicleLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.vehicleLimit'))>=0");
                    table.ForeignKey(
                        name: "FK_BinderVersion_CapacityProvider_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "CapacityProvider",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BinderVersion_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BinderVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RatingRuleVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatingRuleVersion", x => x.Id);
                    table.UniqueConstraint("AK_RatingRuleVersion_Id_ProductId", x => new { x.Id, x.ProductId });
                    table.CheckConstraint("CK_RatingRuleVersion_basePremium", "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.basePremium')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.basePremium'))>0");
                    table.CheckConstraint("CK_RatingRuleVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_RatingRuleVersion_Definition", "COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.kind'),'')='rating' AND COALESCE(JSON_VALUE([DefinitionJson],'$.version'),'')=[Version] AND LEN(TRIM([Version]))>0 AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom'))=[EffectiveFrom] AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo'))=[EffectiveTo]");
                    table.CheckConstraint("CK_RatingRuleVersion_DefinitionJson", "ISJSON([DefinitionJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[DefinitionJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_RatingRuleVersion_EffectiveFrom_Utc", "DATEPART(TZOFFSET,[EffectiveFrom]) = 0");
                    table.CheckConstraint("CK_RatingRuleVersion_EffectiveTo_Utc", "DATEPART(TZOFFSET,[EffectiveTo]) = 0");
                    table.CheckConstraint("CK_RatingRuleVersion_Interval", "[EffectiveFrom]<[EffectiveTo]");
                    table.CheckConstraint("CK_RatingRuleVersion_maximumAnnualPremium", "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.maximumAnnualPremium')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.maximumAnnualPremium'))>0");
                    table.CheckConstraint("CK_RatingRuleVersion_minimumPremium", "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.minimumPremium')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.minimumPremium'))>0");
                    table.CheckConstraint("CK_RatingRuleVersion_State", "[State] IN ('published','retired')");
                    table.CheckConstraint("CK_RatingRuleVersion_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_RatingRuleVersion_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RatingRuleVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuthorityVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BinderVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorityVersion", x => x.Id);
                    table.UniqueConstraint("AK_AuthorityVersion_Id_ProductVersionId_BinderVersionId", x => new { x.Id, x.ProductVersionId, x.BinderVersionId });
                    table.CheckConstraint("CK_AuthorityVersion_annualPremiumLimit", "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremiumLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.annualPremiumLimit'))>=0");
                    table.CheckConstraint("CK_AuthorityVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AuthorityVersion_Definition", "COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.kind'),'')='authority' AND COALESCE(JSON_VALUE([DefinitionJson],'$.version'),'')=[Version] AND LEN(TRIM([Version]))>0 AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom'))=[EffectiveFrom] AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo'))=[EffectiveTo]");
                    table.CheckConstraint("CK_AuthorityVersion_DefinitionJson", "ISJSON([DefinitionJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[DefinitionJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_AuthorityVersion_EffectiveFrom_Utc", "DATEPART(TZOFFSET,[EffectiveFrom]) = 0");
                    table.CheckConstraint("CK_AuthorityVersion_EffectiveTo_Utc", "DATEPART(TZOFFSET,[EffectiveTo]) = 0");
                    table.CheckConstraint("CK_AuthorityVersion_Interval", "[EffectiveFrom]<[EffectiveTo]");
                    table.CheckConstraint("CK_AuthorityVersion_State", "[State] IN ('published','retired')");
                    table.CheckConstraint("CK_AuthorityVersion_stockLimit", "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.stockLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.stockLimit'))>=0");
                    table.CheckConstraint("CK_AuthorityVersion_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.CheckConstraint("CK_AuthorityVersion_vehicleLimit", "TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.vehicleLimit')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.vehicleLimit'))>=0");
                    table.ForeignKey(
                        name: "FK_AuthorityVersion_BinderVersion_BinderVersionId_ProductId",
                        columns: x => new { x.BinderVersionId, x.ProductId },
                        principalTable: "BinderVersion",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_AuthorityVersion_ProductVersion_ProductVersionId_ProductId",
                        columns: x => new { x.ProductVersionId, x.ProductId },
                        principalTable: "ProductVersion",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_AuthorityVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserAuthorityGrant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GrantedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevocationReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAuthorityGrant", x => x.Id);
                    table.CheckConstraint("CK_UserAuthorityGrant_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_UserAuthorityGrant_EffectiveFrom_Utc", "DATEPART(TZOFFSET,[EffectiveFrom]) = 0");
                    table.CheckConstraint("CK_UserAuthorityGrant_EffectiveTo_Utc", "DATEPART(TZOFFSET,[EffectiveTo]) = 0");
                    table.CheckConstraint("CK_UserAuthorityGrant_Interval", "[EffectiveFrom]<[EffectiveTo]");
                    table.CheckConstraint("CK_UserAuthorityGrant_Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[GrantedBy]");
                    table.CheckConstraint("CK_UserAuthorityGrant_Revocation", "([RevokedAt] IS NULL AND [RevokedBy] IS NULL AND [RevocationReason] IS NULL) OR ([RevokedAt] IS NOT NULL AND [RevokedAt]>=[CreatedAt] AND [RevokedBy] IS NOT NULL AND [RevocationReason] IS NOT NULL AND LEN(TRIM([RevocationReason]))>0)");
                    table.CheckConstraint("CK_UserAuthorityGrant_RevokedAt_Utc", "DATEPART(TZOFFSET,[RevokedAt]) = 0");
                    table.CheckConstraint("CK_UserAuthorityGrant_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_UserAuthorityGrant_AuthorityVersion_AuthorityVersionId",
                        column: x => x.AuthorityVersionId,
                        principalTable: "AuthorityVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserAuthorityGrant_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserAuthorityGrant_User_GrantedBy",
                        column: x => x.GrantedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserAuthorityGrant_User_RevokedBy",
                        column: x => x.RevokedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserAuthorityGrant_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteRatingResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AnnualPremium = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    TermPremium = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    GrossPayable = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    BrokerCommission = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteRatingResult", x => x.Id);
                    table.UniqueConstraint("AK_QuoteRatingResult_Id_CycleId_QuoteId", x => new { x.Id, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_QuoteRatingResult_AnnualPremium", "[AnnualPremium] BETWEEN 0 AND 9999999999999.99");
                    table.CheckConstraint("CK_QuoteRatingResult_BrokerCommission", "[BrokerCommission] BETWEEN 0 AND 9999999999999.99");
                    table.CheckConstraint("CK_QuoteRatingResult_CompletedAt_Utc", "DATEPART(TZOFFSET,[CompletedAt]) = 0");
                    table.CheckConstraint("CK_QuoteRatingResult_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteRatingResult_ExpiresAt_Utc", "DATEPART(TZOFFSET,[ExpiresAt]) = 0");
                    table.CheckConstraint("CK_QuoteRatingResult_Fee", "[Fee] BETWEEN 0 AND 9999999999999.99");
                    table.CheckConstraint("CK_QuoteRatingResult_GrossPayable", "[GrossPayable] BETWEEN 0 AND 9999999999999.99");
                    table.CheckConstraint("CK_QuoteRatingResult_Interval", "[CompletedAt]>=[CreatedAt] AND [ExpiresAt]>[CompletedAt]");
                    table.CheckConstraint("CK_QuoteRatingResult_Outcome", "[Outcome] IN ('rated','rejected')");
                    table.CheckConstraint("CK_QuoteRatingResult_ResultJson", "ISJSON([ResultJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[ResultJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_QuoteRatingResult_Tax", "[Tax] BETWEEN 0 AND 9999999999999.99");
                    table.CheckConstraint("CK_QuoteRatingResult_TermPremium", "[TermPremium] BETWEEN 0 AND 9999999999999.99");
                    table.CheckConstraint("CK_QuoteRatingResult_Totals", "[GrossPayable]=[TermPremium]+[Tax]+[Fee] AND [BrokerCommission]<=[TermPremium] AND ([Outcome]<>'rated' OR ([AnnualPremium]>0 AND [TermPremium]>0))");
                    table.ForeignKey(
                        name: "FK_QuoteRatingResult_AdapterAttempt_AttemptId_WorkId",
                        columns: x => new { x.AttemptId, x.WorkId },
                        principalTable: "AdapterAttempt",
                        principalColumns: new[] { "Id", "WorkId" });
                    table.ForeignKey(
                        name: "FK_QuoteRatingResult_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteReferral",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    RuleCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Dimension = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RiskItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredAuthorityJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteReferral", x => x.Id);
                    table.UniqueConstraint("AK_QuoteReferral_Id_CycleId_QuoteId", x => new { x.Id, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_QuoteReferral_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteReferral_RequiredAuthorityJson", "ISJSON([RequiredAuthorityJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[RequiredAuthorityJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_QuoteReferral_Sequence", "[Sequence]>0");
                    table.CheckConstraint("CK_QuoteReferral_State", "[State] IN ('open','approved','conditional','queried','declined','superseded')");
                    table.CheckConstraint("CK_QuoteReferral_Target", "([RiskItemId] IS NULL AND [TargetKey]='00000000-0000-0000-0000-000000000000') OR ([RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000' AND [TargetKey]=[RiskItemId])");
                    table.CheckConstraint("CK_QuoteReferral_Text", "LEN(TRIM([Reason]))>0 AND LEN(TRIM([RuleCode]))>0 AND LEN(TRIM([Dimension]))>0");
                    table.CheckConstraint("CK_QuoteReferral_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_QuoteReferral_QuoteRatingResult_RatingId_CycleId_QuoteId",
                        columns: x => new { x.RatingId, x.CycleId, x.QuoteId },
                        principalTable: "QuoteRatingResult",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteReferral_User_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteReferral_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UnderwritingCycle",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyTermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingRuleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BinderVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PricingInputHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    InputJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrentRatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SupersededReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnderwritingCycle", x => x.Id);
                    table.UniqueConstraint("AK_UnderwritingCycle_Id_QuoteId", x => new { x.Id, x.QuoteId });
                    table.UniqueConstraint("AK_UnderwritingCycle_Id_QuoteId_WorkId_RatingRuleVersionId_PricingInputHash", x => new { x.Id, x.QuoteId, x.WorkId, x.RatingRuleVersionId, x.PricingInputHash });
                    table.CheckConstraint("CK_UnderwritingCycle_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RequestedBy]");
                    table.CheckConstraint("CK_UnderwritingCycle_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_UnderwritingCycle_EndsAt_Utc", "DATEPART(TZOFFSET,[EndsAt]) = 0");
                    table.CheckConstraint("CK_UnderwritingCycle_Hash", "[PricingInputHash]<>0x0000000000000000000000000000000000000000000000000000000000000000");
                    table.CheckConstraint("CK_UnderwritingCycle_InputJson", "ISJSON([InputJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[InputJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_UnderwritingCycle_Interval", "[StartsAt]<[EndsAt]");
                    table.CheckConstraint("CK_UnderwritingCycle_Sequence", "[Sequence]>0");
                    table.CheckConstraint("CK_UnderwritingCycle_StartsAt_Utc", "DATEPART(TZOFFSET,[StartsAt]) = 0");
                    table.CheckConstraint("CK_UnderwritingCycle_State", "[State] IN ('rating-pending','rated','failed','superseded','bound')");
                    table.CheckConstraint("CK_UnderwritingCycle_Superseded", "([State]='superseded' AND [SupersededAt] IS NOT NULL AND [SupersededAt]>=[CreatedAt] AND [SupersededReason] IS NOT NULL AND LEN(TRIM([SupersededReason]))>0) OR ([State]<>'superseded' AND [SupersededAt] IS NULL AND [SupersededReason] IS NULL)");
                    table.CheckConstraint("CK_UnderwritingCycle_SupersededAt_Utc", "DATEPART(TZOFFSET,[SupersededAt]) = 0");
                    table.CheckConstraint("CK_UnderwritingCycle_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_UnderwritingCycle_AuthorityVersion_AuthorityVersionId_ProductVersionId_BinderVersionId",
                        columns: x => new { x.AuthorityVersionId, x.ProductVersionId, x.BinderVersionId },
                        principalTable: "AuthorityVersion",
                        principalColumns: new[] { "Id", "ProductVersionId", "BinderVersionId" });
                    table.ForeignKey(
                        name: "FK_UnderwritingCycle_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UnderwritingCycle_QuoteRatingResult_CurrentRatingId_Id_QuoteId",
                        columns: x => new { x.CurrentRatingId, x.Id, x.QuoteId },
                        principalTable: "QuoteRatingResult",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_UnderwritingCycle_QuoteRevision_QuoteRevisionId_QuoteId_AgencyId_ClientId_RelationshipId_ProductId_ProductVersionId_AgencyTe~",
                        columns: x => new { x.QuoteRevisionId, x.QuoteId, x.AgencyId, x.ClientId, x.RelationshipId, x.ProductId, x.ProductVersionId, x.AgencyTermsVersionId },
                        principalTable: "QuoteRevision",
                        principalColumns: new[] { "Id", "QuoteId", "AgencyId", "ClientId", "RelationshipId", "ProductId", "ProductVersionId", "AgencyTermsVersionId" });
                    table.ForeignKey(
                        name: "FK_UnderwritingCycle_RatingRuleVersion_RatingRuleVersionId_ProductId",
                        columns: x => new { x.RatingRuleVersionId, x.ProductId },
                        principalTable: "RatingRuleVersion",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_UnderwritingCycle_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UnderwritingCycle_User_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteSubmission",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SubmittedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoutingVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteSubmission", x => x.Id);
                    table.CheckConstraint("CK_QuoteSubmission_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[SubmittedBy]");
                    table.CheckConstraint("CK_QuoteSubmission_Assignment", "[AssignedUserId] IS NOT NULL OR [AssignedTeamId] IS NOT NULL");
                    table.CheckConstraint("CK_QuoteSubmission_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteSubmission_Reason", "LEN(TRIM([Reason]))>0 AND LEN(TRIM([OperationKey]))>0");
                    table.CheckConstraint("CK_QuoteSubmission_Sequence", "[Sequence]>0");
                    table.ForeignKey(
                        name: "FK_QuoteSubmission_SettingVersion_RoutingVersionId",
                        column: x => x.RoutingVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteSubmission_Team_AssignedTeamId",
                        column: x => x.AssignedTeamId,
                        principalTable: "Team",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteSubmission_UnderwritingCycle_CycleId_QuoteId",
                        columns: x => new { x.CycleId, x.QuoteId },
                        principalTable: "UnderwritingCycle",
                        principalColumns: new[] { "Id", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteSubmission_User_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteSubmission_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteSubmission_User_SubmittedBy",
                        column: x => x.SubmittedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Quote_CurrentUnderwritingCycleId_Id",
                table: "Quote",
                columns: new[] { "CurrentUnderwritingCycleId", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Quote_State",
                table: "Quote",
                sql: "[State] IN ('draft','rating-pending','rated','referred','approved','sent','accepted','declined','bound','withdrawn')");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorityVersion_BinderVersionId_ProductId",
                table: "AuthorityVersion",
                columns: new[] { "BinderVersionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorityVersion_CreatedBy",
                table: "AuthorityVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorityVersion_ProductVersionId_BinderVersionId_Version",
                table: "AuthorityVersion",
                columns: new[] { "ProductVersionId", "BinderVersionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthorityVersion_ProductVersionId_ProductId",
                table: "AuthorityVersion",
                columns: new[] { "ProductVersionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_BinderVersion_CreatedBy",
                table: "BinderVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_BinderVersion_ProductId",
                table: "BinderVersion",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BinderVersion_ProviderId_ProductId_Version",
                table: "BinderVersion",
                columns: new[] { "ProviderId", "ProductId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRatingResult_AttemptId",
                table: "QuoteRatingResult",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRatingResult_AttemptId_WorkId",
                table: "QuoteRatingResult",
                columns: new[] { "AttemptId", "WorkId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRatingResult_CreatedBy",
                table: "QuoteRatingResult",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRatingResult_CycleId_QuoteId_WorkId_RuleVersionId_InputHash",
                table: "QuoteRatingResult",
                columns: new[] { "CycleId", "QuoteId", "WorkId", "RuleVersionId", "InputHash" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRatingResult_WorkId",
                table: "QuoteRatingResult",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReferral_AssignedUserId",
                table: "QuoteReferral",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReferral_CreatedBy",
                table: "QuoteReferral",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReferral_CycleId_RuleCode_TargetKey",
                table: "QuoteReferral",
                columns: new[] { "CycleId", "RuleCode", "TargetKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReferral_CycleId_Sequence",
                table: "QuoteReferral",
                columns: new[] { "CycleId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReferral_RatingId_CycleId_QuoteId",
                table: "QuoteReferral",
                columns: new[] { "RatingId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReferral_State_AssignedUserId_QuoteId",
                table: "QuoteReferral",
                columns: new[] { "State", "AssignedUserId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteSubmission_AssignedTeamId",
                table: "QuoteSubmission",
                column: "AssignedTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteSubmission_AssignedUserId",
                table: "QuoteSubmission",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteSubmission_CreatedBy",
                table: "QuoteSubmission",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteSubmission_CycleId_QuoteId",
                table: "QuoteSubmission",
                columns: new[] { "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteSubmission_CycleId_Sequence",
                table: "QuoteSubmission",
                columns: new[] { "CycleId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteSubmission_QuoteId_OperationKey",
                table: "QuoteSubmission",
                columns: new[] { "QuoteId", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteSubmission_RoutingVersionId",
                table: "QuoteSubmission",
                column: "RoutingVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteSubmission_SubmittedBy",
                table: "QuoteSubmission",
                column: "SubmittedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RatingRuleVersion_CreatedBy",
                table: "RatingRuleVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RatingRuleVersion_ProductId_Version",
                table: "RatingRuleVersion",
                columns: new[] { "ProductId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingCycle_AuthorityVersionId_ProductVersionId_BinderVersionId",
                table: "UnderwritingCycle",
                columns: new[] { "AuthorityVersionId", "ProductVersionId", "BinderVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingCycle_CreatedBy",
                table: "UnderwritingCycle",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingCycle_CurrentRatingId_Id_QuoteId",
                table: "UnderwritingCycle",
                columns: new[] { "CurrentRatingId", "Id", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingCycle_QuoteId_Sequence",
                table: "UnderwritingCycle",
                columns: new[] { "QuoteId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingCycle_QuoteRevisionId_QuoteId_AgencyId_ClientId_RelationshipId_ProductId_ProductVersionId_AgencyTermsVersionId",
                table: "UnderwritingCycle",
                columns: new[] { "QuoteRevisionId", "QuoteId", "AgencyId", "ClientId", "RelationshipId", "ProductId", "ProductVersionId", "AgencyTermsVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingCycle_RatingRuleVersionId_ProductId",
                table: "UnderwritingCycle",
                columns: new[] { "RatingRuleVersionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingCycle_RequestedBy",
                table: "UnderwritingCycle",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingCycle_WorkId",
                table: "UnderwritingCycle",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAuthorityGrant_AuthorityVersionId",
                table: "UserAuthorityGrant",
                column: "AuthorityVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAuthorityGrant_CreatedBy",
                table: "UserAuthorityGrant",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserAuthorityGrant_GrantedBy",
                table: "UserAuthorityGrant",
                column: "GrantedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserAuthorityGrant_RevokedBy",
                table: "UserAuthorityGrant",
                column: "RevokedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserAuthorityGrant_UserId_AuthorityVersionId_EffectiveFrom",
                table: "UserAuthorityGrant",
                columns: new[] { "UserId", "AuthorityVersionId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Quote_UnderwritingCycle_CurrentUnderwritingCycleId_Id",
                table: "Quote",
                columns: new[] { "CurrentUnderwritingCycleId", "Id" },
                principalTable: "UnderwritingCycle",
                principalColumns: new[] { "Id", "QuoteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_QuoteRatingResult_UnderwritingCycle_CycleId_QuoteId_WorkId_RuleVersionId_InputHash",
                table: "QuoteRatingResult",
                columns: new[] { "CycleId", "QuoteId", "WorkId", "RuleVersionId", "InputHash" },
                principalTable: "UnderwritingCycle",
                principalColumns: new[] { "Id", "QuoteId", "WorkId", "RatingRuleVersionId", "PricingInputHash" });
            AddUnderwritingGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveUnderwritingGuards(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_Quote_UnderwritingCycle_CurrentUnderwritingCycleId_Id",
                table: "Quote");

            migrationBuilder.DropForeignKey(
                name: "FK_AuthorityVersion_BinderVersion_BinderVersionId_ProductId",
                table: "AuthorityVersion");

            migrationBuilder.DropForeignKey(
                name: "FK_QuoteRatingResult_UnderwritingCycle_CycleId_QuoteId_WorkId_RuleVersionId_InputHash",
                table: "QuoteRatingResult");

            migrationBuilder.DropTable(
                name: "QuoteReferral");

            migrationBuilder.DropTable(
                name: "QuoteSubmission");

            migrationBuilder.DropTable(
                name: "UserAuthorityGrant");

            migrationBuilder.DropTable(
                name: "BinderVersion");

            migrationBuilder.DropTable(
                name: "UnderwritingCycle");

            migrationBuilder.DropTable(
                name: "AuthorityVersion");

            migrationBuilder.DropTable(
                name: "QuoteRatingResult");

            migrationBuilder.DropTable(
                name: "RatingRuleVersion");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_QuoteRevision_Id_QuoteId_AgencyId_ClientId_RelationshipId_ProductId_ProductVersionId_AgencyTermsVersionId",
                table: "QuoteRevision");

            migrationBuilder.DropIndex(
                name: "IX_Quote_CurrentUnderwritingCycleId_Id",
                table: "Quote");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Quote_State",
                table: "Quote");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AdapterAttempt_Id_WorkId",
                table: "AdapterAttempt");

            migrationBuilder.DropColumn(
                name: "CurrentUnderwritingCycleId",
                table: "Quote");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Quote_State",
                table: "Quote",
                sql: "[State] IN ('draft','withdrawn')");
        }
    }
}
