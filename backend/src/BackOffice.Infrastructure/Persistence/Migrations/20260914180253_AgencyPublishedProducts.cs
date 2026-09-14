using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgencyPublishedProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgencyProduct",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyTermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    BrokerCommissionBasisPoints = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyProduct", x => x.Id);
                    table.CheckConstraint("CK_AgencyProduct_Commission", "[BrokerCommissionBasisPoints] BETWEEN 0 AND 10000");
                    table.CheckConstraint("CK_AgencyProduct_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_AgencyProduct_AgencyTermsVersion_AgencyTermsVersionId",
                        column: x => x.AgencyTermsVersionId,
                        principalTable: "AgencyTermsVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyProduct_ProductVersion_ProductVersionId",
                        column: x => x.ProductVersionId,
                        principalTable: "ProductVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyProduct_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyProduct_AgencyTermsVersionId_ProductVersionId",
                table: "AgencyProduct",
                columns: new[] { "AgencyTermsVersionId", "ProductVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgencyProduct_CreatedBy",
                table: "AgencyProduct",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyProduct_ProductVersionId",
                table: "AgencyProduct",
                column: "ProductVersionId");
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_AgencyProduct_Immutable] ON [AgencyProduct] AFTER INSERT,UPDATE,DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted) THROW 51000,'Published product grants cannot be changed or deleted.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(
                    SELECT 1 FROM AgencyTermsVersion v CROSS APPLY OPENJSON(v.Snapshot,'$.products') WITH(
                      ProductVersionId uniqueidentifier '$.productVersionId', EffectiveFrom date '$.effectiveFrom', BrokerCommissionBasisPoints int '$.brokerCommissionBasisPoints') p
                    WHERE v.Id=i.AgencyTermsVersionId AND v.CreatedBy=i.CreatedBy AND v.CreatedAt=i.CreatedAt
                      AND p.ProductVersionId=i.ProductVersionId AND p.EffectiveFrom=i.EffectiveFrom AND p.BrokerCommissionBasisPoints=i.BrokerCommissionBasisPoints))
                    THROW 51000,'Product grants must match their published terms snapshot.',1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_AgencyTermsVersion_Products] ON [AgencyTermsVersion] AFTER INSERT AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE LEFT(LTRIM(COALESCE(JSON_QUERY(i.Snapshot,'$.products'),'')),1)<>'['
                    OR (SELECT COUNT(*) FROM OPENJSON(i.Snapshot,'$.products')) NOT BETWEEN 1 AND 3
                    OR NOT EXISTS(SELECT 1 FROM OPENJSON(i.Snapshot,'$.products') WITH(EffectiveFrom date '$.effectiveFrom') p WHERE p.EffectiveFrom=i.EffectiveFrom)
                    OR EXISTS(SELECT 1 FROM OPENJSON(i.Snapshot,'$.products') WITH(EffectiveFrom date '$.effectiveFrom') p WHERE p.EffectiveFrom<i.EffectiveFrom))
                    THROW 51000,'Published terms require a complete effective product set.',1;
                  INSERT INTO AgencyProduct(Id,AgencyTermsVersionId,ProductVersionId,EffectiveFrom,BrokerCommissionBasisPoints,CreatedAt,CreatedBy)
                    SELECT NEWID(),i.Id,p.ProductVersionId,p.EffectiveFrom,p.BrokerCommissionBasisPoints,i.CreatedAt,i.CreatedBy
                    FROM inserted i CROSS APPLY OPENJSON(i.Snapshot,'$.products') WITH(
                      ProductVersionId uniqueidentifier '$.productVersionId', EffectiveFrom date '$.effectiveFrom', BrokerCommissionBasisPoints int '$.brokerCommissionBasisPoints') p;
                END
                """);
            // Backfill any versions created before this additive migration.
            migrationBuilder.Sql("""
                  IF EXISTS(SELECT 1 FROM AgencyTermsVersion i WHERE LEFT(LTRIM(COALESCE(JSON_QUERY(i.Snapshot,'$.products'),'')),1)<>'['
                    OR (SELECT COUNT(*) FROM OPENJSON(i.Snapshot,'$.products')) NOT BETWEEN 1 AND 3
                    OR NOT EXISTS(SELECT 1 FROM OPENJSON(i.Snapshot,'$.products') WITH(EffectiveFrom date '$.effectiveFrom') p WHERE p.EffectiveFrom=i.EffectiveFrom)
                    OR EXISTS(SELECT 1 FROM OPENJSON(i.Snapshot,'$.products') WITH(EffectiveFrom date '$.effectiveFrom') p WHERE p.EffectiveFrom<i.EffectiveFrom))
                    THROW 51000,'Published terms require a complete effective product set.',1;
                  INSERT INTO AgencyProduct(Id,AgencyTermsVersionId,ProductVersionId,EffectiveFrom,BrokerCommissionBasisPoints,CreatedAt,CreatedBy)
                    SELECT NEWID(),i.Id,p.ProductVersionId,p.EffectiveFrom,p.BrokerCommissionBasisPoints,i.CreatedAt,i.CreatedBy
                    FROM AgencyTermsVersion i CROSS APPLY OPENJSON(i.Snapshot,'$.products') WITH(
                      ProductVersionId uniqueidentifier '$.productVersionId', EffectiveFrom date '$.effectiveFrom', BrokerCommissionBasisPoints int '$.brokerCommissionBasisPoints') p;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_AgencyTermsVersion_Products]");
            migrationBuilder.DropTable(
                name: "AgencyProduct");
        }
    }
}
