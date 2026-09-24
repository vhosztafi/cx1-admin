using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceBankReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SignedAmount = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankLine", x => x.Id);
                    table.CheckConstraint("CK_BankLine_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_BankLine_Facts", "[SignedAmount]<>0 AND [Currency]='GBP' AND LEN(TRIM([Reference])) BETWEEN 1 AND 200 AND LEN(TRIM([ImportKey])) BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_BankLine_ImportedAt_Utc", "DATEPART(TZOFFSET,[ImportedAt]) = 0");
                    table.CheckConstraint("CK_BankLine_RawHash", "[RawHash]=HASHBYTES('SHA2_256',CONVERT(varbinary(max),[RawJson]))");
                    table.CheckConstraint("CK_BankLine_RawJson_Json", "ISJSON([RawJson]) = 1");
                    table.ForeignKey(
                        name: "FK_BankLine_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BankLine_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Reconciliation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reconciliation", x => x.Id);
                    table.CheckConstraint("CK_Reconciliation_CompletedAt_Utc", "DATEPART(TZOFFSET,[CompletedAt]) = 0");
                    table.CheckConstraint("CK_Reconciliation_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Reconciliation_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.CheckConstraint("CK_Reconciliation_Window", "[From]<[To] AND ([CompletedAt] IS NULL AND [CompletedBy] IS NULL OR [CompletedAt] IS NOT NULL AND [CompletedBy] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Reconciliation_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Reconciliation_User_CompletedBy",
                        column: x => x.CompletedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Reconciliation_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BankLineExclusion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReconciliationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DuplicateOfBankLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ExcludedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankLineExclusion", x => x.Id);
                    table.CheckConstraint("CK_BankLineExclusion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_BankLineExclusion_Evidence", "[BankLineId]<>[DuplicateOfBankLineId] AND LEN(TRIM([EvidenceReference])) BETWEEN 10 AND 300 AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
                    table.CheckConstraint("CK_BankLineExclusion_ExcludedAt_Utc", "DATEPART(TZOFFSET,[ExcludedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_BankLineExclusion_BankLine_BankLineId",
                        column: x => x.BankLineId,
                        principalTable: "BankLine",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BankLineExclusion_BankLine_DuplicateOfBankLineId",
                        column: x => x.DuplicateOfBankLineId,
                        principalTable: "BankLine",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BankLineExclusion_Reconciliation_ReconciliationId",
                        column: x => x.ReconciliationId,
                        principalTable: "Reconciliation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BankLineExclusion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationMatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReconciliationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinancePostingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignedAmount = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    ReversalOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MatchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationMatch", x => x.Id);
                    table.CheckConstraint("CK_ReconciliationMatch_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ReconciliationMatch_Facts", "[SignedAmount]<>0 AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
                    table.CheckConstraint("CK_ReconciliationMatch_MatchedAt_Utc", "DATEPART(TZOFFSET,[MatchedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ReconciliationMatch_BankLine_BankLineId",
                        column: x => x.BankLineId,
                        principalTable: "BankLine",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReconciliationMatch_FinancePosting_FinancePostingId",
                        column: x => x.FinancePostingId,
                        principalTable: "FinancePosting",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReconciliationMatch_ReconciliationMatch_ReversalOfId",
                        column: x => x.ReversalOfId,
                        principalTable: "ReconciliationMatch",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReconciliationMatch_Reconciliation_ReconciliationId",
                        column: x => x.ReconciliationId,
                        principalTable: "Reconciliation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReconciliationMatch_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationTargetVariance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReconciliationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinancePostingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignedResidual = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ExplainedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationTargetVariance", x => x.Id);
                    table.CheckConstraint("CK_ReconciliationTargetVariance_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ReconciliationTargetVariance_Evidence", "[SignedResidual]<>0 AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
                    table.CheckConstraint("CK_ReconciliationTargetVariance_ExplainedAt_Utc", "DATEPART(TZOFFSET,[ExplainedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ReconciliationTargetVariance_FinancePosting_FinancePostingId",
                        column: x => x.FinancePostingId,
                        principalTable: "FinancePosting",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReconciliationTargetVariance_Reconciliation_ReconciliationId",
                        column: x => x.ReconciliationId,
                        principalTable: "Reconciliation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReconciliationTargetVariance_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationVariance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReconciliationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignedResidual = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ExplainedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationVariance", x => x.Id);
                    table.CheckConstraint("CK_ReconciliationVariance_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ReconciliationVariance_Evidence", "[SignedResidual]<>0 AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
                    table.CheckConstraint("CK_ReconciliationVariance_ExplainedAt_Utc", "DATEPART(TZOFFSET,[ExplainedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ReconciliationVariance_BankLine_BankLineId",
                        column: x => x.BankLineId,
                        principalTable: "BankLine",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReconciliationVariance_Reconciliation_ReconciliationId",
                        column: x => x.ReconciliationId,
                        principalTable: "Reconciliation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReconciliationVariance_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankLine_AgencyId_ImportKey",
                table: "BankLine",
                columns: new[] { "AgencyId", "ImportKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankLine_AgencyId_ValueDate_Reference_SignedAmount",
                table: "BankLine",
                columns: new[] { "AgencyId", "ValueDate", "Reference", "SignedAmount" });

            migrationBuilder.CreateIndex(
                name: "IX_BankLine_CreatedBy",
                table: "BankLine",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_BankLineExclusion_BankLineId",
                table: "BankLineExclusion",
                column: "BankLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankLineExclusion_CreatedBy",
                table: "BankLineExclusion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_BankLineExclusion_DuplicateOfBankLineId",
                table: "BankLineExclusion",
                column: "DuplicateOfBankLineId");

            migrationBuilder.CreateIndex(
                name: "IX_BankLineExclusion_ReconciliationId",
                table: "BankLineExclusion",
                column: "ReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_Reconciliation_AgencyId_From_To",
                table: "Reconciliation",
                columns: new[] { "AgencyId", "From", "To" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reconciliation_CompletedBy",
                table: "Reconciliation",
                column: "CompletedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Reconciliation_CreatedBy",
                table: "Reconciliation",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationMatch_BankLineId_FinancePostingId",
                table: "ReconciliationMatch",
                columns: new[] { "BankLineId", "FinancePostingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationMatch_CreatedBy",
                table: "ReconciliationMatch",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationMatch_FinancePostingId_BankLineId",
                table: "ReconciliationMatch",
                columns: new[] { "FinancePostingId", "BankLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationMatch_ReconciliationId",
                table: "ReconciliationMatch",
                column: "ReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationMatch_ReversalOfId",
                table: "ReconciliationMatch",
                column: "ReversalOfId",
                unique: true,
                filter: "[ReversalOfId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationTargetVariance_CreatedBy",
                table: "ReconciliationTargetVariance",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationTargetVariance_FinancePostingId",
                table: "ReconciliationTargetVariance",
                column: "FinancePostingId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationTargetVariance_ReconciliationId_FinancePostingId_ExplainedAt",
                table: "ReconciliationTargetVariance",
                columns: new[] { "ReconciliationId", "FinancePostingId", "ExplainedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationVariance_BankLineId",
                table: "ReconciliationVariance",
                column: "BankLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationVariance_CreatedBy",
                table: "ReconciliationVariance",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationVariance_ReconciliationId_BankLineId_ExplainedAt",
                table: "ReconciliationVariance",
                columns: new[] { "ReconciliationId", "BankLineId", "ExplainedAt" });

            migrationBuilder.Sql(FinanceBankMigrationSql.BankLineGuard);
            migrationBuilder.Sql(FinanceBankMigrationSql.ReconciliationGuard);
            migrationBuilder.Sql(FinanceBankMigrationSql.MatchGuard);
            migrationBuilder.Sql(FinanceBankMigrationSql.ExclusionGuard);
            migrationBuilder.Sql(FinanceBankMigrationSql.VarianceGuard);
            migrationBuilder.Sql(FinanceBankMigrationSql.TargetVarianceGuard);
            migrationBuilder.Sql(FinanceBankMigrationSql.BankReceiptGuard);
            migrationBuilder.Sql(FinanceBankMigrationSql.PostingReconciliationGuard);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinancePosting_ReconciliationGuard");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Receipt_BankImportGuard");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ReconciliationTargetVariance_Guard");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ReconciliationVariance_Guard");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_BankLineExclusion_Guard");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ReconciliationMatch_Guard");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Reconciliation_Guard");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_BankLine_Guard");
            migrationBuilder.DropTable(
                name: "BankLineExclusion");

            migrationBuilder.DropTable(
                name: "ReconciliationMatch");

            migrationBuilder.DropTable(
                name: "ReconciliationTargetVariance");

            migrationBuilder.DropTable(
                name: "ReconciliationVariance");

            migrationBuilder.DropTable(
                name: "BankLine");

            migrationBuilder.DropTable(
                name: "Reconciliation");
        }
    }
}
