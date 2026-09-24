using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceReceiptApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FinancePosting_Source",
                table: "FinancePosting");

            migrationBuilder.CreateTable(
                name: "Receipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OriginId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    AccountingPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipt", x => x.Id);
                    table.CheckConstraint("CK_Receipt_Amount", "[Amount]>0 AND [Currency]='GBP'");
                    table.CheckConstraint("CK_Receipt_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Receipt_Origin", "[OriginKind] IN ('manual','bank-import') AND [OriginId]<>'00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Receipt_PostedAt_Utc", "DATEPART(TZOFFSET,[PostedAt]) = 0");
                    table.CheckConstraint("CK_Receipt_Reference", "LEN(TRIM([BankReference])) BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "FK_Receipt_AccountingPeriod_AccountingPeriodId",
                        column: x => x.AccountingPeriodId,
                        principalTable: "AccountingPeriod",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Receipt_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Receipt_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Allocation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObligationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    ReversalOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    AccountingPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allocation", x => x.Id);
                    table.CheckConstraint("CK_Allocation_Amount", "[Amount]>0 AND [Ordinal]>0");
                    table.CheckConstraint("CK_Allocation_AppliedAt_Utc", "DATEPART(TZOFFSET,[AppliedAt]) = 0");
                    table.CheckConstraint("CK_Allocation_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Allocation_Reason", "LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
                    table.ForeignKey(
                        name: "FK_Allocation_AccountingPeriod_AccountingPeriodId",
                        column: x => x.AccountingPeriodId,
                        principalTable: "AccountingPeriod",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Allocation_Allocation_ReversalOfId",
                        column: x => x.ReversalOfId,
                        principalTable: "Allocation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Allocation_IssueFinancialObligation_ObligationId",
                        column: x => x.ObligationId,
                        principalTable: "IssueFinancialObligation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Allocation_Receipt_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipt",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Allocation_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReceiptPayerAssignment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    PayerKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PayerAgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PayerRelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptPayerAssignment", x => x.Id);
                    table.CheckConstraint("CK_ReceiptPayerAssignment_AssignedAt_Utc", "DATEPART(TZOFFSET,[AssignedAt]) = 0");
                    table.CheckConstraint("CK_ReceiptPayerAssignment_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ReceiptPayerAssignment_Payer", "([PayerKind]='unidentified' AND [PayerAgencyId] IS NULL AND [PayerRelationshipId] IS NULL) OR ([PayerKind]='agency' AND [PayerAgencyId] IS NOT NULL AND [PayerRelationshipId] IS NULL) OR ([PayerKind]='relationship' AND [PayerAgencyId] IS NULL AND [PayerRelationshipId] IS NOT NULL)");
                    table.CheckConstraint("CK_ReceiptPayerAssignment_Reason", "LEN(TRIM([Reason])) BETWEEN 10 AND 1000 AND [Ordinal]>0");
                    table.ForeignKey(
                        name: "FK_ReceiptPayerAssignment_Agency_PayerAgencyId",
                        column: x => x.PayerAgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReceiptPayerAssignment_ClientAgencyRelationship_PayerRelationshipId",
                        column: x => x.PayerRelationshipId,
                        principalTable: "ClientAgencyRelationship",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReceiptPayerAssignment_Receipt_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipt",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReceiptPayerAssignment_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinancePosting_Source",
                table: "FinancePosting",
                sql: "[SourceKind] IN ('receipt','receipt-application','receipt-application-reversal','receipt-reversal','refund','correction') AND [SourceId]<>'00000000-0000-0000-0000-000000000000'");

            migrationBuilder.CreateIndex(
                name: "IX_Allocation_AccountingPeriodId",
                table: "Allocation",
                column: "AccountingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_Allocation_CreatedBy",
                table: "Allocation",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Allocation_ObligationId",
                table: "Allocation",
                column: "ObligationId");

            migrationBuilder.CreateIndex(
                name: "IX_Allocation_OperationId_Ordinal",
                table: "Allocation",
                columns: new[] { "OperationId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Allocation_ReceiptId_ObligationId",
                table: "Allocation",
                columns: new[] { "ReceiptId", "ObligationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Allocation_ReversalOfId",
                table: "Allocation",
                column: "ReversalOfId",
                unique: true,
                filter: "[ReversalOfId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Receipt_AccountingPeriodId",
                table: "Receipt",
                column: "AccountingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipt_AgencyId_PostingDate",
                table: "Receipt",
                columns: new[] { "AgencyId", "PostingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Receipt_CreatedBy",
                table: "Receipt",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Receipt_OriginKind_OriginId",
                table: "Receipt",
                columns: new[] { "OriginKind", "OriginId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPayerAssignment_CreatedBy",
                table: "ReceiptPayerAssignment",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPayerAssignment_OperationId",
                table: "ReceiptPayerAssignment",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPayerAssignment_PayerAgencyId",
                table: "ReceiptPayerAssignment",
                column: "PayerAgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPayerAssignment_PayerRelationshipId",
                table: "ReceiptPayerAssignment",
                column: "PayerRelationshipId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPayerAssignment_ReceiptId_Ordinal",
                table: "ReceiptPayerAssignment",
                columns: new[] { "ReceiptId", "Ordinal" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Receipt_Immutable ON Receipt AFTER UPDATE,DELETE AS BEGIN
                  SET NOCOUNT ON;
                  THROW 52100,'Receipt cash fact is immutable.',1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_ReceiptPayerAssignment_Immutable ON ReceiptPayerAssignment AFTER UPDATE,DELETE AS BEGIN
                  SET NOCOUNT ON;
                  THROW 52101,'Receipt payer assignment is immutable.',1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Allocation_Immutable ON Allocation AFTER UPDATE,DELETE AS BEGIN
                  SET NOCOUNT ON;
                  THROW 52102,'Allocation and reversal rows are immutable.',1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FinancePosting_ReceiptSource ON FinancePosting AFTER INSERT AS BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted p
                    LEFT JOIN Receipt r ON r.Id=p.SourceId AND p.SourceKind='receipt'
                    LEFT JOIN Allocation a ON a.Id=p.SourceId AND p.SourceKind IN ('receipt-application','receipt-application-reversal')
                    LEFT JOIN Receipt ar ON ar.Id=a.ReceiptId
                    LEFT JOIN IssueFinancialObligation o ON o.Id=a.ObligationId
                    WHERE (p.SourceKind='receipt' AND (r.Id IS NULL OR p.AgencyId<>r.AgencyId OR p.AccountingPeriodId<>r.AccountingPeriodId
                      OR p.PostingDate<>r.PostingDate OR p.PostedAt<>r.PostedAt OR p.Currency<>r.Currency
                      OR p.RelationshipId IS NOT NULL OR p.PolicyId IS NOT NULL OR p.TransactionId IS NOT NULL
                      OR p.DebtorDelta<>0 OR p.ProviderDelta<>0 OR p.CashDelta<>r.Amount OR p.InternalDelta<>-r.Amount))
                      OR (p.SourceKind IN ('receipt-application','receipt-application-reversal') AND
                        (a.Id IS NULL OR ar.Id IS NULL OR o.Id IS NULL OR
                         (p.SourceKind='receipt-application' AND a.ReversalOfId IS NOT NULL) OR
                         (p.SourceKind='receipt-application-reversal' AND a.ReversalOfId IS NULL) OR
                         p.AgencyId<>ar.AgencyId OR p.RelationshipId IS NULL OR p.RelationshipId<>o.RelationshipId
                         OR p.PolicyId IS NULL OR p.PolicyId<>o.PolicyId OR
                         p.TransactionId IS NULL OR p.TransactionId<>o.TransactionId OR p.DebtorKind<>o.DebtorKind OR
                         p.AccountingPeriodId<>a.AccountingPeriodId OR p.PostingDate<>a.PostingDate OR
                         p.PostedAt<>a.AppliedAt OR p.Currency<>ar.Currency OR p.CashDelta<>0 OR p.ProviderDelta<>0 OR
                         p.DebtorDelta<>(CASE WHEN a.ReversalOfId IS NULL THEN -a.Amount ELSE a.Amount END) OR
                         p.InternalDelta<>(CASE WHEN a.ReversalOfId IS NULL THEN a.Amount ELSE -a.Amount END))))
                    THROW 52103,'Receipt posting must match its exact saved cash or application source.',1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Receipt_Insert ON Receipt AFTER INSERT AS BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted r
                    LEFT JOIN AccountingPeriod p WITH(UPDLOCK,HOLDLOCK) ON p.Id=r.AccountingPeriodId
                    WHERE p.Id IS NULL OR p.State<>'open' OR r.PostingDate<p.StartsOn OR r.PostingDate>=p.EndsOn
                      OR r.PostedAt<r.CreatedAt)
                    THROW 52104,'Receipt posting period is unavailable.',1;
                  INSERT FinancePosting(Id,SourceKind,SourceId,AgencyId,RelationshipId,PolicyId,TransactionId,
                    DebtorKind,AccountingPeriodId,PostingDate,EffectiveAt,PostedAt,Currency,DebtorDelta,
                    ProviderDelta,CashDelta,InternalDelta,Reason,CreatedAt,CreatedBy)
                  SELECT NEWID(),N'receipt',r.Id,r.AgencyId,NULL,NULL,NULL,N'agency',r.AccountingPeriodId,
                    r.PostingDate,r.PostedAt,r.PostedAt,r.Currency,0,0,r.Amount,-r.Amount,
                    N'Receipt cash entered suspense',r.CreatedAt,r.CreatedBy FROM inserted r;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_ReceiptPayerAssignment_Insert ON ReceiptPayerAssignment AFTER INSERT AS BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted a
                    JOIN Receipt r WITH(UPDLOCK,HOLDLOCK) ON r.Id=a.ReceiptId
                    LEFT JOIN ClientAgencyRelationship rel ON rel.Id=a.PayerRelationshipId
                    LEFT JOIN ReceiptPayerAssignment prior ON prior.ReceiptId=a.ReceiptId AND prior.Ordinal=a.Ordinal-1
                    WHERE (a.PayerKind='agency' AND a.PayerAgencyId<>r.AgencyId) OR
                      (a.PayerKind='relationship' AND (rel.Id IS NULL OR rel.AgencyId<>r.AgencyId)) OR
                      (a.Ordinal>1 AND (prior.Id IS NULL OR
                        (SELECT COALESCE(SUM(CASE WHEN ReversalOfId IS NULL THEN Amount ELSE -Amount END),0)
                         FROM Allocation WHERE ReceiptId=a.ReceiptId)<>0)))
                    THROW 52105,'Payer assignment must stay in debtor scope and cannot move applied cash.',1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Allocation_Insert ON Allocation AFTER INSERT AS BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted a
                    JOIN Receipt r WITH(UPDLOCK,HOLDLOCK) ON r.Id=a.ReceiptId
                    JOIN IssueFinancialObligation o WITH(UPDLOCK,HOLDLOCK) ON o.Id=a.ObligationId
                    LEFT JOIN Allocation original ON original.Id=a.ReversalOfId
                    LEFT JOIN AccountingPeriod p WITH(UPDLOCK,HOLDLOCK) ON p.Id=a.AccountingPeriodId
                    OUTER APPLY (SELECT TOP(1) x.PayerKind,x.PayerAgencyId,x.PayerRelationshipId
                      FROM ReceiptPayerAssignment x WHERE x.ReceiptId=a.ReceiptId ORDER BY x.Ordinal DESC) payer
                    LEFT JOIN Journal j ON j.ObligationId=o.Id AND j.PostedAt IS NOT NULL
                    WHERE p.Id IS NULL OR p.State<>'open' OR a.PostingDate<p.StartsOn OR a.PostingDate>=p.EndsOn
                      OR a.AppliedAt<a.CreatedAt OR o.AgencyId<>r.AgencyId OR o.Currency<>r.Currency OR o.InvoiceDue<=0
                      OR j.Id IS NULL OR
                      (a.ReversalOfId IS NULL AND (payer.PayerKind IS NULL OR payer.PayerKind='unidentified' OR
                        payer.PayerKind<>o.DebtorKind OR
                        (payer.PayerKind='agency' AND payer.PayerAgencyId<>o.DebtorAgencyId) OR
                        (payer.PayerKind='relationship' AND payer.PayerRelationshipId<>o.DebtorRelationshipId))) OR
                      (a.ReversalOfId IS NOT NULL AND (original.Id IS NULL OR original.ReversalOfId IS NOT NULL OR
                        original.ReceiptId<>a.ReceiptId OR original.ObligationId<>a.ObligationId OR original.Amount<>a.Amount)))
                    THROW 52106,'Allocation must match a posted invoice, payer, source and open period.',1;
                  IF EXISTS(SELECT 1 FROM Receipt r WITH(UPDLOCK,HOLDLOCK)
                    WHERE r.Id IN (SELECT ReceiptId FROM inserted) AND
                      (SELECT COALESCE(SUM(CASE WHEN ReversalOfId IS NULL THEN Amount ELSE -Amount END),0)
                       FROM Allocation WHERE ReceiptId=r.Id)>r.Amount)
                    THROW 52107,'Receipt residual exceeded.',1;
                  IF EXISTS(SELECT 1 FROM IssueFinancialObligation o WITH(UPDLOCK,HOLDLOCK)
                    WHERE o.Id IN (SELECT ObligationId FROM inserted) AND
                      (SELECT COALESCE(SUM(CASE WHEN ReversalOfId IS NULL THEN Amount ELSE -Amount END),0)
                       FROM Allocation WHERE ObligationId=o.Id)>o.InvoiceDue)
                    THROW 52108,'Invoice residual exceeded.',1;
                  INSERT FinancePosting(Id,SourceKind,SourceId,AgencyId,RelationshipId,PolicyId,TransactionId,
                    DebtorKind,AccountingPeriodId,PostingDate,EffectiveAt,PostedAt,Currency,DebtorDelta,
                    ProviderDelta,CashDelta,InternalDelta,Reason,CreatedAt,CreatedBy)
                  SELECT NEWID(),CASE WHEN a.ReversalOfId IS NULL THEN N'receipt-application' ELSE N'receipt-application-reversal' END,
                    a.Id,r.AgencyId,o.RelationshipId,o.PolicyId,o.TransactionId,o.DebtorKind,a.AccountingPeriodId,
                    a.PostingDate,a.AppliedAt,a.AppliedAt,r.Currency,
                    CASE WHEN a.ReversalOfId IS NULL THEN -a.Amount ELSE a.Amount END,
                    0,0,CASE WHEN a.ReversalOfId IS NULL THEN a.Amount ELSE -a.Amount END,
                    CASE WHEN a.ReversalOfId IS NULL THEN N'Receipt applied to invoice' ELSE N'Receipt application reversed' END,
                    a.CreatedAt,a.CreatedBy
                    FROM inserted a JOIN Receipt r ON r.Id=a.ReceiptId
                    JOIN IssueFinancialObligation o ON o.Id=a.ObligationId;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Allocation_Insert");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ReceiptPayerAssignment_Insert");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Receipt_Insert");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinancePosting_ReceiptSource");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Allocation_Immutable");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ReceiptPayerAssignment_Immutable");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Receipt_Immutable");
            migrationBuilder.DropTable(
                name: "Allocation");

            migrationBuilder.DropTable(
                name: "ReceiptPayerAssignment");

            migrationBuilder.DropTable(
                name: "Receipt");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinancePosting_Source",
                table: "FinancePosting");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinancePosting_Source",
                table: "FinancePosting",
                sql: "[SourceKind] IN ('receipt','receipt-reversal','refund','correction') AND [SourceId]<>'00000000-0000-0000-0000-000000000000'");
        }
    }
}
