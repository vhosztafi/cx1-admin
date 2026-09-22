using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalMid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyMidIntent_Purpose",
                table: "PolicyMidIntent");

            migrationBuilder.CreateTable(
                name: "MidSubmission",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyMidIntentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancellationConsequenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MidSubmission", x => x.Id);
                    table.CheckConstraint("CK_MidSubmission_Content", "ISJSON([RequestJson])=1 AND LEN([RequestHash])=64 AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_MidSubmission_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_MidSubmission_Source", "([PolicyMidIntentId] IS NOT NULL AND [CancellationConsequenceId] IS NULL) OR ([PolicyMidIntentId] IS NULL AND [CancellationConsequenceId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_MidSubmission_CancellationConsequence_CancellationConsequenceId",
                        column: x => x.CancellationConsequenceId,
                        principalTable: "CancellationConsequence",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MidSubmission_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MidSubmission_PolicyMidIntent_PolicyMidIntentId",
                        column: x => x.PolicyMidIntentId,
                        principalTable: "PolicyMidIntent",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MidSubmission_PolicyVersion_BaseVersionId",
                        column: x => x.BaseVersionId,
                        principalTable: "PolicyVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MidSubmission_PolicyVersion_VersionId_TransactionId_TermId_PolicyId",
                        columns: x => new { x.VersionId, x.TransactionId, x.TermId, x.PolicyId },
                        principalTable: "PolicyVersion",
                        principalColumns: new[] { "Id", "TransactionId", "TermId", "PolicyId" });
                    table.ForeignKey(
                        name: "FK_MidSubmission_SettingVersion_ScenarioVersionId",
                        column: x => x.ScenarioVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MidSubmission_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MidResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MidResult", x => x.Id);
                    table.CheckConstraint("CK_MidResult_Content", "ISJSON([ResultJson])=1 AND LEN([ContentHash])=64");
                    table.CheckConstraint("CK_MidResult_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_MidResult_DemoProviderOperation_ProviderOperationId",
                        column: x => x.ProviderOperationId,
                        principalTable: "DemoProviderOperation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MidResult_MidSubmission_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "MidSubmission",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MidResult_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyMidIntent_Purpose",
                table: "PolicyMidIntent",
                sql: "[Purpose] IN ('new-business','adjustment','renewal')");

            migrationBuilder.CreateIndex(
                name: "IX_MidResult_CreatedBy",
                table: "MidResult",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_MidResult_ProviderEventId",
                table: "MidResult",
                column: "ProviderEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MidResult_ProviderOperationId",
                table: "MidResult",
                column: "ProviderOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_MidResult_SubmissionId",
                table: "MidResult",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MidSubmission_BaseVersionId",
                table: "MidSubmission",
                column: "BaseVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_MidSubmission_CancellationConsequenceId",
                table: "MidSubmission",
                column: "CancellationConsequenceId",
                unique: true,
                filter: "[CancellationConsequenceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MidSubmission_CreatedBy",
                table: "MidSubmission",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_MidSubmission_PolicyMidIntentId",
                table: "MidSubmission",
                column: "PolicyMidIntentId",
                unique: true,
                filter: "[PolicyMidIntentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MidSubmission_ScenarioVersionId",
                table: "MidSubmission",
                column: "ScenarioVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_MidSubmission_VersionId",
                table: "MidSubmission",
                column: "VersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MidSubmission_VersionId_TransactionId_TermId_PolicyId",
                table: "MidSubmission",
                columns: new[] { "VersionId", "TransactionId", "TermId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_MidSubmission_WorkId",
                table: "MidSubmission",
                column: "WorkId",
                unique: true);
            AddMidGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveMidGuards(migrationBuilder);
            migrationBuilder.DropTable(
                name: "MidResult");

            migrationBuilder.DropTable(
                name: "MidSubmission");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyMidIntent_Purpose",
                table: "PolicyMidIntent");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyMidIntent_Purpose",
                table: "PolicyMidIntent",
                sql: "[Purpose] IN ('adjustment','renewal')");
        }
    }
}
