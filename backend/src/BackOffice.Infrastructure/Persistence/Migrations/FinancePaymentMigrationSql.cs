namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static class FinancePaymentMigrationSql
{
    internal const string Seed = """
        IF NOT EXISTS(SELECT 1 FROM SettingVersion WHERE Scope=N'finance-refund-payment-demo' AND Version=1)
          INSERT SettingVersion(Id,Scope,Version,EffectiveFrom,[Values],CreatedAt,CreatedBy)
          VALUES('ed724838-b5d0-4b41-9c0a-4fb724e1fd3a',N'finance-refund-payment-demo',1,
            '2026-09-01T00:00:00+00:00',N'{"scenario":"success"}',SYSUTCDATETIME(),NULL);
        """;

    internal const string PaymentGuard = """
        CREATE TRIGGER TR_FinanceRefundPayment_Guard ON FinanceRefundPayment AFTER INSERT,UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id
            WHERE i.Id IS NULL OR i.RefundRequestId<>d.RefundRequestId OR i.AgencyId<>d.AgencyId
              OR i.CreditObligationId<>d.CreditObligationId OR i.DebtorKind<>d.DebtorKind
              OR i.DebtorId<>d.DebtorId OR i.Amount<>d.Amount OR i.Currency<>d.Currency
              OR i.WorkId<>d.WorkId OR i.ScenarioVersionId<>d.ScenarioVersionId
              OR i.OperationKey<>d.OperationKey OR i.RequestHash<>d.RequestHash
              OR ISNULL(i.PriorPaymentId,'00000000-0000-0000-0000-000000000000')<>
                 ISNULL(d.PriorPaymentId,'00000000-0000-0000-0000-000000000000')
              OR i.ReviewReason<>d.ReviewReason OR i.CreatedAt<>d.CreatedAt OR i.CreatedBy<>d.CreatedBy
              OR d.State IN ('paid','rejected')
              OR (d.State='failed' AND i.State<>'queued')
              OR (d.State='queued' AND i.State NOT IN ('queued','paid','rejected','failed')))
            THROW 52601,'refund-payment-identity-or-state-immutable',1;
          IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
            JOIN OutboxWork w ON w.Id=i.WorkId
            LEFT JOIN [User] actor ON actor.Id=i.CreatedBy
            WHERE d.State='failed' AND
              (w.State<>'pending' OR w.Kind<>'finance-refund-payment'
               OR w.SubjectRecordId<>i.Id OR w.OperationKey COLLATE Latin1_General_100_BIN2<>i.OperationKey
               OR actor.Id IS NULL OR actor.State<>'active' OR actor.AgencyId IS NOT NULL
               OR NOT EXISTS(SELECT 1 FROM UserRole ur JOIN Role role ON role.Id=ur.RoleId
                 WHERE ur.UserId=i.CreatedBy AND role.Code='finance' AND role.Scope='internal')
               OR EXISTS(SELECT 1 FROM DemoProviderOperation op
                 WHERE op.Kind='finance-refund-payment'
                   AND op.OperationKey COLLATE Latin1_General_100_BIN2=i.OperationKey
                   AND op.Result IS NOT NULL)
               OR EXISTS(SELECT 1 FROM FinancePosting p WHERE p.SourceKind='refund' AND p.SourceId=i.Id)))
            THROW 52609,'refund-payment-resume-invalid',1;
          IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id
            WHERE d.Id IS NULL AND i.State<>'queued')
            THROW 52601,'refund-payment-must-queue',1;
          IF EXISTS(SELECT 1 FROM inserted i
            LEFT JOIN RefundRequest r WITH(UPDLOCK,HOLDLOCK) ON r.Id=i.RefundRequestId
            LEFT JOIN OutboxWork w ON w.Id=i.WorkId
            LEFT JOIN SettingVersion setting ON setting.Id=i.ScenarioVersionId
            WHERE r.Id IS NULL OR r.State<>'approved' OR i.AgencyId<>r.AgencyId
              OR i.CreditObligationId<>r.CreditObligationId OR i.DebtorKind<>r.DebtorKind
              OR i.DebtorId<>r.DebtorId OR i.Amount<>r.Amount OR i.Currency<>r.Currency
              OR w.Id IS NULL OR w.Kind<>'finance-refund-payment' OR w.SubjectRecordId<>i.Id
              OR w.OperationKey COLLATE Latin1_General_100_BIN2<>i.OperationKey
              OR w.ScenarioVersionId<>i.ScenarioVersionId OR w.CreatedBy<>i.CreatedBy
              OR setting.Scope<>'finance-refund-payment-demo')
            THROW 52602,'refund-payment-source-or-work-invalid',1;
          IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id
            LEFT JOIN [User] actor ON actor.Id=i.CreatedBy
            WHERE d.Id IS NULL AND (actor.Id IS NULL OR actor.State<>'active' OR actor.AgencyId IS NOT NULL
              OR NOT EXISTS(SELECT 1 FROM UserRole ur JOIN Role role ON role.Id=ur.RoleId
                WHERE ur.UserId=i.CreatedBy AND role.Code='finance' AND role.Scope='internal')))
            THROW 52608,'refund-payment-queue-authority-invalid',1;
          IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id
            WHERE d.Id IS NULL AND
              ((i.PriorPaymentId IS NULL AND EXISTS(SELECT 1 FROM FinanceRefundPayment prior
                WHERE prior.RefundRequestId=i.RefundRequestId AND prior.Id<>i.Id)) OR
               (i.PriorPaymentId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM FinanceRefundPayment prior
                 WHERE prior.Id=i.PriorPaymentId AND prior.RefundRequestId=i.RefundRequestId
                   AND prior.State='rejected' AND prior.ProviderState='rejected'))))
            THROW 52603,'refund-payment-retry-must-follow-definite-rejection',1;
          IF EXISTS(SELECT 1 FROM inserted i WHERE i.ProviderState IS NOT NULL AND NOT EXISTS(
            SELECT 1 FROM DemoProviderOperation op WHERE op.Kind='finance-refund-payment'
              AND op.OperationKey COLLATE Latin1_General_100_BIN2=i.OperationKey
              AND op.ScenarioVersionId=i.ScenarioVersionId AND op.RequestHash=i.RequestHash
              AND op.Result IS NOT NULL AND op.State=i.ProviderState
              AND JSON_VALUE(op.Result,'$.state')=i.ProviderState
              AND JSON_VALUE(op.Result,'$.paymentId')=CONVERT(nvarchar(36),i.Id)
              AND JSON_VALUE(op.Result,'$.refundRequestId')=CONVERT(nvarchar(36),i.RefundRequestId)
              AND JSON_VALUE(op.Result,'$.requestHash')=CONVERT(varchar(64),i.RequestHash,2)))
            THROW 52604,'refund-payment-provider-outcome-invalid',1;
          IF EXISTS(SELECT 1 FROM inserted i WHERE
            (i.State='paid' AND ISNULL(i.ProviderState,'')<>'accepted') OR
            (i.State='rejected' AND ISNULL(i.ProviderState,'')<>'rejected'))
            THROW 52604,'refund-payment-provider-outcome-invalid',1;
          IF EXISTS(SELECT 1 FROM inserted i WHERE i.State='paid' AND NOT EXISTS(
            SELECT 1 FROM FinancePosting p WHERE p.SourceKind='refund' AND p.SourceId=i.Id
              AND p.CashDelta=-i.Amount AND p.DebtorDelta=i.Amount))
            THROW 52605,'refund-payment-cash-application-missing',1;
          IF EXISTS(SELECT 1 FROM inserted i WHERE i.State='rejected' AND EXISTS(
            SELECT 1 FROM FinancePosting p WHERE p.SourceKind='refund' AND p.SourceId=i.Id))
            THROW 52606,'rejected-refund-cannot-post-cash',1;
        END
        """;

    internal const string PostingSource = """
        CREATE TRIGGER TR_FinancePosting_RefundSource ON FinancePosting AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted p
            LEFT JOIN FinanceRefundPayment payment ON payment.Id=p.SourceId
            LEFT JOIN RefundRequest r ON r.Id=payment.RefundRequestId
            LEFT JOIN IssueFinancialObligation credit ON credit.Id=payment.CreditObligationId
            LEFT JOIN DemoProviderOperation op ON op.Kind='finance-refund-payment'
              AND op.OperationKey COLLATE Latin1_General_100_BIN2=payment.OperationKey
            WHERE p.SourceKind='refund' AND
              (payment.Id IS NULL OR r.Id IS NULL OR r.State<>'approved'
               OR payment.State<>'queued' OR payment.AgencyId<>r.AgencyId
               OR payment.ProviderState<>'accepted' OR op.Id IS NULL OR op.State<>'accepted'
               OR op.Result IS NULL OR op.RequestHash<>payment.RequestHash
               OR op.ScenarioVersionId<>payment.ScenarioVersionId
               OR JSON_VALUE(op.Result,'$.state')<>'accepted'
               OR JSON_VALUE(op.Result,'$.paymentId')<>CONVERT(nvarchar(36),payment.Id)
               OR p.AgencyId<>payment.AgencyId OR ISNULL(p.RelationshipId,'00000000-0000-0000-0000-000000000000')<>
                  ISNULL(credit.RelationshipId,'00000000-0000-0000-0000-000000000000')
               OR p.PolicyId IS NULL OR p.PolicyId<>r.PolicyId
               OR p.TransactionId IS NULL OR p.TransactionId<>credit.TransactionId
               OR p.DebtorKind<>payment.DebtorKind OR p.Currency<>payment.Currency
               OR p.DebtorDelta<>payment.Amount OR p.CashDelta<>-payment.Amount
               OR p.ProviderDelta<>0 OR p.InternalDelta<>0
               OR p.PostedAt<>payment.AppliedAt OR p.EffectiveAt<>payment.AppliedAt
               OR p.CreatedAt<>payment.AppliedAt OR p.CreatedBy IS NULL OR p.CreatedBy<>payment.CreatedBy))
            THROW 52607,'refund-posting-source-invalid',1;
        END
        """;

    internal static string ReconciliationGuard => FinanceBankMigrationSql.ReconciliationGuard
        .Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER")
        .Replace("p.SourceKind='receipt'", "p.SourceKind IN ('receipt','refund')");
    internal static string MatchGuard => FinanceBankMigrationSql.MatchGuard
        .Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER")
        .Replace("p.SourceKind<>'receipt'", "p.SourceKind NOT IN ('receipt','refund')");
    internal static string TargetVarianceGuard => FinanceBankMigrationSql.TargetVarianceGuard
        .Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER")
        .Replace("p.SourceKind<>'receipt'", "p.SourceKind NOT IN ('receipt','refund')");
    internal static string PostingReconciliationGuard => FinanceBankMigrationSql.PostingReconciliationGuard
        .Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER")
        .Replace("i.SourceKind='receipt'", "i.SourceKind IN ('receipt','refund')");
}
