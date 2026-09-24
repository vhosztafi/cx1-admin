using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceLedgerFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancePosting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DebtorKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AccountingPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    DebtorDelta = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    ProviderDelta = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    CashDelta = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    InternalDelta = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancePosting", x => x.Id);
                    table.CheckConstraint("CK_FinancePosting_Amounts", "[DebtorDelta]<>0 OR [ProviderDelta]<>0 OR [CashDelta]<>0 OR [InternalDelta]<>0");
                    table.CheckConstraint("CK_FinancePosting_Balance", "[DebtorDelta]-[ProviderDelta]+[CashDelta]+[InternalDelta]=0");
                    table.CheckConstraint("CK_FinancePosting_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_FinancePosting_Currency", "[Currency]='GBP'");
                    table.CheckConstraint("CK_FinancePosting_Debtor", "[DebtorKind]='agency' OR ([DebtorKind]='relationship' AND [RelationshipId] IS NOT NULL)");
                    table.CheckConstraint("CK_FinancePosting_EffectiveAt_Utc", "DATEPART(TZOFFSET,[EffectiveAt]) = 0");
                    table.CheckConstraint("CK_FinancePosting_PostedAt_Utc", "DATEPART(TZOFFSET,[PostedAt]) = 0");
                    table.CheckConstraint("CK_FinancePosting_Posting", "[PostedAt]>=[CreatedAt]");
                    table.CheckConstraint("CK_FinancePosting_Reason", "LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
                    table.CheckConstraint("CK_FinancePosting_Source", "[SourceKind] IN ('receipt','receipt-reversal','refund','correction') AND [SourceId]<>'00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_FinancePosting_AccountingPeriod_AccountingPeriodId",
                        column: x => x.AccountingPeriodId,
                        principalTable: "AccountingPeriod",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancePosting_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancePosting_ClientAgencyRelationship_RelationshipId",
                        column: x => x.RelationshipId,
                        principalTable: "ClientAgencyRelationship",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancePosting_PolicyTransaction_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "PolicyTransaction",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancePosting_Policy_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policy",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancePosting_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancePosting_AccountingPeriodId",
                table: "FinancePosting",
                column: "AccountingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancePosting_AgencyId_PostingDate_Id",
                table: "FinancePosting",
                columns: new[] { "AgencyId", "PostingDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancePosting_CreatedBy",
                table: "FinancePosting",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FinancePosting_PolicyId_PostingDate",
                table: "FinancePosting",
                columns: new[] { "PolicyId", "PostingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancePosting_RelationshipId",
                table: "FinancePosting",
                column: "RelationshipId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancePosting_SourceKind_SourceId",
                table: "FinancePosting",
                columns: new[] { "SourceKind", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancePosting_TransactionId",
                table: "FinancePosting",
                column: "TransactionId");

            // Finance rows are append-only. The period update lock uses the same
            // lock target as AccountingPeriods.HoldAsync and period closure.
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FinancePosting_Guard ON FinancePosting AFTER INSERT,UPDATE,DELETE AS BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM deleted)
                  THROW 51600,'A posted finance movement is immutable.',1;
                IF EXISTS(SELECT 1 FROM inserted i
                  LEFT JOIN AccountingPeriod ap WITH(UPDLOCK,HOLDLOCK) ON ap.Id=i.AccountingPeriodId
                  LEFT JOIN ClientAgencyRelationship rel ON rel.Id=i.RelationshipId
                  LEFT JOIN Policy pol ON pol.Id=i.PolicyId
                  LEFT JOIN PolicyTransaction tx ON tx.Id=i.TransactionId
                  WHERE ap.Id IS NULL OR ap.State<>'open' OR i.PostingDate<ap.StartsOn OR i.PostingDate>=ap.EndsOn
                    OR (i.RelationshipId IS NOT NULL AND (rel.Id IS NULL OR rel.AgencyId<>i.AgencyId))
                    OR (i.PolicyId IS NOT NULL AND (pol.Id IS NULL OR pol.AgencyId<>i.AgencyId
                      OR (i.RelationshipId IS NOT NULL AND pol.RelationshipId<>i.RelationshipId)))
                    OR (i.TransactionId IS NOT NULL AND (tx.Id IS NULL OR i.PolicyId IS NULL
                      OR tx.PolicyId<>i.PolicyId)))
                  THROW 51601,'Finance posting must use one current agency, relationship, policy and open period.',1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancePosting");
        }
    }
}
