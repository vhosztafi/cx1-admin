using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialExposureStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommercialExposureBook",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialExposureBook", x => x.Id);
                    table.CheckConstraint("CK_CommercialExposureBook_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CommercialExposureBook_Publisher", "[CreatedBy] IS NOT NULL AND LEN(TRIM([Code]))>0");
                    table.ForeignKey(
                        name: "FK_CommercialExposureBook_CapacityProvider_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "CapacityProvider",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CommercialExposureBook_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CommercialExposureBook_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CommercialExposureBinder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BinderVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialExposureBinder", x => x.Id);
                    table.UniqueConstraint("AK_CommercialExposureBinder_BookId_BinderVersionId", x => new { x.BookId, x.BinderVersionId });
                    table.CheckConstraint("CK_CommercialExposureBinder_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CommercialExposureBinder_Publisher", "[CreatedBy] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_CommercialExposureBinder_BinderVersion_BinderVersionId",
                        column: x => x.BinderVersionId,
                        principalTable: "BinderVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CommercialExposureBinder_CommercialExposureBook_BookId",
                        column: x => x.BookId,
                        principalTable: "CommercialExposureBook",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CommercialExposureBinder_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CommercialExposureLimitVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    District = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(16,2)", precision: 16, scale: 2, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SupersedesLimitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublicationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialExposureLimitVersion", x => x.Id);
                    table.CheckConstraint("CK_CommercialExposureLimitVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CommercialExposureLimitVersion_District", "([District]='*' AND DATALENGTH([District])=2) OR ([District]='GIR' OR [District] LIKE '[A-PR-UWYZ][0-9]' OR [District] LIKE '[A-PR-UWYZ][0-9][0-9]' OR [District] LIKE '[A-PR-UWYZ][A-HK-Y][0-9]' OR [District] LIKE '[A-PR-UWYZ][A-HK-Y][0-9][0-9]' OR [District] LIKE '[A-PR-UWYZ][0-9][A-HJKPSTUW]' OR [District] LIKE '[A-PR-UWYZ][A-HK-Y][0-9][ABEHMNPRVWXY]') AND DATALENGTH([District])=2*LEN([District])");
                    table.CheckConstraint("CK_CommercialExposureLimitVersion_EffectiveFrom_Utc", "DATEPART(TZOFFSET,[EffectiveFrom]) = 0");
                    table.CheckConstraint("CK_CommercialExposureLimitVersion_EffectiveTo_Utc", "DATEPART(TZOFFSET,[EffectiveTo]) = 0");
                    table.CheckConstraint("CK_CommercialExposureLimitVersion_Publication", "[CreatedBy] IS NOT NULL AND [Version]>0 AND [EffectiveFrom]<[EffectiveTo] AND [Amount] BETWEEN 0 AND 9999999999999.99 AND [ContentHash]<>0x0000000000000000000000000000000000000000000000000000000000000000");
                    table.CheckConstraint("CK_CommercialExposureLimitVersion_PublicationJson_Json", "ISJSON([PublicationJson]) = 1");
                    table.CheckConstraint("CK_CommercialExposureLimitVersion_PublishedAt_Utc", "DATEPART(TZOFFSET,[PublishedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_CommercialExposureLimitVersion_CommercialExposureBook_BookId",
                        column: x => x.BookId,
                        principalTable: "CommercialExposureBook",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CommercialExposureLimitVersion_CommercialExposureLimitVersion_SupersedesLimitId",
                        column: x => x.SupersedesLimitId,
                        principalTable: "CommercialExposureLimitVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CommercialExposureLimitVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CommercialExposureVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BinderVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    TermStartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TermEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TransactionKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TransactionSequence = table.Column<int>(type: "int", nullable: false),
                    SliceOrdinal = table.Column<int>(type: "int", nullable: false),
                    LocationsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialExposureVersion", x => x.Id);
                    table.CheckConstraint("CK_CommercialExposureVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CommercialExposureVersion_EffectiveAt_Utc", "DATEPART(TZOFFSET,[EffectiveAt]) = 0");
                    table.CheckConstraint("CK_CommercialExposureVersion_Locations", "ISJSON([LocationsJson], ARRAY)=1 AND ([TransactionKind]<>'cancellation' OR [LocationsJson]='[]')");
                    table.CheckConstraint("CK_CommercialExposureVersion_ProcessedAt_Utc", "DATEPART(TZOFFSET,[ProcessedAt]) = 0");
                    table.CheckConstraint("CK_CommercialExposureVersion_TermEndsAt_Utc", "DATEPART(TZOFFSET,[TermEndsAt]) = 0");
                    table.CheckConstraint("CK_CommercialExposureVersion_TermStartsAt_Utc", "DATEPART(TZOFFSET,[TermStartsAt]) = 0");
                    table.CheckConstraint("CK_CommercialExposureVersion_Timeline", "[CreatedBy] IS NOT NULL AND [TermStartsAt]<[TermEndsAt] AND [EffectiveAt]>=[TermStartsAt] AND [EffectiveAt]<[TermEndsAt] AND [TransactionSequence]>0 AND [SliceOrdinal]>0 AND [TransactionKind] IN ('new-business','adjustment','renewal','cancellation')");
                    table.ForeignKey(
                        name: "FK_CommercialExposureVersion_CommercialExposureBinder_BookId_BinderVersionId",
                        columns: x => new { x.BookId, x.BinderVersionId },
                        principalTable: "CommercialExposureBinder",
                        principalColumns: new[] { "BookId", "BinderVersionId" });
                    table.ForeignKey(
                        name: "FK_CommercialExposureVersion_PolicyVersion_VersionId_TransactionId_TermId_PolicyId",
                        columns: x => new { x.VersionId, x.TransactionId, x.TermId, x.PolicyId },
                        principalTable: "PolicyVersion",
                        principalColumns: new[] { "Id", "TransactionId", "TermId", "PolicyId" });
                    table.ForeignKey(
                        name: "FK_CommercialExposureVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CommercialExposureLocation",
                columns: table => new
                {
                    ExposureVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RiskItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    District = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SumInsured = table.Column<decimal>(type: "decimal(16,2)", precision: 16, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialExposureLocation", x => new { x.ExposureVersionId, x.RiskItemId });
                    table.CheckConstraint("CK_CommercialExposureLocation_District", "([District]='GIR' OR [District] LIKE '[A-PR-UWYZ][0-9]' OR [District] LIKE '[A-PR-UWYZ][0-9][0-9]' OR [District] LIKE '[A-PR-UWYZ][A-HK-Y][0-9]' OR [District] LIKE '[A-PR-UWYZ][A-HK-Y][0-9][0-9]' OR [District] LIKE '[A-PR-UWYZ][0-9][A-HJKPSTUW]' OR [District] LIKE '[A-PR-UWYZ][A-HK-Y][0-9][ABEHMNPRVWXY]') AND DATALENGTH([District])=2*LEN([District])");
                    table.CheckConstraint("CK_CommercialExposureLocation_Property", "[RiskItemId]<>'00000000-0000-0000-0000-000000000000' AND [SumInsured] BETWEEN 0 AND 29999999999999.97");
                    table.ForeignKey(
                        name: "FK_CommercialExposureLocation_CommercialExposureVersion_ExposureVersionId",
                        column: x => x.ExposureVersionId,
                        principalTable: "CommercialExposureVersion",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureBinder_BinderVersionId",
                table: "CommercialExposureBinder",
                column: "BinderVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureBinder_CreatedBy",
                table: "CommercialExposureBinder",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureBook_Code",
                table: "CommercialExposureBook",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureBook_CreatedBy",
                table: "CommercialExposureBook",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureBook_ProductId_ProviderId",
                table: "CommercialExposureBook",
                columns: new[] { "ProductId", "ProviderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureBook_ProviderId",
                table: "CommercialExposureBook",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureLimitVersion_BookId_District_Version",
                table: "CommercialExposureLimitVersion",
                columns: new[] { "BookId", "District", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureLimitVersion_BookId_PublishedAt_EffectiveFrom",
                table: "CommercialExposureLimitVersion",
                columns: new[] { "BookId", "PublishedAt", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureLimitVersion_CreatedBy",
                table: "CommercialExposureLimitVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureLimitVersion_SupersedesLimitId",
                table: "CommercialExposureLimitVersion",
                column: "SupersedesLimitId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureLocation_District_ExposureVersionId",
                table: "CommercialExposureLocation",
                columns: new[] { "District", "ExposureVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureVersion_BookId_BinderVersionId",
                table: "CommercialExposureVersion",
                columns: new[] { "BookId", "BinderVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureVersion_BookId_ProcessedAt_PolicyId",
                table: "CommercialExposureVersion",
                columns: new[] { "BookId", "ProcessedAt", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureVersion_CreatedBy",
                table: "CommercialExposureVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureVersion_VersionId",
                table: "CommercialExposureVersion",
                column: "VersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureVersion_VersionId_TransactionId_TermId_PolicyId",
                table: "CommercialExposureVersion",
                columns: new[] { "VersionId", "TransactionId", "TermId", "PolicyId" });
            AddExposureGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropExposureGuards(migrationBuilder);
            migrationBuilder.DropTable(
                name: "CommercialExposureLimitVersion");

            migrationBuilder.DropTable(
                name: "CommercialExposureLocation");

            migrationBuilder.DropTable(
                name: "CommercialExposureVersion");

            migrationBuilder.DropTable(
                name: "CommercialExposureBinder");

            migrationBuilder.DropTable(
                name: "CommercialExposureBook");
        }
    }
}
