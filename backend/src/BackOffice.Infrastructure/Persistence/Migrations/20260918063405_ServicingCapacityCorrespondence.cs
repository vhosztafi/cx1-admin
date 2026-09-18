using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingCapacityCorrespondence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServicingCapacityMessage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingCapacityMessage", x => x.Id);
                    table.CheckConstraint("CK_ServicingCapacityMessage_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RecordedBy] AND [RecordedAt]=[CreatedAt]");
                    table.CheckConstraint("CK_ServicingCapacityMessage_Body", "LEN(TRIM([Body]))>0 AND DATALENGTH([Body])<=20000");
                    table.CheckConstraint("CK_ServicingCapacityMessage_ContentHash", "[ContentHash]=HASHBYTES('SHA2_256',CONVERT(varchar(max),[Body] COLLATE Latin1_General_100_BIN2_UTF8))");
                    table.CheckConstraint("CK_ServicingCapacityMessage_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingCapacityMessage_Kind", "[Kind] IN ('submission','chase','query-reply')");
                    table.CheckConstraint("CK_ServicingCapacityMessage_RecordedAt_Utc", "DATEPART(TZOFFSET,[RecordedAt]) = 0");
                    table.CheckConstraint("CK_ServicingCapacityMessage_Sequence", "[Sequence]>0");
                    table.ForeignKey(
                        name: "FK_ServicingCapacityMessage_ServicingCapacitySubmission_SubmissionId_CaseId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.SubmissionId, x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingCapacitySubmission",
                        principalColumns: new[] { "Id", "CaseId", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingCapacityMessage_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCapacityMessage_User_RecordedBy",
                        column: x => x.RecordedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityMessage_CaseId_Sequence",
                table: "ServicingCapacityMessage",
                columns: new[] { "CaseId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityMessage_CreatedBy",
                table: "ServicingCapacityMessage",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityMessage_RecordedBy",
                table: "ServicingCapacityMessage",
                column: "RecordedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityMessage_SubmissionId",
                table: "ServicingCapacityMessage",
                column: "SubmissionId",
                unique: true,
                filter: "[Kind]='submission'");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityMessage_SubmissionId_CaseId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacityMessage",
                columns: new[] { "SubmissionId", "CaseId", "CycleId", "DraftId", "RevisionId", "RatingId" });
            AddMessageGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ServicingCapacityMessage_Source; DROP TRIGGER IF EXISTS TR_ServicingCapacityMessage_Immutable;");
            migrationBuilder.DropTable(
                name: "ServicingCapacityMessage");
        }
    }
}
