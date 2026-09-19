using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CancellationPolicyGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_DecisionSource",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_IssueDecision",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_Kind",
                table: "PolicyTransaction");

            migrationBuilder.DropIndex(
                name: "IX_IssueFinancialComponent_OriginalComponentId",
                table: "IssueFinancialComponent");

            migrationBuilder.AddColumn<Guid>(
                name: "CancellationIssueDecisionId",
                table: "PolicyTransaction",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_CancellationIssueDecisionId",
                table: "PolicyTransaction",
                column: "CancellationIssueDecisionId",
                unique: true,
                filter: "[CancellationIssueDecisionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_CancellationIssueDecisionId_ServicingDraftId_PolicyId_ServicingRevisionId",
                table: "PolicyTransaction",
                columns: new[] { "CancellationIssueDecisionId", "ServicingDraftId", "PolicyId", "ServicingRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_TermId",
                table: "PolicyTransaction",
                column: "TermId",
                unique: true,
                filter: "[Kind]='cancellation'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_CancellationDecision",
                table: "PolicyTransaction",
                sql: "([Kind]='cancellation' AND [CancellationIssueDecisionId] IS NOT NULL) OR ([Kind]<>'cancellation' AND [CancellationIssueDecisionId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_DecisionSource",
                table: "PolicyTransaction",
                sql: "([Kind]='new-business' AND [CycleId] IS NOT NULL AND [QuoteRevisionId] IS NOT NULL AND [RatingId] IS NOT NULL AND [AcceptanceId] IS NOT NULL AND [ServicingDraftId] IS NULL AND [ServicingRevisionId] IS NULL AND [ServicingCycleId] IS NULL AND [ServicingRatingId] IS NULL AND [ServicingAcceptanceId] IS NULL) OR ([Kind] IN ('adjustment','renewal') AND [CycleId] IS NULL AND [QuoteRevisionId] IS NULL AND [RatingId] IS NULL AND [AcceptanceId] IS NULL AND [ServicingDraftId] IS NOT NULL AND [ServicingRevisionId] IS NOT NULL AND [ServicingCycleId] IS NOT NULL AND [ServicingRatingId] IS NOT NULL AND [ServicingAcceptanceId] IS NOT NULL) OR ([Kind]='cancellation' AND [CycleId] IS NULL AND [QuoteRevisionId] IS NULL AND [RatingId] IS NULL AND [AcceptanceId] IS NULL AND [ServicingDraftId] IS NOT NULL AND [ServicingRevisionId] IS NOT NULL AND [ServicingCycleId] IS NULL AND [ServicingRatingId] IS NULL AND [ServicingAcceptanceId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_IssueDecision",
                table: "PolicyTransaction",
                sql: "([Kind] IN ('new-business','cancellation') AND [ServicingIssueDecisionId] IS NULL) OR ([Kind] IN ('adjustment','renewal') AND [ServicingIssueDecisionId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_Kind",
                table: "PolicyTransaction",
                sql: "[Kind] IN ('new-business','adjustment','renewal','cancellation')");

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialComponent_OriginalComponentId",
                table: "IssueFinancialComponent",
                column: "OriginalComponentId",
                unique: true,
                filter: "[OriginalComponentId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PolicyTransaction_CancellationIssueDecision_CancellationIssueDecisionId_ServicingDraftId_PolicyId_ServicingRevisionId",
                table: "PolicyTransaction",
                columns: new[] { "CancellationIssueDecisionId", "ServicingDraftId", "PolicyId", "ServicingRevisionId" },
                principalTable: "CancellationIssueDecision",
                principalColumns: new[] { "Id", "DraftId", "PolicyId", "RevisionId" });
            AddGraphGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveGraphGuards(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_PolicyTransaction_CancellationIssueDecision_CancellationIssueDecisionId_ServicingDraftId_PolicyId_ServicingRevisionId",
                table: "PolicyTransaction");

            migrationBuilder.DropIndex(
                name: "IX_PolicyTransaction_CancellationIssueDecisionId",
                table: "PolicyTransaction");

            migrationBuilder.DropIndex(
                name: "IX_PolicyTransaction_CancellationIssueDecisionId_ServicingDraftId_PolicyId_ServicingRevisionId",
                table: "PolicyTransaction");

            migrationBuilder.DropIndex(
                name: "IX_PolicyTransaction_TermId",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_CancellationDecision",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_DecisionSource",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_IssueDecision",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_Kind",
                table: "PolicyTransaction");

            migrationBuilder.DropIndex(
                name: "IX_IssueFinancialComponent_OriginalComponentId",
                table: "IssueFinancialComponent");

            migrationBuilder.DropColumn(
                name: "CancellationIssueDecisionId",
                table: "PolicyTransaction");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_DecisionSource",
                table: "PolicyTransaction",
                sql: "([Kind]='new-business' AND [CycleId] IS NOT NULL AND [QuoteRevisionId] IS NOT NULL AND [RatingId] IS NOT NULL AND [AcceptanceId] IS NOT NULL AND [ServicingDraftId] IS NULL AND [ServicingRevisionId] IS NULL AND [ServicingCycleId] IS NULL AND [ServicingRatingId] IS NULL AND [ServicingAcceptanceId] IS NULL) OR ([Kind] IN ('adjustment','renewal') AND [CycleId] IS NULL AND [QuoteRevisionId] IS NULL AND [RatingId] IS NULL AND [AcceptanceId] IS NULL AND [ServicingDraftId] IS NOT NULL AND [ServicingRevisionId] IS NOT NULL AND [ServicingCycleId] IS NOT NULL AND [ServicingRatingId] IS NOT NULL AND [ServicingAcceptanceId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_IssueDecision",
                table: "PolicyTransaction",
                sql: "([Kind]='new-business' AND [ServicingIssueDecisionId] IS NULL) OR ([Kind] IN ('adjustment','renewal') AND [ServicingIssueDecisionId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_Kind",
                table: "PolicyTransaction",
                sql: "[Kind] IN ('new-business','adjustment','renewal')");

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialComponent_OriginalComponentId",
                table: "IssueFinancialComponent",
                column: "OriginalComponentId");
        }
    }
}
