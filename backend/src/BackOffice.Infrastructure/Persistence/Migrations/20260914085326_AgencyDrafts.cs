using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgencyDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Agency_Identity",
                table: "Agency");

            migrationBuilder.CreateSequence(
                name: "AgencyReferenceSequence");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Agency",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "OnboardingStep",
                table: "Agency",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "RegulatoryReference",
                table: "Agency",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelationshipManagerId",
                table: "Agency",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgencyActivity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyActivity", x => x.Id);
                    table.CheckConstraint("CK_AgencyActivity_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyActivity_OccurredAt_Utc", "DATEPART(TZOFFSET,[OccurredAt]) = 0");
                    table.ForeignKey(
                        name: "FK_AgencyActivity_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyActivity_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyActivity_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AgencyDraftProduct",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    BrokerCommissionBasisPoints = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyDraftProduct", x => x.Id);
                    table.CheckConstraint("CK_AgencyDraftProduct_Commission", "[BrokerCommissionBasisPoints] BETWEEN 0 AND 10000");
                    table.CheckConstraint("CK_AgencyDraftProduct_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_AgencyDraftProduct_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyDraftProduct_ProductVersion_ProductVersionId",
                        column: x => x.ProductVersionId,
                        principalTable: "ProductVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyDraftProduct_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AgencyOnboarding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyOnboarding", x => x.Id);
                    table.CheckConstraint("CK_AgencyOnboarding_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyOnboarding_Details_Json", "ISJSON([Details]) = 1");
                    table.CheckConstraint("CK_AgencyOnboarding_DetailsBounds", "DATALENGTH([Details]) <= 131072 AND LEFT(LTRIM([Details]),1) = '{'");
                    table.CheckConstraint("CK_AgencyOnboarding_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_AgencyOnboarding_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyOnboarding_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Agency_RelationshipManagerId_Id",
                table: "Agency",
                columns: new[] { "RelationshipManagerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Agency_State_NormalizedName_Id",
                table: "Agency",
                columns: new[] { "State", "NormalizedName", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Agency_Identity",
                table: "Agency",
                sql: "LEN(TRIM([Reference])) > 0 AND ([State] IN ('draft','abandoned') OR LEN(TRIM([LegalName])) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Agency_OnboardingStep",
                table: "Agency",
                sql: "[OnboardingStep] BETWEEN 1 AND 6");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyActivity_ActorId",
                table: "AgencyActivity",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyActivity_AgencyId_OccurredAt_Id",
                table: "AgencyActivity",
                columns: new[] { "AgencyId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyActivity_CreatedBy",
                table: "AgencyActivity",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyDraftProduct_AgencyId_ProductVersionId",
                table: "AgencyDraftProduct",
                columns: new[] { "AgencyId", "ProductVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgencyDraftProduct_CreatedBy",
                table: "AgencyDraftProduct",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyDraftProduct_ProductVersionId",
                table: "AgencyDraftProduct",
                column: "ProductVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyOnboarding_AgencyId",
                table: "AgencyOnboarding",
                column: "AgencyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgencyOnboarding_CreatedBy",
                table: "AgencyOnboarding",
                column: "CreatedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_Agency_User_RelationshipManagerId",
                table: "Agency",
                column: "RelationshipManagerId",
                principalTable: "User",
                principalColumn: "Id");
            migrationBuilder.Sql("UPDATE [Agency] SET [NormalizedName]=UPPER(TRIM([LegalName]));");
            migrationBuilder.Sql("INSERT INTO [AgencyOnboarding] ([Id],[AgencyId],[SchemaVersion],[Details],[CreatedAt],[UpdatedAt]) SELECT NEWID(),a.[Id],N'1.0',(SELECT a.[LegalName] AS legalName FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),SYSUTCDATETIME(),SYSUTCDATETIME() FROM [Agency] a;");
            migrationBuilder.Sql("CREATE TRIGGER [TR_AgencyActivity_Immutable] ON [AgencyActivity] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; THROW 51020, 'Agency activity is append-only.', 1; END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agency_User_RelationshipManagerId",
                table: "Agency");

            migrationBuilder.DropTable(
                name: "AgencyActivity");

            migrationBuilder.DropTable(
                name: "AgencyDraftProduct");

            migrationBuilder.DropTable(
                name: "AgencyOnboarding");

            migrationBuilder.DropIndex(
                name: "IX_Agency_RelationshipManagerId_Id",
                table: "Agency");

            migrationBuilder.DropIndex(
                name: "IX_Agency_State_NormalizedName_Id",
                table: "Agency");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Agency_Identity",
                table: "Agency");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Agency_OnboardingStep",
                table: "Agency");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Agency");

            migrationBuilder.DropColumn(
                name: "OnboardingStep",
                table: "Agency");

            migrationBuilder.DropColumn(
                name: "RegulatoryReference",
                table: "Agency");

            migrationBuilder.DropColumn(
                name: "RelationshipManagerId",
                table: "Agency");

            migrationBuilder.DropSequence(
                name: "AgencyReferenceSequence");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Agency_Identity",
                table: "Agency",
                sql: "LEN(TRIM([Reference])) > 0 AND LEN(TRIM([LegalName])) > 0");
        }
    }
}
