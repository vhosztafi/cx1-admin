using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LegacyOperationalLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchCorrespondence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InformationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchCorrespondence", x => x.Id);
                    table.CheckConstraint("CK_MatchCorrespondence_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_MatchCorrespondence_Creator", "[CreatedBy] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_MatchCorrespondence_MatchInformationRequest_InformationRequestId",
                        column: x => x.InformationRequestId,
                        principalTable: "MatchInformationRequest",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchCorrespondence_OperationalMessageDraft_MessageId",
                        column: x => x.MessageId,
                        principalTable: "OperationalMessageDraft",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchCorrespondence_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchCorrespondence_CreatedBy",
                table: "MatchCorrespondence",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_MatchCorrespondence_InformationRequestId",
                table: "MatchCorrespondence",
                column: "InformationRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchCorrespondence_MessageId",
                table: "MatchCorrespondence",
                column: "MessageId",
                unique: true);
            AddLegacyGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM MatchCorrespondence) THROW 52042,'Retained correspondence prevents downgrade.',1;");
            migrationBuilder.DropTable(
                name: "MatchCorrespondence");
        }
    }
}
