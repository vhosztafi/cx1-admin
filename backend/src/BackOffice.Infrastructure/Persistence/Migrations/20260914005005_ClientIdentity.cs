using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ClientReferenceSequence");

            migrationBuilder.CreateTable(
                name: "Agency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agency", x => x.Id);
                    table.CheckConstraint("CK_Agency_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Agency_Identity", "LEN(TRIM([Reference])) > 0 AND LEN(TRIM([LegalName])) > 0");
                    table.CheckConstraint("CK_Agency_State", "[State] IN ('draft','active','suspended','abandoned')");
                    table.CheckConstraint("CK_Agency_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_Agency_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClientAccount",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CompanyNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdentityState = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAccount", x => x.Id);
                    table.CheckConstraint("CK_ClientAccount_Address_Json", "ISJSON([Address]) = 1");
                    table.CheckConstraint("CK_ClientAccount_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ClientAccount_EntityType", "[EntityType] IN ('sole-trader','partnership','limited-company','llp')");
                    table.CheckConstraint("CK_ClientAccount_Identity", "LEN(TRIM([Reference])) > 0 AND LEN(TRIM([LegalName])) > 0 AND LEN(TRIM([NormalizedName])) > 0");
                    table.CheckConstraint("CK_ClientAccount_State", "[IdentityState] IN ('active','inactive')");
                    table.CheckConstraint("CK_ClientAccount_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ClientAccount_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClientAgencyRelationship",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAgencyRelationship", x => x.Id);
                    table.UniqueConstraint("AK_ClientAgencyRelationship_Id_ClientId", x => new { x.Id, x.ClientId });
                    table.CheckConstraint("CK_ClientAgencyRelationship_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ClientAgencyRelationship_State", "[State] IN ('active','inactive')");
                    table.CheckConstraint("CK_ClientAgencyRelationship_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ClientAgencyRelationship_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientAgencyRelationship_ClientAccount_ClientId",
                        column: x => x.ClientId,
                        principalTable: "ClientAccount",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientAgencyRelationship_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClientActivity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientActivity", x => x.Id);
                    table.CheckConstraint("CK_ClientActivity_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ClientActivity_EventType", "LEN(TRIM([EventType])) > 0");
                    table.CheckConstraint("CK_ClientActivity_OccurredAt_Utc", "DATEPART(TZOFFSET,[OccurredAt]) = 0");
                    table.CheckConstraint("CK_ClientActivity_RecordKind", "[RecordKind] IS NULL OR [RecordKind] IN ('client','contact','match')");
                    table.CheckConstraint("CK_ClientActivity_RecordLink", "([RecordId] IS NULL AND [RecordKind] IS NULL) OR ([RecordId] IS NOT NULL AND [RecordKind] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ClientActivity_ClientAccount_ClientId",
                        column: x => x.ClientId,
                        principalTable: "ClientAccount",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientActivity_ClientAgencyRelationship_RelationshipId_ClientId",
                        columns: x => new { x.RelationshipId, x.ClientId },
                        principalTable: "ClientAgencyRelationship",
                        principalColumns: new[] { "Id", "ClientId" });
                    table.ForeignKey(
                        name: "FK_ClientActivity_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientActivity_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Agency_CreatedBy",
                table: "Agency",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Agency_Reference",
                table: "Agency",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientAccount_CompanyNumber",
                table: "ClientAccount",
                column: "CompanyNumber",
                filter: "[CompanyNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAccount_CreatedBy",
                table: "ClientAccount",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAccount_NormalizedName",
                table: "ClientAccount",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAccount_Reference",
                table: "ClientAccount",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientActivity_ActorId",
                table: "ClientActivity",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientActivity_ClientId_OccurredAt_Id",
                table: "ClientActivity",
                columns: new[] { "ClientId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientActivity_CreatedBy",
                table: "ClientActivity",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ClientActivity_RelationshipId_ClientId",
                table: "ClientActivity",
                columns: new[] { "RelationshipId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientAgencyRelationship_AgencyId",
                table: "ClientAgencyRelationship",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAgencyRelationship_ClientId_AgencyId",
                table: "ClientAgencyRelationship",
                columns: new[] { "ClientId", "AgencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientAgencyRelationship_CreatedBy",
                table: "ClientAgencyRelationship",
                column: "CreatedBy");

            migrationBuilder.Sql("CREATE TRIGGER [TR_ClientActivity_AppendOnly] ON [ClientActivity] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51004, 'Client activity is append-only.', 1; END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_ClientActivity_AppendOnly];");
            migrationBuilder.DropTable(
                name: "ClientActivity");

            migrationBuilder.DropTable(
                name: "ClientAgencyRelationship");

            migrationBuilder.DropTable(
                name: "Agency");

            migrationBuilder.DropTable(
                name: "ClientAccount");

            migrationBuilder.DropSequence(
                name: "ClientReferenceSequence");
        }
    }
}
