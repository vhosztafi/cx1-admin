using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class FirstPolicyIssueStorage
{
    private static void AddIssuePostingGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_IssueFinancialObligation_Source ON IssueFinancialObligation AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted o JOIN Policy p ON p.Id=o.PolicyId JOIN PolicyTransaction t ON t.Id=o.TransactionId
              JOIN UnderwritingCycle c ON c.Id=t.CycleId JOIN BinderVersion b ON b.Id=c.BinderVersionId JOIN QuoteRatingResult r ON r.Id=t.RatingId JOIN AgencyTermsVersion a ON a.Id=c.AgencyTermsVersionId
              WHERE o.AgencyId<>p.AgencyId OR o.ClientId<>p.ClientId OR o.RelationshipId<>p.RelationshipId OR o.ProviderId<>b.ProviderId OR o.AgencyTermsVersionId<>c.AgencyTermsVersionId
                OR o.Premium<>r.TermPremium OR o.Tax<>r.Tax OR o.Fee<>r.Fee OR o.Commission<>r.BrokerCommission
                OR CONVERT(varbinary(max),o.TermsSnapshotJson)<>CONVERT(varbinary(max),a.Snapshot)
                OR COALESCE(JSON_VALUE(a.Snapshot,'$.settlement.premiumCollection'),'') NOT IN ('agency','mga')
                OR COALESCE(JSON_VALUE(a.Snapshot,'$.settlement.commissionSettlement'),'') NOT IN ('net-remittance','separate-payment')
                OR o.DebtorKind<>CASE WHEN JSON_VALUE(a.Snapshot,'$.settlement.premiumCollection')='agency' THEN 'agency' ELSE 'relationship' END
                OR o.Settlement<>CASE WHEN JSON_VALUE(a.Snapshot,'$.settlement.premiumCollection')='agency' AND JSON_VALUE(a.Snapshot,'$.settlement.commissionSettlement')='net-remittance' THEN 'net-remittance' ELSE 'separate-payment' END
                OR o.FeeShare<>ROUND(o.Fee*CASE WHEN JSON_VALUE(a.Snapshot,'$.commercialTerms.feeSharing')='agreed-split' THEN COALESCE(TRY_CONVERT(decimal(9,4),JSON_VALUE(a.Snapshot,'$.commercialTerms.feeShareBasisPoints')),-1) ELSE 0 END/10000,2))
              THROW 51188,'Issue amounts, debtor and settlement must retain their actual rating and agreed terms.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_IssueFinancialComponent_Source ON IssueFinancialComponent AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted c JOIN IssueFinancialObligation o ON o.Id=c.ObligationId JOIN PolicyTerm t ON t.Id=o.TermId
              WHERE c.Amount<>CASE c.Code WHEN 'premium' THEN o.Premium WHEN 'tax' THEN o.Tax WHEN 'fee' THEN o.Fee WHEN 'commission' THEN o.Commission ELSE o.FeeShare END
                OR c.CoverageStartsAt<>t.StartsAt OR c.CoverageEndsAt<>t.EndsAt
                OR EXISTS(SELECT 1 FROM Journal j WITH(UPDLOCK,HOLDLOCK) WHERE j.ObligationId=o.Id AND j.PostedAt IS NOT NULL))
              THROW 51189,'Original component amount and coverage must agree before posting.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_JournalLine_Source ON JournalLine AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted l JOIN Journal j WITH(UPDLOCK,HOLDLOCK) ON j.Id=l.JournalId
              JOIN IssueFinancialObligation o ON o.Id=j.ObligationId JOIN IssueFinancialComponent c ON c.Id=l.SourceComponentId
              WHERE j.PostedAt IS NOT NULL OR c.ObligationId<>o.Id OR l.Debit+l.Credit<>c.Amount
                OR l.CoverageStartsAt<>c.CoverageStartsAt OR l.CoverageEndsAt<>c.CoverageEndsAt
                OR (l.AccountCode IN ('agency-receivable','broker-remuneration-payable') AND l.PartyId<>o.AgencyId)
                OR (l.AccountCode='relationship-receivable' AND l.PartyId<>o.RelationshipId)
                OR (l.AccountCode='insurer-payable' AND l.PartyId<>o.ProviderId)
                OR l.AccountCode<>CASE WHEN l.Debit>0 THEN
                    CASE c.Code WHEN 'premium' THEN CASE o.DebtorKind WHEN 'agency' THEN 'agency-receivable' ELSE 'relationship-receivable' END
                                WHEN 'tax' THEN CASE o.DebtorKind WHEN 'agency' THEN 'agency-receivable' ELSE 'relationship-receivable' END
                                WHEN 'fee' THEN CASE o.DebtorKind WHEN 'agency' THEN 'agency-receivable' ELSE 'relationship-receivable' END
                                WHEN 'commission' THEN 'insurer-payable' ELSE 'fee-income' END
                  ELSE CASE c.Code WHEN 'premium' THEN 'insurer-payable' WHEN 'tax' THEN 'insurer-payable' WHEN 'fee' THEN 'fee-income'
                       ELSE CASE o.Settlement WHEN 'net-remittance' THEN 'agency-receivable' ELSE 'broker-remuneration-payable' END END END)
              THROW 51190,'Journal line must retain exact component, settlement account, owner and coverage before posting.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_Journal_Posting ON Journal AFTER INSERT,UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted) AND (NOT EXISTS(SELECT 1 FROM inserted) OR UPDATE(Id) OR UPDATE(TransactionId) OR UPDATE(ObligationId) OR UPDATE(Purpose) OR UPDATE(Currency) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy))
              THROW 51191,'Journal identity is immutable.',1;
            IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE (d.Id IS NULL AND i.PostedAt IS NOT NULL) OR d.PostedAt IS NOT NULL OR (d.Id IS NOT NULL AND i.PostedAt IS NULL))
              THROW 51191,'Journal is sealed once after its component lines are stored.',1;
            IF EXISTS(SELECT 1 FROM inserted j JOIN IssueFinancialObligation o ON o.Id=j.ObligationId WHERE j.PostedAt IS NOT NULL AND
              ((SELECT COUNT(*) FROM IssueFinancialComponent c WHERE c.ObligationId=o.Id)<>5
                OR EXISTS(SELECT 1 FROM IssueFinancialComponent c WHERE c.ObligationId=o.Id AND (SELECT COUNT(*) FROM JournalLine l WHERE l.JournalId=j.Id AND l.SourceComponentId=c.Id)<>CASE WHEN c.Amount>0 THEN 2 ELSE 0 END)
                OR COALESCE((SELECT SUM(l.Debit-l.Credit) FROM JournalLine l WHERE l.JournalId=j.Id),0)<>0
                OR COALESCE((SELECT SUM(l.Debit-l.Credit) FROM JournalLine l WHERE l.JournalId=j.Id AND l.AccountCode IN ('agency-receivable','relationship-receivable')),0)<>o.InvoiceDue
                OR COALESCE((SELECT SUM(l.Credit-l.Debit) FROM JournalLine l WHERE l.JournalId=j.Id AND l.AccountCode='broker-remuneration-payable'),0)<>o.BrokerPayable))
              THROW 51192,'Posting requires every original component, balanced amounts and the exact debtor obligation.',1;
            END;
            """);
    }
}
