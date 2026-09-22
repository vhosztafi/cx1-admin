using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgencyResponseTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "AgencyResponseReferenceSequence");

            migrationBuilder.CreateTable(
                name: "AgencyResponseRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Instruction = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolutionReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyResponseRequest", x => x.Id);
                    table.CheckConstraint("CK_AgencyResponseRequest_Content", "[CreatedBy] IS NOT NULL AND LEN(TRIM([Reason]))>=10 AND LEN(TRIM([Instruction]))>0 AND LEN(TRIM([Subject]))>0");
                    table.CheckConstraint("CK_AgencyResponseRequest_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyResponseRequest_Resolution", "([State]='awaiting-response' AND [ResolvedAt] IS NULL AND [ResolvedBy] IS NULL AND [ResolutionReason] IS NULL) OR ([State] IN ('response-received','withdrawn') AND [ResolvedAt] IS NOT NULL AND [ResolvedBy] IS NOT NULL AND [ResolutionReason] IS NOT NULL AND LEN(TRIM([ResolutionReason]))>=10)");
                    table.CheckConstraint("CK_AgencyResponseRequest_ResolvedAt_Utc", "DATEPART(TZOFFSET,[ResolvedAt]) = 0");
                    table.CheckConstraint("CK_AgencyResponseRequest_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_AgencyResponseRequest_ClientAgencyRelationship_RelationshipId",
                        column: x => x.RelationshipId,
                        principalTable: "ClientAgencyRelationship",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyResponseRequest_OperationalMessageVersion_MessageVersionId",
                        column: x => x.MessageVersionId,
                        principalTable: "OperationalMessageVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyResponseRequest_OperationalSubject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "OperationalSubject",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyResponseRequest_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyResponseRequest_CreatedBy",
                table: "AgencyResponseRequest",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyResponseRequest_MessageVersionId",
                table: "AgencyResponseRequest",
                column: "MessageVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgencyResponseRequest_Reference",
                table: "AgencyResponseRequest",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgencyResponseRequest_RelationshipId_State_CreatedAt_Id",
                table: "AgencyResponseRequest",
                columns: new[] { "RelationshipId", "State", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyResponseRequest_SubjectId",
                table: "AgencyResponseRequest",
                column: "SubjectId");
            AddResponseGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM AgencyResponseRequest) THROW 52000,'Retained agency response requests prevent downgrade.',1;");
            migrationBuilder.DropTable(
                name: "AgencyResponseRequest");

            migrationBuilder.DropSequence(
                name: "AgencyResponseReferenceSequence");
        }
    }
}
