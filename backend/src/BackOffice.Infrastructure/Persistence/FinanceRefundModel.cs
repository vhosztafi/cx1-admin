using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureFinanceRefund(ModelBuilder model)
    {
        var rule = Record<RefundApprovalRule>(model, "RefundApprovalRule");
        rule.ToTable(t => t.UseSqlOutputClause(false));
        Text(rule, ("Code", 100));
        rule.HasIndex(x => new { x.Code, x.Version }).IsUnique();
        rule.Property(x => x.SecondApprovalThreshold).HasPrecision(15, 2);
        Check(rule, "Facts", "[SecondApprovalThreshold]>0 AND [SmallApprovalCount]=1 AND [LargeApprovalCount]=2");

        var authority = Record<RefundRoleAuthority>(model, "RefundRoleAuthority");
        authority.ToTable(t => t.UseSqlOutputClause(false));
        Text(authority, ("RoleCode", 100));
        authority.HasIndex(x => x.RoleCode).IsUnique();
        authority.Property(x => x.Limit).HasPrecision(15, 2);
        Check(authority, "Limit", "[Limit]>0");

        var request = Record<RefundRequest>(model, "RefundRequest");
        request.ToTable(t => t.UseSqlOutputClause(false));
        Text(request, ("DebtorKind", 20), ("Currency", 3), ("State", 30), ("Reason", 1000));
        request.Property(x => x.Amount).HasPrecision(15, 2);
        request.HasIndex(x => new { x.CreditObligationId, x.State });
        request.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<Policy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<IssueFinancialObligation>().WithMany().HasForeignKey(x => x.CreditObligationId).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<RefundApprovalRule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.RequestedBy).OnDelete(DeleteBehavior.NoAction);
        Check(request, "Facts", "[Amount]>0 AND [Currency]='GBP' AND [DebtorKind] IN ('agency','relationship') AND [State] IN ('building','pending','approved','rejected') AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");

        var reservation = Record<RefundCashReservation>(model, "RefundCashReservation");
        reservation.ToTable(t => t.UseSqlOutputClause(false));
        reservation.Property(x => x.Amount).HasPrecision(15, 2);
        reservation.HasIndex(x => new { x.RefundRequestId, x.AllocationId }).IsUnique();
        reservation.HasIndex(x => x.AllocationId);
        reservation.HasOne<RefundRequest>().WithMany().HasForeignKey(x => x.RefundRequestId).OnDelete(DeleteBehavior.NoAction);
        reservation.HasOne<Allocation>().WithMany().HasForeignKey(x => x.AllocationId).OnDelete(DeleteBehavior.NoAction);
        Check(reservation, "Positive", "[Amount]>0");

        var decision = Record<RefundDecision>(model, "RefundDecision");
        decision.ToTable(t => t.UseSqlOutputClause(false));
        Text(decision, ("Kind", 20), ("Reason", 1000));
        decision.Property(x => x.AuthorityLimitSnapshot).HasPrecision(15, 2);
        decision.HasIndex(x => new { x.RefundRequestId, x.ActorId }).IsUnique();
        decision.HasOne<RefundRequest>().WithMany().HasForeignKey(x => x.RefundRequestId).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<RefundApprovalRule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        Check(decision, "Facts", "[Kind] IN ('approve','reject') AND [AuthorityLimitSnapshot]>0 AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
    }
}
