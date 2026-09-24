namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static class FinanceRefundMigrationSql
{
    internal const string Seed = """
        INSERT RefundApprovalRule(Id,Code,Version,SecondApprovalThreshold,SmallApprovalCount,LargeApprovalCount,CreatedAt)
        VALUES('b235310a-5be1-4a83-b779-d34b4d8bfb44','finance-refund-rule-v1',1,250.00,1,2,SYSUTCDATETIME());
        INSERT RefundRoleAuthority(Id,RoleCode,[Limit],Active,CreatedAt,UpdatedAt)
        VALUES('7d7263a5-6d1a-4ac2-a17e-d86ca13f45ba','finance',10000.00,1,SYSUTCDATETIME(),SYSUTCDATETIME());
        """;

    internal const string RuleGuard = """
        CREATE TRIGGER TR_RefundApprovalRule_Immutable ON RefundApprovalRule AFTER UPDATE,DELETE AS BEGIN
          THROW 51100,'refund-approval-rule-immutable',1;
        END
        """;

    internal const string ReservationGuard = """
        CREATE TRIGGER TR_RefundCashReservation_Guard ON RefundCashReservation AFTER INSERT,UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted) THROW 51101,'refund-reservation-immutable',1;
          DECLARE @id uniqueidentifier,@held bigint;
          DECLARE source_lock CURSOR LOCAL FAST_FORWARD FOR SELECT DISTINCT AllocationId FROM inserted ORDER BY AllocationId;
          OPEN source_lock; FETCH NEXT FROM source_lock INTO @id;
          WHILE @@FETCH_STATUS=0 BEGIN
            SELECT @held=COUNT_BIG(*) FROM Allocation WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id;
            FETCH NEXT FROM source_lock INTO @id;
          END
          CLOSE source_lock; DEALLOCATE source_lock;
          IF EXISTS(SELECT 1 FROM inserted i
            JOIN RefundRequest r ON r.Id=i.RefundRequestId
            JOIN Allocation a ON a.Id=i.AllocationId
            JOIN Receipt cash ON cash.Id=a.ReceiptId
            JOIN IssueFinancialObligation invoice ON invoice.Id=a.ObligationId
            OUTER APPLY(SELECT TOP(1) p.PayerKind,p.PayerAgencyId,p.PayerRelationshipId
              FROM ReceiptPayerAssignment p WHERE p.ReceiptId=cash.Id ORDER BY p.Ordinal DESC) payer
            WHERE r.State<>'building' OR a.ReversalOfId IS NOT NULL
              OR EXISTS(SELECT 1 FROM Allocation reversal WHERE reversal.ReversalOfId=a.Id)
              OR invoice.InvoiceDue<=0 OR invoice.PolicyId<>r.PolicyId
              OR invoice.AgencyId<>r.AgencyId OR invoice.DebtorKind<>r.DebtorKind
              OR (CASE WHEN invoice.DebtorKind='agency' THEN invoice.DebtorAgencyId ELSE invoice.DebtorRelationshipId END)<>r.DebtorId
              OR cash.AgencyId<>r.AgencyId OR cash.Currency<>r.Currency
              OR payer.PayerKind<>r.DebtorKind
              OR (CASE WHEN payer.PayerKind='agency' THEN payer.PayerAgencyId ELSE payer.PayerRelationshipId END)<>r.DebtorId)
            THROW 51102,'refund-source-payee-invalid',1;
          IF EXISTS(SELECT 1 FROM (SELECT DISTINCT AllocationId FROM inserted) changed
            JOIN Allocation a ON a.Id=changed.AllocationId
            OUTER APPLY(SELECT COALESCE(SUM(s.Amount),0) Used FROM RefundCashReservation s WITH(HOLDLOCK)
              JOIN RefundRequest r ON r.Id=s.RefundRequestId WHERE s.AllocationId=a.Id AND r.State<>'rejected') totals
            WHERE totals.Used>a.Amount)
            THROW 51103,'refund-source-overreserved',1;
          IF EXISTS(SELECT 1 FROM (SELECT DISTINCT RefundRequestId FROM inserted) changed
            JOIN RefundRequest r ON r.Id=changed.RefundRequestId
            OUTER APPLY(SELECT COALESCE(SUM(s.Amount),0) Used FROM RefundCashReservation s WITH(HOLDLOCK)
              WHERE s.RefundRequestId=r.Id) totals WHERE totals.Used>r.Amount)
            THROW 51104,'refund-request-source-total-invalid',1;
        END
        """;

    internal const string RequestGuard = """
        CREATE TRIGGER TR_RefundRequest_Guard ON RefundRequest AFTER INSERT,UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id
            WHERE i.Id IS NULL OR d.AgencyId<>i.AgencyId OR d.PolicyId<>i.PolicyId
              OR d.CreditObligationId<>i.CreditObligationId OR d.DebtorKind<>i.DebtorKind
              OR d.DebtorId<>i.DebtorId OR d.Currency<>i.Currency OR d.Amount<>i.Amount
              OR d.RuleId<>i.RuleId OR d.RequestedBy<>i.RequestedBy OR d.RequestedAt<>i.RequestedAt
              OR d.Reason<>i.Reason OR d.CreatedAt<>i.CreatedAt OR d.CreatedBy<>i.CreatedBy
              OR (d.State='building' AND i.State NOT IN ('building','pending'))
              OR (d.State='pending' AND i.State NOT IN ('pending','approved','rejected'))
              OR d.State IN ('approved','rejected'))
            THROW 51105,'refund-request-invalid-transition',1;
          IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id
            WHERE d.Id IS NULL AND i.State<>'building')
            THROW 51105,'refund-request-invalid-transition',1;
          IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id
            WHERE d.Id IS NULL AND (i.CreatedBy<>i.RequestedBy OR NOT EXISTS(
              SELECT 1 FROM [User] actor JOIN UserRole ur ON ur.UserId=actor.Id
                JOIN Role role ON role.Id=ur.RoleId
              WHERE actor.Id=i.RequestedBy AND actor.State='active' AND actor.AgencyId IS NULL
                AND role.Code='finance' AND role.Scope='internal')))
            THROW 51114,'refund-request-authority-invalid',1;
          IF EXISTS(SELECT 1 FROM inserted i
            JOIN IssueFinancialObligation c WITH(UPDLOCK,HOLDLOCK) ON c.Id=i.CreditObligationId
            LEFT JOIN Journal j ON j.ObligationId=c.Id AND j.PostedAt IS NOT NULL
            WHERE i.AgencyId<>c.AgencyId OR i.PolicyId<>c.PolicyId OR i.Currency<>c.Currency
              OR c.Purpose<>'cancellation' OR c.InvoiceDue>=0 OR j.Id IS NULL
              OR i.DebtorKind<>c.DebtorKind OR i.DebtorId<>
                CASE WHEN c.DebtorKind='agency' THEN c.DebtorAgencyId ELSE c.DebtorRelationshipId END)
            THROW 51106,'refund-credit-source-invalid',1;
          IF EXISTS(SELECT 1 FROM inserted i
            OUTER APPLY(SELECT COALESCE(SUM(s.Amount),0) Used FROM RefundCashReservation s
              WHERE s.RefundRequestId=i.Id) sources
            WHERE i.State<>'building' AND sources.Used<>i.Amount)
            THROW 51107,'refund-reservation-total-invalid',1;
          IF EXISTS(SELECT 1 FROM inserted i WHERE i.State='approved' AND
            (SELECT COUNT(DISTINCT d.ActorId) FROM RefundDecision d
              WHERE d.RefundRequestId=i.Id AND d.Kind='approve') <
            (SELECT CASE WHEN i.Amount<=approvalRule.SecondApprovalThreshold THEN approvalRule.SmallApprovalCount
              ELSE approvalRule.LargeApprovalCount END FROM RefundApprovalRule approvalRule WHERE approvalRule.Id=i.RuleId))
            THROW 51108,'refund-approval-incomplete',1;
          IF EXISTS(SELECT 1 FROM inserted i WHERE i.State='rejected' AND NOT EXISTS(
            SELECT 1 FROM RefundDecision d WHERE d.RefundRequestId=i.Id AND d.Kind='reject'))
            THROW 51109,'refund-rejection-unaudited',1;
          IF EXISTS(SELECT 1 FROM inserted i
            JOIN IssueFinancialObligation c ON c.Id=i.CreditObligationId
            JOIN Journal journal ON journal.ObligationId=c.Id AND journal.PostedAt IS NOT NULL
            OUTER APPLY(SELECT COALESCE(SUM(o.InvoiceDue),0) Due FROM IssueFinancialObligation o
              JOIN Journal posted ON posted.ObligationId=o.Id AND posted.PostedAt IS NOT NULL
              WHERE o.PolicyId=c.PolicyId AND o.AgencyId=c.AgencyId AND o.DebtorKind=c.DebtorKind
                AND CASE WHEN o.DebtorKind='agency' THEN o.DebtorAgencyId ELSE o.DebtorRelationshipId END=i.DebtorId
                AND o.InvoiceDue>0) invoices
            OUTER APPLY(SELECT COALESCE(SUM(CASE WHEN a.ReversalOfId IS NULL THEN a.Amount ELSE -a.Amount END),0) Paid
              FROM Allocation a JOIN IssueFinancialObligation o ON o.Id=a.ObligationId
              WHERE o.PolicyId=c.PolicyId AND o.AgencyId=c.AgencyId AND o.DebtorKind=c.DebtorKind
                AND CASE WHEN o.DebtorKind='agency' THEN o.DebtorAgencyId ELSE o.DebtorRelationshipId END=i.DebtorId
                AND o.InvoiceDue>0) collected
            OUTER APPLY(SELECT COALESCE(SUM(-prior.InvoiceDue),0) Earlier FROM IssueFinancialObligation prior
              JOIN Journal pj ON pj.ObligationId=prior.Id AND pj.PostedAt IS NOT NULL
              WHERE prior.PolicyId=c.PolicyId AND prior.AgencyId=c.AgencyId AND prior.DebtorKind=c.DebtorKind
                AND CASE WHEN prior.DebtorKind='agency' THEN prior.DebtorAgencyId ELSE prior.DebtorRelationshipId END=i.DebtorId
                AND prior.InvoiceDue<0 AND (pj.PostedAt<journal.PostedAt OR pj.PostedAt=journal.PostedAt AND prior.Id<c.Id)) priorCredit
            OUTER APPLY(SELECT COALESCE(SUM(r.Amount),0) Reserved FROM RefundRequest r WITH(HOLDLOCK)
              WHERE r.CreditObligationId=c.Id AND r.State<>'rejected') total
            CROSS APPLY(SELECT CASE WHEN invoices.Due-collected.Paid>0 THEN invoices.Due-collected.Paid ELSE 0 END Outstanding) debt
            CROSS APPLY(SELECT CASE WHEN debt.Outstanding-priorCredit.Earlier>0 THEN debt.Outstanding-priorCredit.Earlier ELSE 0 END Unoffset) remainder
            CROSS APPLY(SELECT CASE WHEN -c.InvoiceDue-remainder.Unoffset>0 THEN -c.InvoiceDue-remainder.Unoffset ELSE 0 END CreditAfterDebt) refundable
            WHERE i.State<>'rejected' AND (total.Reserved>refundable.CreditAfterDebt OR total.Reserved>collected.Paid))
            THROW 51110,'refund-not-collected',1;
        END
        """;

    internal const string DecisionGuard = """
        CREATE TRIGGER TR_RefundDecision_Guard ON RefundDecision AFTER INSERT,UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted) THROW 51111,'refund-decision-immutable',1;
          IF EXISTS(SELECT 1 FROM inserted i JOIN RefundRequest r WITH(UPDLOCK,HOLDLOCK) ON r.Id=i.RefundRequestId
            JOIN [User] actor ON actor.Id=i.ActorId
            JOIN RefundRoleAuthority authority ON authority.RoleCode='finance'
            WHERE r.State<>'pending' OR i.RuleId<>r.RuleId OR i.ActorId=r.RequestedBy
              OR actor.State<>'active' OR actor.AgencyId IS NOT NULL
              OR NOT EXISTS(SELECT 1 FROM UserRole ur JOIN Role role ON role.Id=ur.RoleId
                WHERE ur.UserId=i.ActorId AND role.Code='finance' AND role.Scope='internal')
              OR authority.Active=0 OR authority.[Limit]<r.Amount
              OR i.AuthorityLimitSnapshot<>authority.[Limit])
            THROW 51112,'refund-decision-authority-invalid',1;
        END
        """;

    internal const string AllocationGuard = """
        CREATE TRIGGER TR_Allocation_RefundReservationGuard ON Allocation AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted i JOIN RefundCashReservation s WITH(UPDLOCK,HOLDLOCK)
            ON s.AllocationId=i.ReversalOfId JOIN RefundRequest r ON r.Id=s.RefundRequestId
            WHERE i.ReversalOfId IS NOT NULL AND r.State<>'rejected')
            THROW 51113,'reserved-allocation-cannot-reverse',1;
        END
        """;
}
