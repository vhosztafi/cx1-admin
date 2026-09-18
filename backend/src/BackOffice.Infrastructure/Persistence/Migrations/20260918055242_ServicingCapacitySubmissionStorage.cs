using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingCapacitySubmissionStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentSubmissionId",
                table: "ServicingCapacityCase",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServicingCapacitySubmission",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ContextJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContextHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResponseDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingCapacitySubmission", x => x.Id);
                    table.UniqueConstraint("AK_ServicingCapacitySubmission_Id_CaseId_CycleId_DraftId_RevisionId_RatingId", x => new { x.Id, x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
                    table.CheckConstraint("CK_ServicingCapacitySubmission_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[SubmittedBy]");
                    table.CheckConstraint("CK_ServicingCapacitySubmission_ContextHash", "[ContextHash]=HASHBYTES('SHA2_256',CONVERT(varchar(max),[ContextJson] COLLATE Latin1_General_100_BIN2_UTF8))");
                    table.CheckConstraint("CK_ServicingCapacitySubmission_ContextJson", "ISJSON([ContextJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[ContextJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_ServicingCapacitySubmission_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingCapacitySubmission_Identity", "[Id]<>'00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ServicingCapacitySubmission_ResponseDueAt_Utc", "DATEPART(TZOFFSET,[ResponseDueAt]) = 0");
                    table.CheckConstraint("CK_ServicingCapacitySubmission_Sequence", "[Sequence]>0");
                    table.CheckConstraint("CK_ServicingCapacitySubmission_SubmittedAt_Utc", "DATEPART(TZOFFSET,[SubmittedAt]) = 0");
                    table.CheckConstraint("CK_ServicingCapacitySubmission_Text", "LEN(TRIM([Body]))>0 AND DATALENGTH([Body])<=20000 AND LEN(TRIM([Reason]))>=10");
                    table.CheckConstraint("CK_ServicingCapacitySubmission_Time", "[SubmittedAt]=[CreatedAt] AND [ResponseDueAt]>[SubmittedAt]");
                    table.ForeignKey(
                        name: "FK_ServicingCapacitySubmission_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCapacitySubmission_ServicingCapacityCase_CaseId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingCapacityCase",
                        principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingCapacitySubmission_SettingVersion_ScenarioVersionId",
                        column: x => x.ScenarioVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCapacitySubmission_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCapacitySubmission_User_SubmittedBy",
                        column: x => x.SubmittedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityCase_CurrentSubmissionId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacityCase",
                columns: new[] { "CurrentSubmissionId", "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacitySubmission_CaseId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacitySubmission",
                columns: new[] { "CaseId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacitySubmission_CaseId_Sequence",
                table: "ServicingCapacitySubmission",
                columns: new[] { "CaseId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacitySubmission_CreatedBy",
                table: "ServicingCapacitySubmission",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacitySubmission_ScenarioVersionId",
                table: "ServicingCapacitySubmission",
                column: "ScenarioVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacitySubmission_SubmittedBy",
                table: "ServicingCapacitySubmission",
                column: "SubmittedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacitySubmission_WorkId",
                table: "ServicingCapacitySubmission",
                column: "WorkId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingCapacityCase_ServicingCapacitySubmission_CurrentSubmissionId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacityCase",
                columns: new[] { "CurrentSubmissionId", "Id", "CycleId", "DraftId", "RevisionId", "RatingId" },
                principalTable: "ServicingCapacitySubmission",
                principalColumns: new[] { "Id", "CaseId", "CycleId", "DraftId", "RevisionId", "RatingId" });
            AddSubmissionGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveSubmissionGuards(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingCapacityCase_ServicingCapacitySubmission_CurrentSubmissionId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacityCase");

            migrationBuilder.DropTable(
                name: "ServicingCapacitySubmission");

            migrationBuilder.DropIndex(
                name: "IX_ServicingCapacityCase_CurrentSubmissionId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacityCase");

            migrationBuilder.DropColumn(
                name: "CurrentSubmissionId",
                table: "ServicingCapacityCase");
            RestoreCaseGuard(migrationBuilder);
        }
    }
}
