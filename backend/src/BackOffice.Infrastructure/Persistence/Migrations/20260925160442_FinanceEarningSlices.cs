using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceEarningSlices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_IssueFinancialComponent_Id_TransactionId",
                table: "IssueFinancialComponent",
                columns: new[] { "Id", "TransactionId" });

            migrationBuilder.CreateTable(
                name: "FinanceEarningSlice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObligationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PremiumPence = table.Column<long>(type: "bigint", nullable: false),
                    CoverageStartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CoverageEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MonthStart = table.Column<DateOnly>(type: "date", nullable: false),
                    EarnedPence = table.Column<long>(type: "bigint", nullable: false),
                    AlgorithmVersion = table.Column<int>(type: "int", nullable: false),
                    SourceHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    SourcePostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceEarningSlice", x => x.Id);
                    table.CheckConstraint("CK_FinanceEarningSlice_CoverageEndsAt_Utc", "DATEPART(TZOFFSET,[CoverageEndsAt]) = 0");
                    table.CheckConstraint("CK_FinanceEarningSlice_CoverageStartsAt_Utc", "DATEPART(TZOFFSET,[CoverageStartsAt]) = 0");
                    table.CheckConstraint("CK_FinanceEarningSlice_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_FinanceEarningSlice_Interval", "[CoverageStartsAt]<[CoverageEndsAt]");
                    table.CheckConstraint("CK_FinanceEarningSlice_Month", "DAY([MonthStart])=1");
                    table.CheckConstraint("CK_FinanceEarningSlice_SourcePostedAt_Utc", "DATEPART(TZOFFSET,[SourcePostedAt]) = 0");
                    table.CheckConstraint("CK_FinanceEarningSlice_Version", "[AlgorithmVersion]=1");
                    table.ForeignKey(
                        name: "FK_FinanceEarningSlice_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceEarningSlice_IssueFinancialComponent_SourceComponentId_TransactionId",
                        columns: x => new { x.SourceComponentId, x.TransactionId },
                        principalTable: "IssueFinancialComponent",
                        principalColumns: new[] { "Id", "TransactionId" });
                    table.ForeignKey(
                        name: "FK_FinanceEarningSlice_IssueFinancialObligation_ObligationId_TransactionId",
                        columns: x => new { x.ObligationId, x.TransactionId },
                        principalTable: "IssueFinancialObligation",
                        principalColumns: new[] { "Id", "TransactionId" });
                    table.ForeignKey(
                        name: "FK_FinanceEarningSlice_Policy_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policy",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceEarningSlice_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceEarningSlice_AgencyId_MonthStart_SourcePostedAt",
                table: "FinanceEarningSlice",
                columns: new[] { "AgencyId", "MonthStart", "SourcePostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceEarningSlice_CreatedBy",
                table: "FinanceEarningSlice",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceEarningSlice_ObligationId_TransactionId",
                table: "FinanceEarningSlice",
                columns: new[] { "ObligationId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceEarningSlice_PolicyId",
                table: "FinanceEarningSlice",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceEarningSlice_SourceComponentId_MonthStart",
                table: "FinanceEarningSlice",
                columns: new[] { "SourceComponentId", "MonthStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceEarningSlice_SourceComponentId_TransactionId",
                table: "FinanceEarningSlice",
                columns: new[] { "SourceComponentId", "TransactionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanceEarningSlice");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_IssueFinancialComponent_Id_TransactionId",
                table: "IssueFinancialComponent");
        }
    }
}
