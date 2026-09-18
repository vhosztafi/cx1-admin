using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static partial class ServicingPostingGuards
{
    private static void AddJournalGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            ALTER TRIGGER TR_JournalLine_Source ON JournalLine AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted l JOIN Journal j WITH(UPDLOCK,HOLDLOCK) ON j.Id=l.JournalId
              JOIN IssueFinancialObligation o ON o.Id=j.ObligationId JOIN IssueFinancialComponent c ON c.Id=l.SourceComponentId
              WHERE j.PostedAt IS NOT NULL OR c.ObligationId<>o.Id OR l.Debit+l.Credit<>ABS(c.Amount)
                OR l.CoverageStartsAt<>c.CoverageStartsAt OR l.CoverageEndsAt<>c.CoverageEndsAt
                OR (l.AccountCode IN ('agency-receivable','broker-remuneration-payable') AND l.PartyId<>o.AgencyId)
                OR (l.AccountCode='relationship-receivable' AND l.PartyId<>o.RelationshipId)
                OR (l.AccountCode='insurer-payable' AND l.PartyId<>o.ProviderId)
                OR l.AccountCode<>CASE WHEN (l.Debit>0 AND c.Amount>0) OR (l.Credit>0 AND c.Amount<0) THEN
                    CASE c.Code WHEN 'premium' THEN CASE o.DebtorKind WHEN 'agency' THEN 'agency-receivable' ELSE 'relationship-receivable' END
                                WHEN 'tax' THEN CASE o.DebtorKind WHEN 'agency' THEN 'agency-receivable' ELSE 'relationship-receivable' END
                                WHEN 'fee' THEN CASE o.DebtorKind WHEN 'agency' THEN 'agency-receivable' ELSE 'relationship-receivable' END
                                WHEN 'commission' THEN 'insurer-payable' ELSE 'fee-income' END
                  ELSE CASE c.Code WHEN 'premium' THEN 'insurer-payable' WHEN 'tax' THEN 'insurer-payable' WHEN 'fee' THEN 'fee-income'
                       ELSE CASE o.Settlement WHEN 'net-remittance' THEN 'agency-receivable' ELSE 'broker-remuneration-payable' END END END)
              THROW 51190,'Journal line must retain exact signed component, settlement account, owner and coverage before posting.',1;
            END;
            """);
        migration.Sql("""
            ALTER TRIGGER TR_Journal_Posting ON Journal AFTER INSERT,UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted) AND (NOT EXISTS(SELECT 1 FROM inserted) OR UPDATE(Id) OR UPDATE(TransactionId) OR UPDATE(ObligationId)
              OR UPDATE(Purpose) OR UPDATE(Currency) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy) OR UPDATE(AccountingPeriodId) OR UPDATE(PostingDate))
              THROW 51191,'Journal identity is immutable.',1;
            IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE (d.Id IS NULL AND i.PostedAt IS NOT NULL) OR d.PostedAt IS NOT NULL OR (d.Id IS NOT NULL AND i.PostedAt IS NULL))
              THROW 51191,'Journal is sealed once after its component lines are stored.',1;
            IF EXISTS(SELECT 1 FROM inserted j JOIN IssueFinancialObligation o ON o.Id=j.ObligationId JOIN PolicyTransaction t ON t.Id=j.TransactionId
              WHERE j.Purpose<>o.Purpose OR j.Currency<>o.Currency OR j.CreatedAt<>t.ProcessedAt OR (j.PostedAt IS NOT NULL AND j.PostedAt<>t.ProcessedAt))
              THROW 51191,'Journal must retain its original transaction purpose, currency and processing time.',1;
            IF EXISTS(SELECT 1 FROM inserted j JOIN PolicyTransaction t ON t.Id=j.TransactionId
              CROSS APPLY(SELECT CONVERT(date,t.ProcessedAt AT TIME ZONE 'GMT Standard Time') AS ProcessingDate) day
              LEFT JOIN AccountingPeriod p WITH(UPDLOCK,HOLDLOCK) ON p.Id=j.AccountingPeriodId
              WHERE (j.Purpose<>'first-issue' OR j.AccountingPeriodId IS NOT NULL) AND
                (p.Id IS NULL OR p.State<>'open' OR p.EndsOn<=day.ProcessingDate
                 OR j.PostingDate<>CASE WHEN day.ProcessingDate<p.StartsOn THEN p.StartsOn ELSE day.ProcessingDate END
                 OR EXISTS(SELECT 1 FROM AccountingPeriod earlier WITH(UPDLOCK,HOLDLOCK)
                   WHERE earlier.State='open' AND earlier.EndsOn>day.ProcessingDate AND earlier.StartsOn<p.StartsOn)))
              THROW 51512,'Posting requires the first eligible open accounting period held through sealing.',1;
            IF EXISTS(SELECT 1 FROM inserted j JOIN IssueFinancialObligation o WITH(UPDLOCK,HOLDLOCK) ON o.Id=j.ObligationId WHERE j.PostedAt IS NOT NULL AND
              ((SELECT COUNT(*) FROM IssueFinancialComponent c WITH(HOLDLOCK) WHERE c.ObligationId=o.Id)<>(SELECT COUNT(*) FROM ServicingExpectedPostingMovement e WHERE e.TransactionId=j.TransactionId)
                OR EXISTS(SELECT 1 FROM ServicingExpectedPostingMovement e WHERE e.TransactionId=j.TransactionId AND NOT EXISTS(
                  SELECT 1 FROM IssueFinancialComponent c WHERE c.ObligationId=o.Id AND c.Code=e.Code AND c.Ordinal=e.Ordinal AND c.Amount=e.Amount
                    AND c.CoverageStartsAt=e.CoverageStartsAt AND c.CoverageEndsAt=e.CoverageEndsAt))
                OR (SELECT COUNT(DISTINCT c.Code) FROM IssueFinancialComponent c WHERE c.ObligationId=o.Id)<>5
                OR EXISTS(SELECT 1 FROM (VALUES('premium',o.Premium),('tax',o.Tax),('fee',o.Fee),('commission',o.Commission),('fee-share',o.FeeShare)) amount(Code,Value)
                  WHERE amount.Value<>COALESCE((SELECT SUM(c.Amount) FROM IssueFinancialComponent c WHERE c.ObligationId=o.Id AND c.Code=amount.Code),0))
                OR EXISTS(SELECT 1 FROM IssueFinancialComponent c WHERE c.ObligationId=o.Id AND (SELECT COUNT(*) FROM JournalLine l WHERE l.JournalId=j.Id AND l.SourceComponentId=c.Id)<>CASE WHEN c.Amount<>0 THEN 2 ELSE 0 END)
                OR COALESCE((SELECT SUM(l.Debit-l.Credit) FROM JournalLine l WHERE l.JournalId=j.Id),0)<>0
                OR COALESCE((SELECT SUM(l.Debit-l.Credit) FROM JournalLine l WHERE l.JournalId=j.Id AND l.AccountCode IN ('agency-receivable','relationship-receivable')),0)<>o.InvoiceDue
                OR COALESCE((SELECT SUM(l.Credit-l.Debit) FROM JournalLine l WHERE l.JournalId=j.Id AND l.AccountCode='broker-remuneration-payable'),0)<>o.BrokerPayable))
              THROW 51192,'Posting requires every expected signed component, balanced amounts and the exact debtor obligation.',1;
            END;
            """);
    }

    internal static void Down(MigrationBuilder migration)
    {
        migration.Sql("""
            IF EXISTS(SELECT 1 FROM PolicyTransaction WHERE Kind<>'new-business')
              THROW 51513,'Servicing issued history cannot be downgraded.',1;
            DROP TRIGGER TR_IssueFinancialObligation_Source;
            DROP TRIGGER TR_IssueFinancialComponent_Source;
            DROP TRIGGER TR_JournalLine_Source;
            DROP TRIGGER TR_Journal_Posting;
            DROP VIEW ServicingExpectedPostingMovement;
            """);
        FirstPolicyIssueStorage.RestoreFirstIssuePostingGuards(migration);
        migration.Sql("""
            ALTER TRIGGER TR_PolicyTransaction_Source ON PolicyTransaction AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN QuoteAcceptance a ON a.Id=i.AcceptanceId JOIN UnderwritingCycle c ON c.Id=i.CycleId JOIN PolicyTerm t ON t.Id=i.TermId
              WHERE a.RatingId<>i.RatingId OR c.QuoteRevisionId<>i.QuoteRevisionId OR c.ProductVersionId<>t.ProductVersionId OR c.StartsAt<>t.StartsAt OR c.EndsAt<>t.EndsAt OR i.EffectiveAt<>t.StartsAt)
              THROW 51183,'First issue must retain exact accepted rating, revision and term.',1;
            END;
            """);
    }
}

// Reuses the original definitions for empty-schema rollback without changing
// any historical migration's Up behavior or rebuilding retained ledger rows.
public partial class FirstPolicyIssueStorage
{
    internal static void RestoreFirstIssuePostingGuards(MigrationBuilder migration) => AddIssuePostingGuards(migration);
}
