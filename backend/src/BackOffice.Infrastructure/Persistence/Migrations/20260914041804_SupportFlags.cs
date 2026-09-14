using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportFlag",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginRelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TypeCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    InternalCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InternalInstruction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AgencyInstruction = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConsentBasis = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReviewOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportFlag", x => x.Id);
                    table.UniqueConstraint("AK_SupportFlag_Id_ClientId", x => new { x.Id, x.ClientId });
                    table.CheckConstraint("CK_SupportFlag_Category", "[InternalCategory] IN ('health','life-event','resilience','capability','authority')");
                    table.CheckConstraint("CK_SupportFlag_ConsentBasis", "[ConsentBasis] IN ('verbal-consent','written-consent','third-party-authority')");
                    table.CheckConstraint("CK_SupportFlag_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_SupportFlag_EndedAt_Utc", "DATEPART(TZOFFSET,[EndedAt]) = 0");
                    table.CheckConstraint("CK_SupportFlag_Ending", "([EndedAt] IS NULL AND [EndedBy] IS NULL) OR ([EndedAt] IS NOT NULL AND [EndedAt]>=[CreatedAt] AND [EndedBy] IS NOT NULL)");
                    table.CheckConstraint("CK_SupportFlag_ReviewOn", "[ReviewOn] > CONVERT(date,'00010101',112)");
                    table.CheckConstraint("CK_SupportFlag_Text", "LEN(TRIM([InternalInstruction]))>0 AND LEN(TRIM([Reason]))>0 AND ([AgencyInstruction] IS NULL OR LEN(TRIM([AgencyInstruction]))>0)");
                    table.CheckConstraint("CK_SupportFlag_TypeCode", "[TypeCode] IN ('vulnerability','third-party-authority','financial-difficulty','accessible-format','interpreter-required','deceased-or-business-ceased')");
                    table.CheckConstraint("CK_SupportFlag_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_SupportFlag_ClientAgencyRelationship_OriginRelationshipId_ClientId",
                        columns: x => new { x.OriginRelationshipId, x.ClientId },
                        principalTable: "ClientAgencyRelationship",
                        principalColumns: new[] { "Id", "ClientId" });
                    table.ForeignKey(
                        name: "FK_SupportFlag_Person_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Person",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupportFlag_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupportFlag_User_EndedBy",
                        column: x => x.EndedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FlagVisibility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlagVisibility", x => x.Id);
                    table.CheckConstraint("CK_FlagVisibility_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_FlagVisibility_ClientAgencyRelationship_RelationshipId_ClientId",
                        columns: x => new { x.RelationshipId, x.ClientId },
                        principalTable: "ClientAgencyRelationship",
                        principalColumns: new[] { "Id", "ClientId" });
                    table.ForeignKey(
                        name: "FK_FlagVisibility_SupportFlag_FlagId_ClientId",
                        columns: x => new { x.FlagId, x.ClientId },
                        principalTable: "SupportFlag",
                        principalColumns: new[] { "Id", "ClientId" });
                    table.ForeignKey(
                        name: "FK_FlagVisibility_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SupportFlagHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Snapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportFlagHistory", x => x.Id);
                    table.CheckConstraint("CK_SupportFlagHistory_Action", "[Action] IN ('created','amended','reviewed','ended')");
                    table.CheckConstraint("CK_SupportFlagHistory_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_SupportFlagHistory_OccurredAt_Utc", "DATEPART(TZOFFSET,[OccurredAt]) = 0");
                    table.CheckConstraint("CK_SupportFlagHistory_Reason", "LEN(TRIM([Reason]))>0");
                    table.CheckConstraint("CK_SupportFlagHistory_Snapshot_Json", "ISJSON([Snapshot]) = 1");
                    table.ForeignKey(
                        name: "FK_SupportFlagHistory_SupportFlag_FlagId",
                        column: x => x.FlagId,
                        principalTable: "SupportFlag",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupportFlagHistory_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupportFlagHistory_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlagVisibility_CreatedBy",
                table: "FlagVisibility",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FlagVisibility_FlagId_ClientId",
                table: "FlagVisibility",
                columns: new[] { "FlagId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_FlagVisibility_FlagId_RelationshipId",
                table: "FlagVisibility",
                columns: new[] { "FlagId", "RelationshipId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlagVisibility_RelationshipId_ClientId",
                table: "FlagVisibility",
                columns: new[] { "RelationshipId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_FlagVisibility_RelationshipId_FlagId",
                table: "FlagVisibility",
                columns: new[] { "RelationshipId", "FlagId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportFlag_CreatedBy",
                table: "SupportFlag",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SupportFlag_EndedBy",
                table: "SupportFlag",
                column: "EndedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SupportFlag_OriginRelationshipId_ClientId",
                table: "SupportFlag",
                columns: new[] { "OriginRelationshipId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportFlag_OriginRelationshipId_EndedAt_Id",
                table: "SupportFlag",
                columns: new[] { "OriginRelationshipId", "EndedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportFlag_PersonId_ReviewOn",
                table: "SupportFlag",
                columns: new[] { "PersonId", "ReviewOn" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportFlagHistory_ActorId",
                table: "SupportFlagHistory",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportFlagHistory_CreatedBy",
                table: "SupportFlagHistory",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SupportFlagHistory_FlagId_OccurredAt_Id",
                table: "SupportFlagHistory",
                columns: new[] { "FlagId", "OccurredAt", "Id" });
            migrationBuilder.Sql("CREATE TRIGGER [TR_SupportFlagHistory_AppendOnly] ON [SupportFlagHistory] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51005, 'Support flag history is append-only.', 1; END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_SupportFlagHistory_AppendOnly];");
            migrationBuilder.DropTable(
                name: "FlagVisibility");

            migrationBuilder.DropTable(
                name: "SupportFlagHistory");

            migrationBuilder.DropTable(
                name: "SupportFlag");
        }
    }
}
