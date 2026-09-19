using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CancellationPolicyGraph
{
    private static void AddCancellationClosureGuards(MigrationBuilder migration)
    {
        migration.Sql(ReplaceOnce(Alter(Before_TR_ServicingDraft_Issued),"WHERE d.State='issued' AND NOT EXISTS(",
            "WHERE d.State='issued' AND d.Kind<>'cancellation' AND NOT EXISTS("));
        migration.Sql("""
            CREATE TRIGGER TR_ServicingDraft_CancellationIssued ON ServicingDraft AFTER INSERT,UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted draft WHERE draft.State='issued' AND draft.Kind='cancellation' AND NOT EXISTS(
              SELECT 1 FROM PolicyTransaction t JOIN CancellationIssueDecision d ON d.Id=t.CancellationIssueDecisionId
                JOIN PolicyVersion v ON v.TransactionId=t.Id AND v.SliceOrdinal=1
                JOIN IssueFinancialObligation o ON o.TransactionId=t.Id AND o.Purpose='cancellation'
                JOIN Journal j ON j.ObligationId=o.Id AND j.PostedAt IS NOT NULL
                JOIN PolicyTerm term ON term.Id=t.TermId AND term.CurrentVersionId=v.Id
              WHERE t.Id=draft.IssuedTransactionId AND t.Kind='cancellation' AND d.DraftId=draft.Id AND d.RevisionId=draft.CurrentRevisionId
                AND (SELECT COUNT(*) FROM PolicyVersion versions WHERE versions.TransactionId=t.Id)=1
                AND (SELECT COUNT(*) FROM CancellationConsequence c WHERE c.TransactionId=t.Id AND c.VersionId=v.Id AND c.DecisionId=d.Id)=4
                AND NOT EXISTS(SELECT 1 FROM ServicingLease lease WHERE lease.DraftId=draft.Id AND lease.Active=1)
                AND NOT EXISTS(SELECT 1 FROM ServicingDraft conflict WITH(UPDLOCK,HOLDLOCK) WHERE conflict.PolicyId=draft.PolicyId AND conflict.Id<>draft.Id AND conflict.State='draft')))
              THROW 51834,'Issued cancellation requires one complete posted version, four durable consequences and fenced conflicting drafts.',1;
            END;
            """);
    }

    private static void RemoveGraphGuards(MigrationBuilder migration)
    {
        migration.Sql("IF EXISTS(SELECT 1 FROM PolicyTransaction WHERE Kind='cancellation') THROW 51835,'Issued cancellation history cannot be downgraded.',1;");
        foreach(var trigger in new[]{"TR_PolicyTransaction_Cancellation","TR_PolicyVersion_Cancellation","TR_IssueFinancialObligation_Cancellation",
            "TR_IssueFinancialComponent_Cancellation","TR_ServicingDraft_CancellationIssued"})migration.Sql("DROP TRIGGER "+trigger+";");
        migration.Sql(Alter(Before_TR_PolicyTransaction_Source));
        migration.Sql(Alter(Before_TR_ServicingDraft_Issued));
        migration.Sql(Alter(Before_TR_IssueFinancialObligation_Source));
        migration.Sql(Alter(Before_TR_IssueFinancialComponent_Source));
        migration.Sql(Alter(Before_ServicingExpectedPostingMovement));
    }
}
