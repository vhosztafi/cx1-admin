using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LegacyRenewalCorrespondence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RenewalLapseCorrespondence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LapseEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalLapseCorrespondence", x => x.Id);
                    table.CheckConstraint("CK_RenewalLapseCorrespondence_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_RenewalLapseCorrespondence_Creator", "[CreatedBy] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_RenewalLapseCorrespondence_OperationalMessageDraft_MessageId",
                        column: x => x.MessageId,
                        principalTable: "OperationalMessageDraft",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RenewalLapseCorrespondence_RenewalLapseEvent_LapseEventId",
                        column: x => x.LapseEventId,
                        principalTable: "RenewalLapseEvent",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RenewalLapseCorrespondence_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalLapseCorrespondence_CreatedBy",
                table: "RenewalLapseCorrespondence",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalLapseCorrespondence_LapseEventId",
                table: "RenewalLapseCorrespondence",
                column: "LapseEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalLapseCorrespondence_MessageId",
                table: "RenewalLapseCorrespondence",
                column: "MessageId",
                unique: true);
            AddLegacyRenewalGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM RenewalLapseCorrespondence) THROW 52047,'Retained lapse correspondence prevents downgrade.',1;");
            migrationBuilder.DropTable(
                name: "RenewalLapseCorrespondence");
        }
    }
}
