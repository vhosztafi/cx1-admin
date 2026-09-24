using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceRefundPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinanceRefundPayment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefundRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditObligationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DebtorKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DebtorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    PriorPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderState = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ProviderOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceRefundPayment", x => x.Id);
                    table.CheckConstraint("CK_FinanceRefundPayment_AppliedAt_Utc", "DATEPART(TZOFFSET,[AppliedAt]) = 0");
                    table.CheckConstraint("CK_FinanceRefundPayment_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_FinanceRefundPayment_Facts", "[Amount]>0 AND [Currency]='GBP' AND [DebtorKind] IN ('agency','relationship') AND [State] IN ('queued','paid','rejected','failed') AND LEN(TRIM([ReviewReason])) BETWEEN 10 AND 1000");
                    table.CheckConstraint("CK_FinanceRefundPayment_Provider", "([ProviderState] IS NULL AND [ProviderOperationId] IS NULL AND [ProviderEventId] IS NULL AND [AppliedAt] IS NULL) OR ([ProviderState] IN ('accepted','rejected') AND [ProviderOperationId] IS NOT NULL AND [ProviderEventId] IS NOT NULL AND [AppliedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_FinanceRefundPayment_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_FinanceRefundPayment_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceRefundPayment_FinanceRefundPayment_PriorPaymentId",
                        column: x => x.PriorPaymentId,
                        principalTable: "FinanceRefundPayment",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceRefundPayment_IssueFinancialObligation_CreditObligationId",
                        column: x => x.CreditObligationId,
                        principalTable: "IssueFinancialObligation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceRefundPayment_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceRefundPayment_RefundRequest_RefundRequestId",
                        column: x => x.RefundRequestId,
                        principalTable: "RefundRequest",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceRefundPayment_SettingVersion_ScenarioVersionId",
                        column: x => x.ScenarioVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceRefundPayment_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceRefundPayment_AgencyId",
                table: "FinanceRefundPayment",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceRefundPayment_CreatedBy",
                table: "FinanceRefundPayment",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceRefundPayment_CreditObligationId",
                table: "FinanceRefundPayment",
                column: "CreditObligationId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceRefundPayment_OperationKey",
                table: "FinanceRefundPayment",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceRefundPayment_PriorPaymentId",
                table: "FinanceRefundPayment",
                column: "PriorPaymentId",
                unique: true,
                filter: "[PriorPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceRefundPayment_RefundRequestId",
                table: "FinanceRefundPayment",
                column: "RefundRequestId",
                unique: true,
                filter: "[State]<>'rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceRefundPayment_ScenarioVersionId",
                table: "FinanceRefundPayment",
                column: "ScenarioVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceRefundPayment_WorkId",
                table: "FinanceRefundPayment",
                column: "WorkId",
                unique: true);
            migrationBuilder.DropCheckConstraint(name: "CK_OutboxWork_AttemptLimit", table: "OutboxWork");
            migrationBuilder.AddCheckConstraint(name: "CK_OutboxWork_AttemptLimit", table: "OutboxWork",
                sql: "([AttemptLimit] IN (6,12,18) OR ([Kind]='finance-refund-payment' AND [AttemptLimit]=2147483647)) AND [Attempts] <= [AttemptLimit]");
            migrationBuilder.Sql(FinancePaymentMigrationSql.Seed);
            migrationBuilder.Sql(FinancePaymentMigrationSql.PaymentGuard);
            migrationBuilder.Sql(FinancePaymentMigrationSql.PostingSource);
            migrationBuilder.Sql(FinancePaymentMigrationSql.ReconciliationGuard);
            migrationBuilder.Sql(FinancePaymentMigrationSql.MatchGuard);
            migrationBuilder.Sql(FinancePaymentMigrationSql.TargetVarianceGuard);
            migrationBuilder.Sql(FinancePaymentMigrationSql.PostingReconciliationGuard);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(FinanceBankMigrationSql.ReconciliationGuard.Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER"));
            migrationBuilder.Sql(FinanceBankMigrationSql.MatchGuard.Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER"));
            migrationBuilder.Sql(FinanceBankMigrationSql.TargetVarianceGuard.Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER"));
            migrationBuilder.Sql(FinanceBankMigrationSql.PostingReconciliationGuard.Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER"));
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinancePosting_RefundSource");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinanceRefundPayment_Guard");
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM OutboxWork WHERE Kind='finance-refund-payment' AND Attempts>18) THROW 52620,'Refund recovery attempts exceed legacy budget; downgrade refused.',1; UPDATE OutboxWork SET AttemptLimit=18 WHERE Kind='finance-refund-payment' AND AttemptLimit=2147483647;");
            migrationBuilder.DropCheckConstraint(name: "CK_OutboxWork_AttemptLimit", table: "OutboxWork");
            migrationBuilder.AddCheckConstraint(name: "CK_OutboxWork_AttemptLimit", table: "OutboxWork",
                sql: "[AttemptLimit] IN (6,12,18) AND [Attempts] <= [AttemptLimit]");
            migrationBuilder.DropTable(
                name: "FinanceRefundPayment");
        }
    }
}
