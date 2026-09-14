using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelationshipContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Person",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Surname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Person", x => x.Id);
                    table.CheckConstraint("CK_Person_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Person_Name", "LEN(TRIM([FullName])) > 0");
                    table.CheckConstraint("CK_Person_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_Person_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Contact",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeclaredFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeclaredFirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeclaredSurname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    Telephone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    MarketingConsent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contact", x => x.Id);
                    table.CheckConstraint("CK_Contact_Consent", "COALESCE(JSON_VALUE([MarketingConsent],'$.state'),'') IN ('given','withheld','not-asked') AND COALESCE(JSON_VALUE([MarketingConsent],'$.email'),'') IN ('true','false') AND COALESCE(JSON_VALUE([MarketingConsent],'$.telephone'),'') IN ('true','false') AND COALESCE(LEN(TRIM(JSON_VALUE([MarketingConsent],'$.source'))),0) > 0 AND TRY_CONVERT(datetimeoffset,JSON_VALUE([MarketingConsent],'$.recordedAt'),127) IS NOT NULL AND DATEPART(TZOFFSET,TRY_CONVERT(datetimeoffset,JSON_VALUE([MarketingConsent],'$.recordedAt'),127))=0 AND ((JSON_VALUE([MarketingConsent],'$.state')='given' AND (JSON_VALUE([MarketingConsent],'$.email')='true' OR JSON_VALUE([MarketingConsent],'$.telephone')='true')) OR (JSON_VALUE([MarketingConsent],'$.state') IN ('withheld','not-asked') AND JSON_VALUE([MarketingConsent],'$.email')='false' AND JSON_VALUE([MarketingConsent],'$.telephone')='false'))");
                    table.CheckConstraint("CK_Contact_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Contact_EndedAt_Utc", "DATEPART(TZOFFSET,[EndedAt]) = 0");
                    table.CheckConstraint("CK_Contact_Ending", "([EndedAt] IS NULL AND [EndedBy] IS NULL AND [EndReason] IS NULL) OR ([EndedAt] IS NOT NULL AND [EndedAt] >= [CreatedAt] AND [EndedBy] IS NOT NULL AND [EndReason] IS NOT NULL AND LEN(TRIM([EndReason])) > 0 AND [IsPrimary] = 0)");
                    table.CheckConstraint("CK_Contact_Identity", "LEN(TRIM([DeclaredFullName])) > 0 AND LEN(TRIM([NormalizedName])) > 0 AND LEN(TRIM([Role])) > 0");
                    table.CheckConstraint("CK_Contact_MarketingConsent_Json", "ISJSON([MarketingConsent]) = 1");
                    table.CheckConstraint("CK_Contact_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_Contact_ClientAgencyRelationship_RelationshipId_ClientId",
                        columns: x => new { x.RelationshipId, x.ClientId },
                        principalTable: "ClientAgencyRelationship",
                        principalColumns: new[] { "Id", "ClientId" });
                    table.ForeignKey(
                        name: "FK_Contact_Person_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Person",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Contact_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Contact_User_EndedBy",
                        column: x => x.EndedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contact_CreatedBy",
                table: "Contact",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Contact_EndedBy",
                table: "Contact",
                column: "EndedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Contact_NormalizedName",
                table: "Contact",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Contact_PersonId_ClientId",
                table: "Contact",
                columns: new[] { "PersonId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contact_RelationshipId",
                table: "Contact",
                column: "RelationshipId",
                unique: true,
                filter: "[IsPrimary] = 1 AND [EndedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Contact_RelationshipId_ClientId",
                table: "Contact",
                columns: new[] { "RelationshipId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contact_RelationshipId_EndedAt_Id",
                table: "Contact",
                columns: new[] { "RelationshipId", "EndedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Person_CreatedBy",
                table: "Person",
                column: "CreatedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Contact");

            migrationBuilder.DropTable(
                name: "Person");
        }
    }
}
