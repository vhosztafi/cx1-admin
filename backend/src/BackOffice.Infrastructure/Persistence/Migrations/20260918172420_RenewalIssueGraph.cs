using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenewalIssueGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_PolicyTransaction_Kind", table: "PolicyTransaction");
            migrationBuilder.DropCheckConstraint(name: "CK_PolicyTransaction_DecisionSource", table: "PolicyTransaction");
            migrationBuilder.AddCheckConstraint(name: "CK_PolicyTransaction_Kind", table: "PolicyTransaction", sql: "[Kind] IN ('new-business','adjustment','renewal')");
            migrationBuilder.AddCheckConstraint(name: "CK_PolicyTransaction_DecisionSource", table: "PolicyTransaction", sql: "([Kind]='new-business' AND [CycleId] IS NOT NULL AND [QuoteRevisionId] IS NOT NULL AND [RatingId] IS NOT NULL AND [AcceptanceId] IS NOT NULL AND [ServicingDraftId] IS NULL AND [ServicingRevisionId] IS NULL AND [ServicingCycleId] IS NULL AND [ServicingRatingId] IS NULL AND [ServicingAcceptanceId] IS NULL) OR ([Kind] IN ('adjustment','renewal') AND [CycleId] IS NULL AND [QuoteRevisionId] IS NULL AND [RatingId] IS NULL AND [AcceptanceId] IS NULL AND [ServicingDraftId] IS NOT NULL AND [ServicingRevisionId] IS NOT NULL AND [ServicingCycleId] IS NOT NULL AND [ServicingRatingId] IS NOT NULL AND [ServicingAcceptanceId] IS NOT NULL)");
            AddRenewalIssueGuards(migrationBuilder);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_IssueDecision",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyMidIntent_Purpose",
                table: "PolicyMidIntent");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyDocumentRequest_Purpose",
                table: "PolicyDocumentRequest");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_IssueDecision",
                table: "PolicyTransaction",
                sql: "([Kind]='new-business' AND [ServicingIssueDecisionId] IS NULL) OR ([Kind] IN ('adjustment','renewal') AND [ServicingIssueDecisionId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyMidIntent_Purpose",
                table: "PolicyMidIntent",
                sql: "[Purpose] IN ('adjustment','renewal')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyDocumentRequest_Purpose",
                table: "PolicyDocumentRequest",
                sql: "[Purpose] IN ('first-issue','adjustment','renewal')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveRenewalIssueGuards(migrationBuilder);

            migrationBuilder.DropCheckConstraint(name: "CK_PolicyTransaction_Kind", table: "PolicyTransaction");
            migrationBuilder.DropCheckConstraint(name: "CK_PolicyTransaction_DecisionSource", table: "PolicyTransaction");
            migrationBuilder.AddCheckConstraint(name: "CK_PolicyTransaction_Kind", table: "PolicyTransaction", sql: "[Kind] IN ('new-business','adjustment')");
            migrationBuilder.AddCheckConstraint(name: "CK_PolicyTransaction_DecisionSource", table: "PolicyTransaction", sql: "([Kind]='new-business' AND [CycleId] IS NOT NULL AND [QuoteRevisionId] IS NOT NULL AND [RatingId] IS NOT NULL AND [AcceptanceId] IS NOT NULL AND [ServicingDraftId] IS NULL AND [ServicingRevisionId] IS NULL AND [ServicingCycleId] IS NULL AND [ServicingRatingId] IS NULL AND [ServicingAcceptanceId] IS NULL) OR ([Kind]='adjustment' AND [CycleId] IS NULL AND [QuoteRevisionId] IS NULL AND [RatingId] IS NULL AND [AcceptanceId] IS NULL AND [ServicingDraftId] IS NOT NULL AND [ServicingRevisionId] IS NOT NULL AND [ServicingCycleId] IS NOT NULL AND [ServicingRatingId] IS NOT NULL AND [ServicingAcceptanceId] IS NOT NULL)");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyTransaction_IssueDecision",
                table: "PolicyTransaction");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyMidIntent_Purpose",
                table: "PolicyMidIntent");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PolicyDocumentRequest_Purpose",
                table: "PolicyDocumentRequest");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyTransaction_IssueDecision",
                table: "PolicyTransaction",
                sql: "([Kind]='new-business' AND [ServicingIssueDecisionId] IS NULL) OR ([Kind]='adjustment' AND [ServicingIssueDecisionId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyMidIntent_Purpose",
                table: "PolicyMidIntent",
                sql: "[Purpose]='adjustment'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PolicyDocumentRequest_Purpose",
                table: "PolicyDocumentRequest",
                sql: "[Purpose] IN ('first-issue','adjustment')");
        }
    }
}
