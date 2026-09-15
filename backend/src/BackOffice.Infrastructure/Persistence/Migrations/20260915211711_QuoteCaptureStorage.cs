using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteCaptureStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "QuoteReferenceSequence",
                maxValue: 9999999999L);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProductVersion_Id_ProductId",
                table: "ProductVersion",
                columns: new[] { "Id", "ProductId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AgencyTermsVersion_Id_AgencyId",
                table: "AgencyTermsVersion",
                columns: new[] { "Id", "AgencyId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AgencyProduct_AgencyTermsVersionId_ProductVersionId",
                table: "AgencyProduct",
                columns: new[] { "AgencyTermsVersionId", "ProductVersionId" });

            migrationBuilder.CreateTable(
                name: "Quote",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "NEXT VALUE FOR [QuoteReferenceSequence]"),
                    Reference = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, computedColumnSql: "'QT-MT-' + RIGHT('0000000000' + CONVERT(varchar(20), [Number]), 10)", stored: true),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CaptureClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CaptureClosedReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AssignedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClonedFromQuoteRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quote", x => x.Id);
                    table.UniqueConstraint("AK_Quote_Id_AgencyId_ProductId", x => new { x.Id, x.AgencyId, x.ProductId });
                    table.CheckConstraint("CK_Quote_CaptureClosedAt_Utc", "DATEPART(TZOFFSET,[CaptureClosedAt]) = 0");
                    table.CheckConstraint("CK_Quote_CaptureClosure", "([CaptureClosedAt] IS NULL AND [CaptureClosedReason] IS NULL) OR ([CaptureClosedAt] IS NOT NULL AND [CaptureClosedAt]>=[CreatedAt] AND [CaptureClosedReason] IS NOT NULL AND LEN(TRIM([CaptureClosedReason]))>0)");
                    table.CheckConstraint("CK_Quote_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Quote_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_Quote_Number", "[Number] BETWEEN 1 AND 9999999999");
                    table.CheckConstraint("CK_Quote_State", "[State] IN ('draft','withdrawn')");
                    table.CheckConstraint("CK_Quote_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_Quote_ClientAgencyRelationship_RelationshipId_ClientId_AgencyId",
                        columns: x => new { x.RelationshipId, x.ClientId, x.AgencyId },
                        principalTable: "ClientAgencyRelationship",
                        principalColumns: new[] { "Id", "ClientId", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_Quote_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Quote_User_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Quote_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteRegistration",
                columns: table => new
                {
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NormalizedRegistration = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteRegistration", x => new { x.QuoteId, x.VehicleId });
                    table.CheckConstraint("CK_QuoteRegistration_Registration", "LEN([NormalizedRegistration]) BETWEEN 2 AND 12 AND [NormalizedRegistration] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_QuoteRegistration_Vehicle", "[VehicleId]<>'00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_QuoteRegistration_Quote_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quote",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    ProductVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyTermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    QuestionSetVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReferenceVersionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProposalJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TermIntentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SavedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteRevision", x => x.Id);
                    table.UniqueConstraint("AK_QuoteRevision_Id_QuoteId", x => new { x.Id, x.QuoteId });
                    table.CheckConstraint("CK_QuoteRevision_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[SavedBy] AND [SavedAt]>=[CreatedAt]");
                    table.CheckConstraint("CK_QuoteRevision_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteRevision_Hash", "[ContentHash]<>0x0000000000000000000000000000000000000000000000000000000000000000");
                    table.CheckConstraint("CK_QuoteRevision_Number", "[Number]>0");
                    table.CheckConstraint("CK_QuoteRevision_ProposalJson", "ISJSON([ProposalJson], OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[ProposalJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_QuoteRevision_ReferenceVersionsJson", "ISJSON([ReferenceVersionsJson], OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[ReferenceVersionsJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_QuoteRevision_SavedAt_Utc", "DATEPART(TZOFFSET,[SavedAt]) = 0");
                    table.CheckConstraint("CK_QuoteRevision_TermIntentJson", "ISJSON([TermIntentJson], OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[TermIntentJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_QuoteRevision_Versions", "LEN(TRIM([SchemaVersion]))>0 AND LEN(TRIM([QuestionSetVersion]))>0 AND COALESCE(JSON_VALUE([ProposalJson],'$.schemaVersion'),'')=[SchemaVersion]");
                    table.ForeignKey(
                        name: "FK_QuoteRevision_AgencyProduct_AgencyTermsVersionId_ProductVersionId",
                        columns: x => new { x.AgencyTermsVersionId, x.ProductVersionId },
                        principalTable: "AgencyProduct",
                        principalColumns: new[] { "AgencyTermsVersionId", "ProductVersionId" });
                    table.ForeignKey(
                        name: "FK_QuoteRevision_AgencyTermsVersion_AgencyTermsVersionId_AgencyId",
                        columns: x => new { x.AgencyTermsVersionId, x.AgencyId },
                        principalTable: "AgencyTermsVersion",
                        principalColumns: new[] { "Id", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_QuoteRevision_ProductVersion_ProductVersionId_ProductId",
                        columns: x => new { x.ProductVersionId, x.ProductId },
                        principalTable: "ProductVersion",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_QuoteRevision_Quote_QuoteId_AgencyId_ProductId",
                        columns: x => new { x.QuoteId, x.AgencyId, x.ProductId },
                        principalTable: "Quote",
                        principalColumns: new[] { "Id", "AgencyId", "ProductId" });
                    table.ForeignKey(
                        name: "FK_QuoteRevision_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteRevision_User_SavedBy",
                        column: x => x.SavedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteActivity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteActivity", x => x.Id);
                    table.CheckConstraint("CK_QuoteActivity_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId]");
                    table.CheckConstraint("CK_QuoteActivity_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteActivity_EventType", "LEN(TRIM([EventType]))>0");
                    table.CheckConstraint("CK_QuoteActivity_OccurredAt_Utc", "DATEPART(TZOFFSET,[OccurredAt]) = 0");
                    table.ForeignKey(
                        name: "FK_QuoteActivity_QuoteRevision_RevisionId_QuoteId",
                        columns: x => new { x.RevisionId, x.QuoteId },
                        principalTable: "QuoteRevision",
                        principalColumns: new[] { "Id", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteActivity_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteActivity_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Quote_AgencyId_State_UpdatedAt_Id",
                table: "Quote",
                columns: new[] { "AgencyId", "State", "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Quote_AssignedUserId",
                table: "Quote",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Quote_ClientId_UpdatedAt_Id",
                table: "Quote",
                columns: new[] { "ClientId", "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Quote_ClonedFromQuoteRevisionId",
                table: "Quote",
                column: "ClonedFromQuoteRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Quote_CreatedBy",
                table: "Quote",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Quote_CurrentRevisionId_Id",
                table: "Quote",
                columns: new[] { "CurrentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Quote_Number",
                table: "Quote",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quote_ProductId",
                table: "Quote",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Quote_Reference",
                table: "Quote",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quote_RelationshipId_ClientId_AgencyId",
                table: "Quote",
                columns: new[] { "RelationshipId", "ClientId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteActivity_ActorId",
                table: "QuoteActivity",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteActivity_CreatedBy",
                table: "QuoteActivity",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteActivity_QuoteId_OccurredAt_Id",
                table: "QuoteActivity",
                columns: new[] { "QuoteId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteActivity_RevisionId_QuoteId",
                table: "QuoteActivity",
                columns: new[] { "RevisionId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRegistration_NormalizedRegistration_QuoteId",
                table: "QuoteRegistration",
                columns: new[] { "NormalizedRegistration", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRevision_AgencyTermsVersionId_AgencyId",
                table: "QuoteRevision",
                columns: new[] { "AgencyTermsVersionId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRevision_AgencyTermsVersionId_ProductVersionId",
                table: "QuoteRevision",
                columns: new[] { "AgencyTermsVersionId", "ProductVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRevision_CreatedBy",
                table: "QuoteRevision",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRevision_ProductVersionId_ProductId",
                table: "QuoteRevision",
                columns: new[] { "ProductVersionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRevision_QuoteId_AgencyId_ProductId",
                table: "QuoteRevision",
                columns: new[] { "QuoteId", "AgencyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRevision_QuoteId_Number",
                table: "QuoteRevision",
                columns: new[] { "QuoteId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRevision_SavedBy",
                table: "QuoteRevision",
                column: "SavedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_Quote_QuoteRevision_ClonedFromQuoteRevisionId",
                table: "Quote",
                column: "ClonedFromQuoteRevisionId",
                principalTable: "QuoteRevision",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Quote_QuoteRevision_CurrentRevisionId_Id",
                table: "Quote",
                columns: new[] { "CurrentRevisionId", "Id" },
                principalTable: "QuoteRevision",
                principalColumns: new[] { "Id", "QuoteId" });

            migrationBuilder.Sql("CREATE TRIGGER [TR_QuoteRevision_AppendOnly] ON [QuoteRevision] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51060, 'Quote revisions are append-only.', 1; END;");
            migrationBuilder.Sql("CREATE TRIGGER [TR_QuoteActivity_AppendOnly] ON [QuoteActivity] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51061, 'Quote activity is append-only.', 1; END;");
            migrationBuilder.Sql("CREATE TRIGGER [TR_Quote_NumberImmutable] ON [Quote] AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE i.Number<>d.Number) THROW 51062, 'Quote numbers are immutable.', 1; END;");
            migrationBuilder.Sql("CREATE TRIGGER [TR_QuoteRevision_CaptureGuard] ON [QuoteRevision] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i JOIN Quote q ON q.Id=i.QuoteId JOIN Product p ON p.Id=i.ProductId WHERE q.State<>'draft' OR q.CaptureClosedAt IS NOT NULL OR COALESCE(JSON_VALUE(i.ProposalJson,'$.productCode'),'')<>p.Code COLLATE Latin1_General_100_BIN2) THROW 51063, 'Quote capture is closed or product identity is invalid.', 1; END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_QuoteRevision_CaptureGuard]; DROP TRIGGER [TR_QuoteRevision_AppendOnly]; DROP TRIGGER [TR_QuoteActivity_AppendOnly]; DROP TRIGGER [TR_Quote_NumberImmutable];");
            migrationBuilder.DropForeignKey(
                name: "FK_Quote_QuoteRevision_ClonedFromQuoteRevisionId",
                table: "Quote");

            migrationBuilder.DropForeignKey(
                name: "FK_Quote_QuoteRevision_CurrentRevisionId_Id",
                table: "Quote");

            migrationBuilder.DropTable(
                name: "QuoteActivity");

            migrationBuilder.DropTable(
                name: "QuoteRegistration");

            migrationBuilder.DropTable(
                name: "QuoteRevision");

            migrationBuilder.DropTable(
                name: "Quote");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProductVersion_Id_ProductId",
                table: "ProductVersion");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AgencyTermsVersion_Id_AgencyId",
                table: "AgencyTermsVersion");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AgencyProduct_AgencyTermsVersionId_ProductVersionId",
                table: "AgencyProduct");

            migrationBuilder.DropSequence(
                name: "QuoteReferenceSequence");
        }
    }
}
