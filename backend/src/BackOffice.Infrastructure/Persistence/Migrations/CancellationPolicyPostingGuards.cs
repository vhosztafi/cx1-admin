using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CancellationPolicyGraph
{
    private static void AddCancellationPostingGuards(MigrationBuilder migration)
    {
        migration.Sql(Alter(Before_ServicingExpectedPostingMovement).TrimEnd().TrimEnd(';')+"""

            UNION ALL
            SELECT t.Id,e.Code,e.Ordinal,e.Amount,e.CoverageStartsAt,e.CoverageEndsAt
              FROM PolicyTransaction t JOIN CancellationExpectedReturnMovement e ON e.DecisionId=t.CancellationIssueDecisionId
              WHERE t.Kind='cancellation';
            """);
        migration.Sql(ReplaceOnce(Alter(Before_TR_IssueFinancialObligation_Source),"FROM inserted o", "FROM (SELECT * FROM inserted WHERE Purpose<>'cancellation') o"));
        migration.Sql(ReplaceOnce(Alter(Before_TR_IssueFinancialComponent_Source),"WHERE c.OriginalComponentId IS NOT NULL",
            "WHERE (o.Purpose<>'cancellation' AND c.OriginalComponentId IS NOT NULL)"));
        migration.Sql("""
            CREATE TRIGGER TR_IssueFinancialObligation_Cancellation ON IssueFinancialObligation AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted o JOIN PolicyTransaction t ON t.Id=o.TransactionId
              LEFT JOIN CancellationIssueDecision d ON d.Id=t.CancellationIssueDecisionId
              OUTER APPLY(SELECT TOP(1) source.* FROM IssueFinancialObligation source JOIN PolicyTransaction st ON st.Id=source.TransactionId
                WHERE source.PolicyId=t.PolicyId AND source.TermId=t.TermId AND source.Purpose<>'cancellation' ORDER BY st.Sequence,source.Id) basis
              OUTER APPLY(SELECT SUM(CASE WHEN e.Code='premium' THEN e.Amount ELSE 0 END) AS Premium,
                SUM(CASE WHEN e.Code='tax' THEN e.Amount ELSE 0 END) AS Tax,
                SUM(CASE WHEN e.Code='commission' THEN e.Amount ELSE 0 END) AS Commission,COUNT(*) AS Components
                FROM CancellationExpectedReturnMovement e WHERE e.DecisionId=d.Id) amounts
              WHERE (o.Purpose='cancellation' OR t.Kind='cancellation') AND
                (o.Purpose<>'cancellation' OR t.Kind<>'cancellation' OR d.Id IS NULL OR basis.Id IS NULL OR amounts.Components<5
                 OR o.PolicyId<>t.PolicyId OR o.TermId<>t.TermId OR o.CreatedAt<>t.ProcessedAt OR o.CreatedBy IS NULL OR o.CreatedBy<>d.ActorId
                 OR o.AgencyId<>basis.AgencyId OR o.ClientId<>basis.ClientId OR o.RelationshipId<>basis.RelationshipId OR o.ProviderId<>basis.ProviderId
                 OR o.AgencyTermsVersionId<>basis.AgencyTermsVersionId OR CONVERT(varbinary(max),o.TermsSnapshotJson)<>CONVERT(varbinary(max),basis.TermsSnapshotJson)
                 OR o.Currency<>basis.Currency OR o.DebtorKind<>basis.DebtorKind OR o.Settlement<>basis.Settlement
                 OR ISNULL(o.DebtorAgencyId,'00000000-0000-0000-0000-000000000000')<>ISNULL(basis.DebtorAgencyId,'00000000-0000-0000-0000-000000000000')
                 OR ISNULL(o.DebtorRelationshipId,'00000000-0000-0000-0000-000000000000')<>ISNULL(basis.DebtorRelationshipId,'00000000-0000-0000-0000-000000000000')
                 OR o.Premium<>amounts.Premium OR o.Tax<>amounts.Tax OR o.Commission<>amounts.Commission OR o.Fee<>0 OR o.FeeShare<>0
                 OR o.InvoiceDue<>CASE WHEN o.Settlement='net-remittance' THEN o.NetDue ELSE o.GrossDue END
                 OR o.BrokerPayable<>CASE WHEN o.Settlement='net-remittance' THEN 0 ELSE o.Commission END
                 OR EXISTS(SELECT 1 FROM CancellationExpectedReturnMovement e WHERE e.DecisionId=d.Id AND e.Amount IS NULL)
                 OR EXISTS(SELECT 1 FROM IssueFinancialObligation prior WHERE prior.PolicyId=t.PolicyId AND prior.TermId=t.TermId AND prior.Purpose<>'cancellation'
                    AND (prior.AgencyId<>basis.AgencyId OR prior.ClientId<>basis.ClientId OR prior.RelationshipId<>basis.RelationshipId OR prior.ProviderId<>basis.ProviderId
                      OR prior.Currency<>basis.Currency OR prior.DebtorKind<>basis.DebtorKind OR prior.Settlement<>basis.Settlement))))
              THROW 51832,'Cancellation obligation must equal independently calculated original ledger returns and original settlement parties.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_IssueFinancialComponent_Cancellation ON IssueFinancialComponent AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted c JOIN IssueFinancialObligation o ON o.Id=c.ObligationId
              JOIN PolicyTransaction t ON t.Id=c.TransactionId
              WHERE o.Purpose='cancellation' AND (c.OriginalComponentId IS NULL OR NOT EXISTS(
                SELECT 1 FROM CancellationExpectedReturnMovement e JOIN IssueFinancialComponent original ON original.Id=e.OriginalComponentId
                WHERE e.DecisionId=t.CancellationIssueDecisionId AND original.OriginalComponentId IS NULL
                  AND e.OriginalComponentId=c.OriginalComponentId AND e.Code=c.Code AND e.Ordinal=c.Ordinal AND e.Amount=c.Amount
                  AND e.CoverageStartsAt=c.CoverageStartsAt AND e.CoverageEndsAt=c.CoverageEndsAt)))
              THROW 51833,'Cancellation component must return its exact original component once.',1;
            END;
            """);
    }
}
