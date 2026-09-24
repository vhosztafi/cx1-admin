namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static class FinancePeriodMigrationSql
{
    internal const string SealedJournalLine = """
        CREATE TRIGGER TR_JournalLine_Sealed ON JournalLine AFTER UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted d JOIN Journal j WITH(UPDLOCK,HOLDLOCK) ON j.Id=d.JournalId
            WHERE j.PostedAt IS NOT NULL)
            THROW 52720,'sealed-journal-line-immutable',1;
        END
        """;
    internal const string PeriodClose = """
        CREATE TRIGGER TR_AccountingPeriod_FinanceClose ON AccountingPeriod AFTER INSERT,UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id
            WHERE i.Id IS NULL OR d.State='closed' OR
              (d.State='open' AND i.State NOT IN ('open','closed')))
            THROW 52700,'finance-period-sealed',1;
          IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id
            WHERE (d.Id IS NULL OR i.State='open') AND
              (i.State<>'open' OR i.ClosedAt IS NOT NULL OR i.ClosedBy IS NOT NULL OR
               i.CloseReason IS NOT NULL OR i.SourceCutoff IS NOT NULL OR i.CloseChecklistJson IS NOT NULL))
            THROW 52701,'finance-period-open-state-invalid',1;
          IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
            LEFT JOIN [User] actor ON actor.Id=i.ClosedBy
            WHERE d.State='open' AND i.State='closed' AND
              (i.ClosedAt IS NULL OR i.SourceCutoff IS NULL OR i.ClosedAt<>i.SourceCutoff OR
               i.ClosedAt<i.CreatedAt OR i.ClosedBy IS NULL OR actor.Id IS NULL OR
               actor.State<>'active' OR actor.AgencyId IS NOT NULL OR
               NOT EXISTS(SELECT 1 FROM UserRole ur JOIN Role role ON role.Id=ur.RoleId
                 WHERE ur.UserId=i.ClosedBy AND role.Code='finance' AND role.Scope='internal') OR
               LEN(TRIM(ISNULL(i.CloseReason,''))) NOT BETWEEN 10 AND 1000 OR
               ISNULL(ISJSON(i.CloseChecklistJson),0)<>1 OR
               ISNULL(JSON_VALUE(i.CloseChecklistJson,'$.reconciliation'),'')<>'complete-or-not-applicable' OR
               ISNULL(JSON_VALUE(i.CloseChecklistJson,'$.payments'),'')<>'no-acknowledged-unapplied-or-pending' OR
               ISNULL(JSON_VALUE(i.CloseChecklistJson,'$.statements'),'')<>'current-for-every-active-agency-or-not-applicable' OR
               ISNULL(JSON_VALUE(i.CloseChecklistJson,'$.bordereaux'),'')<>'valid-for-every-active-provider-or-not-applicable'))
            THROW 52702,'finance-period-close-evidence-invalid',1;
          IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
            WHERE d.State='open' AND i.State='closed' AND
              (EXISTS(SELECT 1 FROM Reconciliation r WHERE r.[From]<i.EndsOn AND i.StartsOn<r.[To]
                AND r.CompletedAt IS NULL) OR
               EXISTS(SELECT 1 FROM BankLine line WHERE line.ValueDate>=i.StartsOn AND line.ValueDate<i.EndsOn
                 AND NOT EXISTS(SELECT 1 FROM Reconciliation r WHERE r.AgencyId=line.AgencyId
                   AND r.CompletedAt IS NOT NULL AND r.[From]<=line.ValueDate AND line.ValueDate<r.[To])) OR
               EXISTS(SELECT 1 FROM FinancePosting p WHERE p.AccountingPeriodId=i.Id AND p.CashDelta<>0
                 AND NOT EXISTS(SELECT 1 FROM Reconciliation r WHERE r.AgencyId=p.AgencyId
                   AND r.CompletedAt IS NOT NULL AND r.[From]<=p.PostingDate AND p.PostingDate<r.[To])) OR
               EXISTS(SELECT 1 FROM Journal j WHERE j.AccountingPeriodId=i.Id AND j.PostedAt IS NULL) OR
               EXISTS(SELECT 1 FROM FinanceRefundPayment payment
                 JOIN RefundRequest request ON request.Id=payment.RefundRequestId
                 JOIN Journal j ON j.ObligationId=request.CreditObligationId
                 WHERE payment.State='queued' AND (j.AccountingPeriodId=i.Id OR
                   (j.AccountingPeriodId IS NULL AND j.PostedAt IS NOT NULL AND
                    CAST(j.PostedAt AT TIME ZONE 'GMT Standard Time' AS date)>=i.StartsOn AND
                    CAST(j.PostedAt AT TIME ZONE 'GMT Standard Time' AS date)<i.EndsOn))) OR
               EXISTS(SELECT 1 FROM FinanceRefundPayment payment
                 JOIN DemoProviderOperation op ON op.Kind='finance-refund-payment'
                   AND op.OperationKey COLLATE Latin1_General_100_BIN2=payment.OperationKey
                 WHERE payment.State<>'paid' AND op.State='accepted' AND op.Result IS NOT NULL)))
            THROW 52703,'finance-period-has-cash-or-payment-blockers',1;
          IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
            WHERE d.State='open' AND i.State='closed' AND
              (EXISTS(SELECT 1 FROM Journal j JOIN IssueFinancialObligation o ON o.Id=j.ObligationId
                WHERE j.PostedAt IS NOT NULL AND (j.AccountingPeriodId=i.Id OR
                  (j.AccountingPeriodId IS NULL AND
                   CAST(j.PostedAt AT TIME ZONE 'GMT Standard Time' AS date)>=i.StartsOn AND
                   CAST(j.PostedAt AT TIME ZONE 'GMT Standard Time' AS date)<i.EndsOn))
                  AND NOT EXISTS(SELECT 1 FROM FinanceStatementVersion s
                    WHERE s.AgencyId=o.AgencyId AND s.[From]<=i.StartsOn AND s.[To]>=i.EndsOn
                      AND s.SourceCutoff>=j.PostedAt)) OR
               EXISTS(SELECT 1 FROM FinancePosting p WHERE p.AccountingPeriodId=i.Id
                 AND NOT EXISTS(SELECT 1 FROM FinanceStatementVersion s
                   WHERE s.AgencyId=p.AgencyId AND s.[From]<=i.StartsOn AND s.[To]>=i.EndsOn
                     AND s.SourceCutoff>=p.PostedAt))))
            THROW 52704,'finance-period-statement-missing-or-stale',1;
          IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
            WHERE d.State='open' AND i.State='closed' AND EXISTS(
              SELECT 1 FROM Journal j JOIN IssueFinancialObligation o ON o.Id=j.ObligationId
              WHERE j.PostedAt IS NOT NULL AND (j.AccountingPeriodId=i.Id OR
                (j.AccountingPeriodId IS NULL AND
                 CAST(j.PostedAt AT TIME ZONE 'GMT Standard Time' AS date)>=i.StartsOn AND
                 CAST(j.PostedAt AT TIME ZONE 'GMT Standard Time' AS date)<i.EndsOn))
                AND NOT EXISTS(SELECT 1 FROM FinanceBordereauBatch b
                  JOIN FinanceBordereauVersion v ON v.Id=b.CurrentVersionId
                  WHERE b.ProviderId=o.ProviderId AND b.AccountingPeriodId=i.Id AND
                    v.State='valid' AND v.SourceCutoff>=j.PostedAt)))
            THROW 52705,'finance-period-bordereau-missing-or-stale',1;
        END
        """;

    internal const string CorrectionGuard = """
        CREATE TRIGGER TR_FinanceCorrection_Guard ON FinanceCorrection AFTER INSERT,UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted) THROW 52710,'finance-correction-immutable',1;
          IF EXISTS(SELECT 1 FROM inserted c LEFT JOIN [User] actor ON actor.Id=c.CreatedBy
            WHERE actor.Id IS NULL OR actor.State<>'active' OR actor.AgencyId IS NOT NULL OR
              NOT EXISTS(SELECT 1 FROM UserRole ur JOIN Role role ON role.Id=ur.RoleId
                WHERE ur.UserId=c.CreatedBy AND role.Code='finance' AND role.Scope='internal'))
            THROW 52711,'finance-correction-authority-invalid',1;
          IF EXISTS(SELECT 1 FROM inserted c
            LEFT JOIN Journal j ON c.OriginalSourceKind='insurance' AND j.Id=c.OriginalSourceId
            LEFT JOIN IssueFinancialObligation o ON o.Id=j.ObligationId
            LEFT JOIN FinancePosting p ON c.OriginalSourceKind='finance-posting' AND p.Id=c.OriginalSourceId
            LEFT JOIN AccountingPeriod ap ON
              (j.Id IS NOT NULL AND ap.StartsOn<=COALESCE(j.PostingDate,
                 CAST(j.PostedAt AT TIME ZONE 'GMT Standard Time' AS date)) AND
                 COALESCE(j.PostingDate,CAST(j.PostedAt AT TIME ZONE 'GMT Standard Time' AS date))<ap.EndsOn)
              OR (p.Id IS NOT NULL AND ap.Id=p.AccountingPeriodId)
            WHERE ap.Id IS NULL OR ap.State<>'closed' OR
              (j.Id IS NOT NULL AND (j.PostedAt IS NULL OR o.Id IS NULL OR
                c.AgencyId<>o.AgencyId OR
                ISNULL(c.RelationshipId,'00000000-0000-0000-0000-000000000000')<>
                  ISNULL(o.RelationshipId,'00000000-0000-0000-0000-000000000000') OR
                c.PolicyId<>o.PolicyId OR c.TransactionId<>o.TransactionId OR c.DebtorKind<>o.DebtorKind)) OR
              (p.Id IS NOT NULL AND (c.AgencyId<>p.AgencyId OR
                ISNULL(c.RelationshipId,'00000000-0000-0000-0000-000000000000')<>
                  ISNULL(p.RelationshipId,'00000000-0000-0000-0000-000000000000') OR
                ISNULL(c.PolicyId,'00000000-0000-0000-0000-000000000000')<>
                  ISNULL(p.PolicyId,'00000000-0000-0000-0000-000000000000') OR
                ISNULL(c.TransactionId,'00000000-0000-0000-0000-000000000000')<>
                  ISNULL(p.TransactionId,'00000000-0000-0000-0000-000000000000') OR
                c.DebtorKind<>p.DebtorKind)))
            THROW 52712,'finance-correction-original-invalid',1;
        END
        """;

    internal const string PostingSource = """
        CREATE TRIGGER TR_FinancePosting_CorrectionSource ON FinancePosting AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted p LEFT JOIN FinanceCorrection c ON
              p.SourceKind='correction' AND c.Id=p.SourceId
            LEFT JOIN Journal originalJournal ON c.OriginalSourceKind='insurance' AND
              originalJournal.Id=c.OriginalSourceId
            LEFT JOIN FinancePosting originalPosting ON c.OriginalSourceKind='finance-posting' AND
              originalPosting.Id=c.OriginalSourceId
            LEFT JOIN AccountingPeriod oldPeriod ON
              (originalPosting.Id IS NOT NULL AND oldPeriod.Id=originalPosting.AccountingPeriodId) OR
              (originalJournal.Id IS NOT NULL AND oldPeriod.StartsOn<=
                COALESCE(originalJournal.PostingDate,
                  CAST(originalJournal.PostedAt AT TIME ZONE 'GMT Standard Time' AS date)) AND
                COALESCE(originalJournal.PostingDate,
                  CAST(originalJournal.PostedAt AT TIME ZONE 'GMT Standard Time' AS date))<oldPeriod.EndsOn)
            WHERE p.SourceKind='correction' AND (c.Id IS NULL OR
              p.AgencyId<>c.AgencyId OR
              ISNULL(p.RelationshipId,'00000000-0000-0000-0000-000000000000')<>
                ISNULL(c.RelationshipId,'00000000-0000-0000-0000-000000000000') OR
              ISNULL(p.PolicyId,'00000000-0000-0000-0000-000000000000')<>
                ISNULL(c.PolicyId,'00000000-0000-0000-0000-000000000000') OR
              ISNULL(p.TransactionId,'00000000-0000-0000-0000-000000000000')<>
                ISNULL(c.TransactionId,'00000000-0000-0000-0000-000000000000') OR
              p.DebtorKind<>c.DebtorKind OR p.Reason<>c.Reason OR p.CreatedBy<>c.CreatedBy OR
              p.CreatedAt<>c.CreatedAt OR p.PostedAt<>c.CreatedAt OR
              oldPeriod.Id IS NULL OR oldPeriod.State<>'closed' OR p.PostingDate<oldPeriod.EndsOn))
            THROW 52713,'finance-correction-posting-source-invalid',1;
        END
        """;

    internal static string ReconciliationGuard => FinancePaymentMigrationSql.ReconciliationGuard
        .Replace("('receipt','refund')", "('receipt','refund','correction')");
    internal static string MatchGuard => FinancePaymentMigrationSql.MatchGuard
        .Replace("('receipt','refund')", "('receipt','refund','correction')");
    internal static string TargetVarianceGuard => FinancePaymentMigrationSql.TargetVarianceGuard
        .Replace("('receipt','refund')", "('receipt','refund','correction')");
    internal static string PostingReconciliationGuard => FinancePaymentMigrationSql.PostingReconciliationGuard
        .Replace("('receipt','refund')", "('receipt','refund','correction')");
}
