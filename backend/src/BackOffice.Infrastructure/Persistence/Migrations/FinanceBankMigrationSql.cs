namespace BackOffice.Infrastructure.Persistence.Migrations;

// SQL Server guards keep retained bank evidence sound even for direct SQL
// writes outside the API. Each trigger is installed by the 10-06 migration.
internal static class FinanceBankMigrationSql
{
    internal const string BankLineGuard = """
        CREATE TRIGGER TR_BankLine_Guard ON BankLine AFTER INSERT,UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted) THROW 51061,'bank-line-immutable',1;
          IF EXISTS(SELECT 1 FROM inserted i JOIN Reconciliation r WITH(UPDLOCK,HOLDLOCK)
            ON r.AgencyId=i.AgencyId AND r.[From]<=i.ValueDate AND i.ValueDate<r.[To]
            WHERE r.CompletedAt IS NOT NULL)
            THROW 51062,'reconciliation-period-completed',1;
        END
        """;

    internal const string ReconciliationGuard = """
        CREATE TRIGGER TR_Reconciliation_Guard ON Reconciliation AFTER INSERT,UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id
            WHERE i.Id IS NULL OR d.AgencyId<>i.AgencyId OR d.[From]<>i.[From] OR d.[To]<>i.[To]
              OR d.CreatedAt<>i.CreatedAt OR d.CreatedBy<>i.CreatedBy
              OR d.CompletedAt IS NOT NULL OR i.CompletedAt IS NULL
              OR i.CompletedAt<i.CreatedAt)
            THROW 51063,'reconciliation-immutable-or-invalid-transition',1;
          IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id
            WHERE d.Id IS NULL AND i.CompletedAt IS NOT NULL)
            THROW 51063,'reconciliation-immutable-or-invalid-transition',1;
          IF EXISTS(SELECT 1 FROM inserted i JOIN Reconciliation r WITH(UPDLOCK,HOLDLOCK)
            ON r.AgencyId=i.AgencyId AND r.Id<>i.Id AND r.[From]<i.[To] AND i.[From]<r.[To])
            THROW 51064,'reconciliation-window-overlap',1;
          IF EXISTS(
            SELECT 1 FROM inserted i JOIN BankLine l WITH(UPDLOCK,HOLDLOCK)
              ON l.AgencyId=i.AgencyId AND i.[From]<=l.ValueDate AND l.ValueDate<i.[To]
            OUTER APPLY(SELECT COALESCE(SUM(CASE WHEN m.ReversalOfId IS NULL THEN m.SignedAmount
              ELSE -m.SignedAmount END),0) Applied FROM ReconciliationMatch m
              WHERE m.ReconciliationId=i.Id AND m.BankLineId=l.Id) net
            OUTER APPLY(SELECT TOP(1) v.SignedResidual FROM ReconciliationVariance v
              WHERE v.ReconciliationId=i.Id AND v.BankLineId=l.Id
              ORDER BY v.ExplainedAt DESC,v.Id DESC) latest
            WHERE i.CompletedAt IS NOT NULL AND l.SignedAmount-net.Applied<>0
              AND NOT EXISTS(SELECT 1 FROM BankLineExclusion e WHERE e.ReconciliationId=i.Id AND e.BankLineId=l.Id)
              AND (latest.SignedResidual IS NULL OR latest.SignedResidual<>l.SignedAmount-net.Applied))
            THROW 51065,'variance-unexplained',1;
          IF EXISTS(
            SELECT 1 FROM inserted i JOIN FinancePosting p WITH(UPDLOCK,HOLDLOCK)
              ON p.AgencyId=i.AgencyId AND i.[From]<=p.PostingDate AND p.PostingDate<i.[To]
              AND p.SourceKind='receipt' AND p.CashDelta<>0
            OUTER APPLY(SELECT COALESCE(SUM(CASE WHEN m.ReversalOfId IS NULL THEN m.SignedAmount
              ELSE -m.SignedAmount END),0) Applied FROM ReconciliationMatch m
              WHERE m.FinancePostingId=p.Id) net
            OUTER APPLY(SELECT TOP(1) v.SignedResidual FROM ReconciliationTargetVariance v
              WHERE v.ReconciliationId=i.Id AND v.FinancePostingId=p.Id
              ORDER BY v.ExplainedAt DESC,v.Id DESC) latest
            WHERE i.CompletedAt IS NOT NULL AND p.CashDelta-net.Applied<>0
              AND (latest.SignedResidual IS NULL OR latest.SignedResidual<>p.CashDelta-net.Applied))
            THROW 51075,'cash-source-variance-unexplained',1;
        END
        """;

    internal const string MatchGuard = """
        CREATE TRIGGER TR_ReconciliationMatch_Guard ON ReconciliationMatch AFTER INSERT,UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted) THROW 51066,'bank-match-immutable',1;
          -- Hold both residual sources across the aggregate check. This also
          -- serializes two direct SQL inserts that use distinct connections.
          DECLARE @held bigint,@id uniqueidentifier;
          DECLARE line_lock CURSOR LOCAL FAST_FORWARD FOR
            SELECT DISTINCT BankLineId FROM inserted ORDER BY BankLineId;
          OPEN line_lock; FETCH NEXT FROM line_lock INTO @id;
          WHILE @@FETCH_STATUS=0 BEGIN
            SELECT @held=COUNT_BIG(*) FROM BankLine WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id;
            FETCH NEXT FROM line_lock INTO @id;
          END
          CLOSE line_lock; DEALLOCATE line_lock;
          DECLARE posting_lock CURSOR LOCAL FAST_FORWARD FOR
            SELECT DISTINCT FinancePostingId FROM inserted ORDER BY FinancePostingId;
          OPEN posting_lock; FETCH NEXT FROM posting_lock INTO @id;
          WHILE @@FETCH_STATUS=0 BEGIN
            SELECT @held=COUNT_BIG(*) FROM FinancePosting WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id;
            FETCH NEXT FROM posting_lock INTO @id;
          END
          CLOSE posting_lock; DEALLOCATE posting_lock;
          IF EXISTS(SELECT 1 FROM inserted i
            JOIN Reconciliation r ON r.Id=i.ReconciliationId
            JOIN BankLine l ON l.Id=i.BankLineId
            JOIN FinancePosting p ON p.Id=i.FinancePostingId
            LEFT JOIN ReconciliationMatch original ON original.Id=i.ReversalOfId
            WHERE r.CompletedAt IS NOT NULL OR l.AgencyId<>r.AgencyId OR p.AgencyId<>r.AgencyId
              OR l.ValueDate<r.[From] OR l.ValueDate>=r.[To]
              OR l.Currency<>p.Currency OR p.SourceKind<>'receipt' OR p.CashDelta=0
              OR SIGN(i.SignedAmount)<>SIGN(l.SignedAmount)
              OR SIGN(i.SignedAmount)<>SIGN(p.CashDelta)
              OR ABS(i.SignedAmount)>ABS(l.SignedAmount)
              OR ABS(i.SignedAmount)>ABS(p.CashDelta)
              OR EXISTS(SELECT 1 FROM Reconciliation closedSource
                WHERE closedSource.AgencyId=p.AgencyId AND closedSource.[From]<=p.PostingDate
                  AND p.PostingDate<closedSource.[To] AND closedSource.CompletedAt IS NOT NULL
                  AND i.MatchedAt<=closedSource.CompletedAt)
              OR EXISTS(SELECT 1 FROM BankLineExclusion e WHERE e.BankLineId=i.BankLineId)
              OR (i.ReversalOfId IS NOT NULL AND (original.Id IS NULL OR original.ReversalOfId IS NOT NULL
                OR original.ReconciliationId<>i.ReconciliationId OR original.BankLineId<>i.BankLineId
                OR original.FinancePostingId<>i.FinancePostingId OR original.SignedAmount<>i.SignedAmount)))
            THROW 51067,'bank-match-source-or-scope-invalid',1;
          IF EXISTS(SELECT 1 FROM (SELECT DISTINCT BankLineId FROM inserted) changed
            JOIN BankLine l ON l.Id=changed.BankLineId
            OUTER APPLY(SELECT COALESCE(SUM(CASE WHEN m.ReversalOfId IS NULL THEN m.SignedAmount
              ELSE -m.SignedAmount END),0) Applied FROM ReconciliationMatch m WITH(HOLDLOCK)
              WHERE m.BankLineId=l.Id) net
            WHERE (l.SignedAmount>0 AND (net.Applied<0 OR net.Applied>l.SignedAmount))
              OR (l.SignedAmount<0 AND (net.Applied>0 OR net.Applied<l.SignedAmount)))
            THROW 51068,'bank-line-residual-invalid',1;
          IF EXISTS(SELECT 1 FROM (SELECT DISTINCT FinancePostingId FROM inserted) changed
            JOIN FinancePosting p ON p.Id=changed.FinancePostingId
            OUTER APPLY(SELECT COALESCE(SUM(CASE WHEN m.ReversalOfId IS NULL THEN m.SignedAmount
              ELSE -m.SignedAmount END),0) Applied FROM ReconciliationMatch m WITH(HOLDLOCK)
              WHERE m.FinancePostingId=p.Id) net
            WHERE (p.CashDelta>0 AND (net.Applied<0 OR net.Applied>p.CashDelta))
              OR (p.CashDelta<0 AND (net.Applied>0 OR net.Applied<p.CashDelta)))
            THROW 51069,'cash-source-residual-invalid',1;
        END
        """;

    internal const string ExclusionGuard = """
        CREATE TRIGGER TR_BankLineExclusion_Guard ON BankLineExclusion AFTER INSERT,UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted) THROW 51070,'bank-exclusion-immutable',1;
          IF EXISTS(SELECT 1 FROM inserted i JOIN Reconciliation r ON r.Id=i.ReconciliationId
            JOIN BankLine l WITH(UPDLOCK,HOLDLOCK) ON l.Id=i.BankLineId
            JOIN BankLine candidate WITH(UPDLOCK,HOLDLOCK) ON candidate.Id=i.DuplicateOfBankLineId
            WHERE r.CompletedAt IS NOT NULL OR l.AgencyId<>r.AgencyId OR candidate.AgencyId<>r.AgencyId
              OR l.ValueDate<r.[From] OR l.ValueDate>=r.[To]
              OR l.ValueDate<>candidate.ValueDate OR l.Reference<>candidate.Reference
              OR l.SignedAmount<>candidate.SignedAmount OR l.Currency<>candidate.Currency
              OR EXISTS(SELECT 1 FROM ReconciliationMatch m WHERE m.BankLineId=l.Id)
              OR EXISTS(SELECT 1 FROM BankLineExclusion e WHERE e.BankLineId=candidate.Id))
            THROW 51071,'bank-duplicate-evidence-invalid',1;
        END
        """;

    internal const string VarianceGuard = """
        CREATE TRIGGER TR_ReconciliationVariance_Guard ON ReconciliationVariance AFTER INSERT,UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted) THROW 51072,'variance-explanation-immutable',1;
          IF EXISTS(SELECT 1 FROM inserted i JOIN Reconciliation r ON r.Id=i.ReconciliationId
            JOIN BankLine l WITH(UPDLOCK,HOLDLOCK) ON l.Id=i.BankLineId
            OUTER APPLY(SELECT COALESCE(SUM(CASE WHEN m.ReversalOfId IS NULL THEN m.SignedAmount
              ELSE -m.SignedAmount END),0) Applied FROM ReconciliationMatch m
              WHERE m.ReconciliationId=i.ReconciliationId AND m.BankLineId=i.BankLineId) net
            WHERE r.CompletedAt IS NOT NULL OR l.AgencyId<>r.AgencyId
              OR l.ValueDate<r.[From] OR l.ValueDate>=r.[To]
              OR EXISTS(SELECT 1 FROM BankLineExclusion e WHERE e.BankLineId=l.Id)
              OR i.SignedResidual<>l.SignedAmount-net.Applied)
            THROW 51073,'variance-explanation-invalid',1;
        END
        """;

    internal const string TargetVarianceGuard = """
        CREATE TRIGGER TR_ReconciliationTargetVariance_Guard ON ReconciliationTargetVariance AFTER INSERT,UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted) THROW 51076,'cash-source-variance-immutable',1;
          IF EXISTS(SELECT 1 FROM inserted i JOIN Reconciliation r ON r.Id=i.ReconciliationId
            JOIN FinancePosting p WITH(UPDLOCK,HOLDLOCK) ON p.Id=i.FinancePostingId
            OUTER APPLY(SELECT COALESCE(SUM(CASE WHEN m.ReversalOfId IS NULL THEN m.SignedAmount
              ELSE -m.SignedAmount END),0) Applied FROM ReconciliationMatch m
              WHERE m.FinancePostingId=i.FinancePostingId) net
            WHERE r.CompletedAt IS NOT NULL OR p.AgencyId<>r.AgencyId
              OR p.PostingDate<r.[From] OR p.PostingDate>=r.[To]
              OR p.SourceKind<>'receipt' OR p.CashDelta=0
              OR i.SignedResidual<>p.CashDelta-net.Applied)
            THROW 51077,'cash-source-variance-invalid',1;
        END
        """;

    internal const string PostingReconciliationGuard = """
        CREATE TRIGGER TR_FinancePosting_ReconciliationGuard ON FinancePosting AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted i JOIN Reconciliation r WITH(UPDLOCK,HOLDLOCK)
            ON r.AgencyId=i.AgencyId AND r.[From]<=i.PostingDate AND i.PostingDate<r.[To]
            WHERE i.SourceKind='receipt' AND i.CashDelta<>0 AND r.CompletedAt IS NOT NULL)
            THROW 51078,'reconciliation-period-completed',1;
        END
        """;

    internal const string BankReceiptGuard = """
        CREATE TRIGGER TR_Receipt_BankImportGuard ON Receipt AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN BankLine l WITH(UPDLOCK,HOLDLOCK)
            ON l.Id=i.OriginId WHERE i.OriginKind='bank-import' AND
            (l.Id IS NULL OR l.AgencyId<>i.AgencyId OR l.SignedAmount<>i.Amount
              OR l.SignedAmount<=0 OR l.Currency<>i.Currency OR l.ValueDate<>i.ReceivedOn
              OR l.Reference<>i.BankReference))
            THROW 51074,'receipt-bank-origin-invalid',1;
        END
        """;
}
