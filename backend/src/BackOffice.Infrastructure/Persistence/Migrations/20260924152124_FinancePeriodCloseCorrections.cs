using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancePeriodCloseCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloseChecklistJson",
                table: "AccountingPeriod",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloseReason",
                table: "AccountingPeriod",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "AccountingPeriod",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedBy",
                table: "AccountingPeriod",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SourceCutoff",
                table: "AccountingPeriod",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinanceCorrection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalSourceKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OriginalSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DebtorKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceCorrection", x => x.Id);
                    table.CheckConstraint("CK_FinanceCorrection_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_FinanceCorrection_Debtor", "[DebtorKind]='agency' OR ([DebtorKind]='relationship' AND [RelationshipId] IS NOT NULL)");
                    table.CheckConstraint("CK_FinanceCorrection_Reason", "LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
                    table.CheckConstraint("CK_FinanceCorrection_Source", "[OriginalSourceKind] IN ('insurance','finance-posting') AND [OriginalSourceId]<>'00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_FinanceCorrection_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceCorrection_ClientAgencyRelationship_RelationshipId",
                        column: x => x.RelationshipId,
                        principalTable: "ClientAgencyRelationship",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceCorrection_PolicyTransaction_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "PolicyTransaction",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceCorrection_Policy_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policy",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceCorrection_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriod_ClosedBy",
                table: "AccountingPeriod",
                column: "ClosedBy");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccountingPeriod_CloseChecklistJson_Json",
                table: "AccountingPeriod",
                sql: "[CloseChecklistJson] IS NULL OR ISJSON([CloseChecklistJson]) = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccountingPeriod_ClosedAt_Utc",
                table: "AccountingPeriod",
                sql: "DATEPART(TZOFFSET,[ClosedAt]) = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccountingPeriod_SourceCutoff_Utc",
                table: "AccountingPeriod",
                sql: "DATEPART(TZOFFSET,[SourceCutoff]) = 0");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCorrection_AgencyId",
                table: "FinanceCorrection",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCorrection_CreatedBy",
                table: "FinanceCorrection",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCorrection_OriginalSourceKind_OriginalSourceId",
                table: "FinanceCorrection",
                columns: new[] { "OriginalSourceKind", "OriginalSourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCorrection_PolicyId",
                table: "FinanceCorrection",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCorrection_RelationshipId",
                table: "FinanceCorrection",
                column: "RelationshipId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCorrection_TransactionId",
                table: "FinanceCorrection",
                column: "TransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccountingPeriod_User_ClosedBy",
                table: "AccountingPeriod",
                column: "ClosedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.Sql(FinancePeriodMigrationSql.PeriodClose);
            migrationBuilder.Sql(FinancePeriodMigrationSql.SealedJournalLine);
            migrationBuilder.Sql(FinancePeriodMigrationSql.CorrectionGuard);
            migrationBuilder.Sql(FinancePeriodMigrationSql.PostingSource);
            migrationBuilder.Sql(FinancePeriodMigrationSql.ReconciliationGuard);
            migrationBuilder.Sql(FinancePeriodMigrationSql.MatchGuard);
            migrationBuilder.Sql(FinancePeriodMigrationSql.TargetVarianceGuard);
            migrationBuilder.Sql(FinancePeriodMigrationSql.PostingReconciliationGuard);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_AccountingPeriod_FinanceClose;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_JournalLine_Sealed;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinanceCorrection_Guard;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinancePosting_CorrectionSource;");
            migrationBuilder.Sql(FinancePaymentMigrationSql.ReconciliationGuard);
            migrationBuilder.Sql(FinancePaymentMigrationSql.MatchGuard);
            migrationBuilder.Sql(FinancePaymentMigrationSql.TargetVarianceGuard);
            migrationBuilder.Sql(FinancePaymentMigrationSql.PostingReconciliationGuard);
            migrationBuilder.DropForeignKey(
                name: "FK_AccountingPeriod_User_ClosedBy",
                table: "AccountingPeriod");

            migrationBuilder.DropTable(
                name: "FinanceCorrection");

            migrationBuilder.DropIndex(
                name: "IX_AccountingPeriod_ClosedBy",
                table: "AccountingPeriod");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccountingPeriod_CloseChecklistJson_Json",
                table: "AccountingPeriod");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccountingPeriod_ClosedAt_Utc",
                table: "AccountingPeriod");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccountingPeriod_SourceCutoff_Utc",
                table: "AccountingPeriod");

            migrationBuilder.DropColumn(
                name: "CloseChecklistJson",
                table: "AccountingPeriod");

            migrationBuilder.DropColumn(
                name: "CloseReason",
                table: "AccountingPeriod");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "AccountingPeriod");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "AccountingPeriod");

            migrationBuilder.DropColumn(
                name: "SourceCutoff",
                table: "AccountingPeriod");
        }
    }
}
