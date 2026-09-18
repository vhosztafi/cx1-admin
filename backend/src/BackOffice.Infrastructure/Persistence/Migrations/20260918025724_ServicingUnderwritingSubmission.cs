using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingUnderwritingSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServicingUnderwritingSubmission",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingUnderwritingSubmission", x => x.Id);
                    table.CheckConstraint("CK_ServicingUnderwritingSubmission_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[SubmittedBy]");
                    table.CheckConstraint("CK_ServicingUnderwritingSubmission_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingUnderwritingSubmission_Identity", "[Id]<>'00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ServicingUnderwritingSubmission_Reason", "LEN(TRIM([Reason]))>=10");
                    table.CheckConstraint("CK_ServicingUnderwritingSubmission_Recorded", "[SubmittedAt]=[CreatedAt]");
                    table.CheckConstraint("CK_ServicingUnderwritingSubmission_SubmittedAt_Utc", "DATEPART(TZOFFSET,[SubmittedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ServicingUnderwritingSubmission_ServicingCycle_CycleId_DraftId_RevisionId",
                        columns: x => new { x.CycleId, x.DraftId, x.RevisionId },
                        principalTable: "ServicingCycle",
                        principalColumns: new[] { "Id", "DraftId", "RevisionId" });
                    table.ForeignKey(
                        name: "FK_ServicingUnderwritingSubmission_ServicingRatingResult_RatingId_CycleId_DraftId_RevisionId",
                        columns: x => new { x.RatingId, x.CycleId, x.DraftId, x.RevisionId },
                        principalTable: "ServicingRatingResult",
                        principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId" });
                    table.ForeignKey(
                        name: "FK_ServicingUnderwritingSubmission_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingUnderwritingSubmission_User_SubmittedBy",
                        column: x => x.SubmittedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingUnderwritingSubmission_CreatedBy",
                table: "ServicingUnderwritingSubmission",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingUnderwritingSubmission_CycleId",
                table: "ServicingUnderwritingSubmission",
                column: "CycleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingUnderwritingSubmission_CycleId_DraftId_RevisionId",
                table: "ServicingUnderwritingSubmission",
                columns: new[] { "CycleId", "DraftId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingUnderwritingSubmission_DraftId_SubmittedAt_Id",
                table: "ServicingUnderwritingSubmission",
                columns: new[] { "DraftId", "SubmittedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingUnderwritingSubmission_RatingId_CycleId_DraftId_RevisionId",
                table: "ServicingUnderwritingSubmission",
                columns: new[] { "RatingId", "CycleId", "DraftId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingUnderwritingSubmission_SubmittedBy",
                table: "ServicingUnderwritingSubmission",
                column: "SubmittedBy");
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_ServicingUnderwritingSubmission_Current ON ServicingUnderwritingSubmission AFTER INSERT AS BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(
                    SELECT 1 FROM ServicingDraft d WITH(UPDLOCK,HOLDLOCK)
                    JOIN ServicingCycle c WITH(UPDLOCK,HOLDLOCK) ON c.Id=i.CycleId AND c.DraftId=d.Id
                    JOIN ServicingRatingResult r ON r.Id=i.RatingId AND r.CycleId=c.Id
                    WHERE d.Id=i.DraftId AND d.State='draft' AND d.CurrentCycleId=c.Id
                      AND d.CurrentRevisionId=i.RevisionId AND c.RevisionId=i.RevisionId
                      AND c.State='rated' AND c.CurrentRatingId=r.Id
                      AND c.InputHash=i.InputHash AND r.InputHash=i.InputHash AND r.Outcome='rated'
                      AND i.SubmittedAt>=r.CompletedAt AND i.SubmittedAt<r.ExpiresAt))
                    THROW 51410,'Submission requires the current owned revision and unexpired successful rating.',1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_ServicingUnderwritingSubmission_Immutable ON ServicingUnderwritingSubmission AFTER UPDATE,DELETE AS BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted)
                    THROW 51411,'Underwriting submission history is immutable.',1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ServicingUnderwritingSubmission_Current; DROP TRIGGER IF EXISTS TR_ServicingUnderwritingSubmission_Immutable;");
            migrationBuilder.DropTable(
                name: "ServicingUnderwritingSubmission");
        }
    }
}
