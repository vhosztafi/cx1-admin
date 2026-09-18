using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingSignedPostingStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_Kind",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Journal_Purpose",
                table: "Journal");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_Amounts",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_BrokerPayable",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_Commission",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_Fee",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_FeeShare",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_GrossDue",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_InvoiceDue",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_NetDue",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_Premium",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_Purpose",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_Tax",
                table: "IssueFinancialObligation");

            migrationBuilder.DropIndex(
                name: "IX_IssueFinancialComponent_ObligationId_Code",
                table: "IssueFinancialComponent");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialComponent_Amount",
                table: "IssueFinancialComponent");

            migrationBuilder.AlterColumn<Guid>(
                name: "RatingId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "QuoteRevisionId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "CycleId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "AcceptanceId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "ServicingAcceptanceId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServicingCycleId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServicingDraftId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServicingRatingId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServicingRevisionId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AccountingPeriodId",
                table: "Journal",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PostingDate",
                table: "Journal",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Ordinal",
                table: "IssueFinancialComponent",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalComponentId",
                table: "IssueFinancialComponent",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ServicingAcceptance_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingAcceptance",
                columns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_ServicingAcceptanceId_ServicingCycleId_ServicingDraftId_ServicingRevisionId_ServicingRatingId",
                table: "PolicyTransaction",
                columns: new[] { "ServicingAcceptanceId", "ServicingCycleId", "ServicingDraftId", "ServicingRevisionId", "ServicingRatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_ServicingCycleId_ServicingDraftId_PolicyId",
                table: "PolicyTransaction",
                columns: new[] { "ServicingCycleId", "ServicingDraftId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_ServicingDraftId",
                table: "PolicyTransaction",
                column: "ServicingDraftId",
                unique: true,
                filter: "[ServicingDraftId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_ServicingRatingId_ServicingCycleId_ServicingDraftId_ServicingRevisionId",
                table: "PolicyTransaction",
                columns: new[] { "ServicingRatingId", "ServicingCycleId", "ServicingDraftId", "ServicingRevisionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_DecisionSource",
                table: "PolicyTransaction",
                sql: "([Kind]='new-business' AND [CycleId] IS NOT NULL AND [QuoteRevisionId] IS NOT NULL AND [RatingId] IS NOT NULL AND [AcceptanceId] IS NOT NULL AND [ServicingDraftId] IS NULL AND [ServicingRevisionId] IS NULL AND [ServicingCycleId] IS NULL AND [ServicingRatingId] IS NULL AND [ServicingAcceptanceId] IS NULL) OR ([Kind]='adjustment' AND [CycleId] IS NULL AND [QuoteRevisionId] IS NULL AND [RatingId] IS NULL AND [AcceptanceId] IS NULL AND [ServicingDraftId] IS NOT NULL AND [ServicingRevisionId] IS NOT NULL AND [ServicingCycleId] IS NOT NULL AND [ServicingRatingId] IS NOT NULL AND [ServicingAcceptanceId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_Kind",
                table: "PolicyTransaction",
                sql: "[Kind] IN ('new-business','adjustment')");

            migrationBuilder.CreateIndex(
                name: "IX_Journal_AccountingPeriodId",
                table: "Journal",
                column: "AccountingPeriodId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Journal_Period",
                table: "Journal",
                sql: "([Purpose]='first-issue' AND [AccountingPeriodId] IS NULL AND [PostingDate] IS NULL) OR ([AccountingPeriodId] IS NOT NULL AND [PostingDate] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Journal_Purpose",
                table: "Journal",
                sql: "[Purpose] IN ('first-issue','adjustment','renewal','cancellation')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_Amounts",
                table: "IssueFinancialObligation",
                sql: "([Purpose]<>'first-issue' OR ([Commission]<=[Premium] AND [FeeShare]<=[Fee])) AND [GrossDue]=[Premium]+[Tax]+[Fee] AND [NetDue]=[GrossDue]-[Commission]-[FeeShare]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_BrokerPayable",
                table: "IssueFinancialObligation",
                sql: "[Purpose]<>'first-issue' OR [BrokerPayable]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_Commission",
                table: "IssueFinancialObligation",
                sql: "[Purpose]<>'first-issue' OR [Commission]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_Fee",
                table: "IssueFinancialObligation",
                sql: "[Purpose]<>'first-issue' OR [Fee]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_FeeShare",
                table: "IssueFinancialObligation",
                sql: "[Purpose]<>'first-issue' OR [FeeShare]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_GrossDue",
                table: "IssueFinancialObligation",
                sql: "[Purpose]<>'first-issue' OR [GrossDue]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_InvoiceDue",
                table: "IssueFinancialObligation",
                sql: "[Purpose]<>'first-issue' OR [InvoiceDue]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_NetDue",
                table: "IssueFinancialObligation",
                sql: "[Purpose]<>'first-issue' OR [NetDue]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_Premium",
                table: "IssueFinancialObligation",
                sql: "[Purpose]<>'first-issue' OR [Premium]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_Purpose",
                table: "IssueFinancialObligation",
                sql: "[Purpose] IN ('first-issue','adjustment','renewal','cancellation')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_Tax",
                table: "IssueFinancialObligation",
                sql: "[Purpose]<>'first-issue' OR [Tax]>=0");

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialComponent_ObligationId_Code_Ordinal",
                table: "IssueFinancialComponent",
                columns: new[] { "ObligationId", "Code", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialComponent_ObligationId_OriginalComponentId",
                table: "IssueFinancialComponent",
                columns: new[] { "ObligationId", "OriginalComponentId" },
                unique: true,
                filter: "[OriginalComponentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialComponent_OriginalComponentId",
                table: "IssueFinancialComponent",
                column: "OriginalComponentId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialComponent_Ordinal",
                table: "IssueFinancialComponent",
                sql: "[Ordinal] BETWEEN 1 AND 1000");

            migrationBuilder.AddForeignKey(
                name: "FK_IssueFinancialComponent_IssueFinancialComponent_OriginalComponentId",
                table: "IssueFinancialComponent",
                column: "OriginalComponentId",
                principalTable: "IssueFinancialComponent",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Journal_AccountingPeriod_AccountingPeriodId",
                table: "Journal",
                column: "AccountingPeriodId",
                principalTable: "AccountingPeriod",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PolicyTransaction_ServicingAcceptance_ServicingAcceptanceId_ServicingCycleId_ServicingDraftId_ServicingRevisionId_ServicingR~",
                table: "PolicyTransaction",
                columns: new[] { "ServicingAcceptanceId", "ServicingCycleId", "ServicingDraftId", "ServicingRevisionId", "ServicingRatingId" },
                principalTable: "ServicingAcceptance",
                principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PolicyTransaction_ServicingCycle_ServicingCycleId_ServicingDraftId_PolicyId",
                table: "PolicyTransaction",
                columns: new[] { "ServicingCycleId", "ServicingDraftId", "PolicyId" },
                principalTable: "ServicingCycle",
                principalColumns: new[] { "Id", "DraftId", "PolicyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PolicyTransaction_ServicingRatingResult_ServicingRatingId_ServicingCycleId_ServicingDraftId_ServicingRevisionId",
                table: "PolicyTransaction",
                columns: new[] { "ServicingRatingId", "ServicingCycleId", "ServicingDraftId", "ServicingRevisionId" },
                principalTable: "ServicingRatingResult",
                principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId" });
            ServicingPostingGuards.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ServicingPostingGuards.Down(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_IssueFinancialComponent_IssueFinancialComponent_OriginalComponentId",
                table: "IssueFinancialComponent");

            migrationBuilder.DropForeignKey(
                name: "FK_Journal_AccountingPeriod_AccountingPeriodId",
                table: "Journal");

            migrationBuilder.DropForeignKey(
                name: "FK_PolicyTransaction_ServicingAcceptance_ServicingAcceptanceId_ServicingCycleId_ServicingDraftId_ServicingRevisionId_ServicingR~",
                table: "PolicyTransaction");

            migrationBuilder.DropForeignKey(
                name: "FK_PolicyTransaction_ServicingCycle_ServicingCycleId_ServicingDraftId_PolicyId",
                table: "PolicyTransaction");

            migrationBuilder.DropForeignKey(
                name: "FK_PolicyTransaction_ServicingRatingResult_ServicingRatingId_ServicingCycleId_ServicingDraftId_ServicingRevisionId",
                table: "PolicyTransaction");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ServicingAcceptance_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingAcceptance");

            migrationBuilder.DropIndex(
                name: "IX_PolicyTransaction_ServicingAcceptanceId_ServicingCycleId_ServicingDraftId_ServicingRevisionId_ServicingRatingId",
                table: "PolicyTransaction");

            migrationBuilder.DropIndex(
                name: "IX_PolicyTransaction_ServicingCycleId_ServicingDraftId_PolicyId",
                table: "PolicyTransaction");

            migrationBuilder.DropIndex(
                name: "IX_PolicyTransaction_ServicingDraftId",
                table: "PolicyTransaction");

            migrationBuilder.DropIndex(
                name: "IX_PolicyTransaction_ServicingRatingId_ServicingCycleId_ServicingDraftId_ServicingRevisionId",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_DecisionSource",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_Kind",
                table: "PolicyTransaction");

            migrationBuilder.DropIndex(
                name: "IX_Journal_AccountingPeriodId",
                table: "Journal");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Journal_Period",
                table: "Journal");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Journal_Purpose",
                table: "Journal");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_Amounts",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_BrokerPayable",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_Commission",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_Fee",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_FeeShare",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_GrossDue",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_InvoiceDue",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_NetDue",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_Premium",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_Purpose",
                table: "IssueFinancialObligation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialObligation_Tax",
                table: "IssueFinancialObligation");

            migrationBuilder.DropIndex(
                name: "IX_IssueFinancialComponent_ObligationId_Code_Ordinal",
                table: "IssueFinancialComponent");

            migrationBuilder.DropIndex(
                name: "IX_IssueFinancialComponent_ObligationId_OriginalComponentId",
                table: "IssueFinancialComponent");

            migrationBuilder.DropIndex(
                name: "IX_IssueFinancialComponent_OriginalComponentId",
                table: "IssueFinancialComponent");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IssueFinancialComponent_Ordinal",
                table: "IssueFinancialComponent");

            migrationBuilder.DropColumn(
                name: "ServicingAcceptanceId",
                table: "PolicyTransaction");

            migrationBuilder.DropColumn(
                name: "ServicingCycleId",
                table: "PolicyTransaction");

            migrationBuilder.DropColumn(
                name: "ServicingDraftId",
                table: "PolicyTransaction");

            migrationBuilder.DropColumn(
                name: "ServicingRatingId",
                table: "PolicyTransaction");

            migrationBuilder.DropColumn(
                name: "ServicingRevisionId",
                table: "PolicyTransaction");

            migrationBuilder.DropColumn(
                name: "AccountingPeriodId",
                table: "Journal");

            migrationBuilder.DropColumn(
                name: "PostingDate",
                table: "Journal");

            migrationBuilder.DropColumn(
                name: "Ordinal",
                table: "IssueFinancialComponent");

            migrationBuilder.DropColumn(
                name: "OriginalComponentId",
                table: "IssueFinancialComponent");

            migrationBuilder.AlterColumn<Guid>(
                name: "RatingId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "QuoteRevisionId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CycleId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "AcceptanceId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_Kind",
                table: "PolicyTransaction",
                sql: "[Kind]='new-business'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Journal_Purpose",
                table: "Journal",
                sql: "[Purpose]='first-issue'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_Amounts",
                table: "IssueFinancialObligation",
                sql: "[Commission]<=[Premium] AND [FeeShare]<=[Fee] AND [GrossDue]=[Premium]+[Tax]+[Fee] AND [NetDue]=[GrossDue]-[Commission]-[FeeShare]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_BrokerPayable",
                table: "IssueFinancialObligation",
                sql: "[BrokerPayable]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_Commission",
                table: "IssueFinancialObligation",
                sql: "[Commission]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_Fee",
                table: "IssueFinancialObligation",
                sql: "[Fee]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_FeeShare",
                table: "IssueFinancialObligation",
                sql: "[FeeShare]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_GrossDue",
                table: "IssueFinancialObligation",
                sql: "[GrossDue]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_InvoiceDue",
                table: "IssueFinancialObligation",
                sql: "[InvoiceDue]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_NetDue",
                table: "IssueFinancialObligation",
                sql: "[NetDue]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_Premium",
                table: "IssueFinancialObligation",
                sql: "[Premium]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_Purpose",
                table: "IssueFinancialObligation",
                sql: "[Purpose]='first-issue'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialObligation_Tax",
                table: "IssueFinancialObligation",
                sql: "[Tax]>=0");

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialComponent_ObligationId_Code",
                table: "IssueFinancialComponent",
                columns: new[] { "ObligationId", "Code" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_IssueFinancialComponent_Amount",
                table: "IssueFinancialComponent",
                sql: "[Amount]>=0");
        }
    }
}
