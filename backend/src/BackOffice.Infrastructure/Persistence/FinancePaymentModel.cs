using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureFinancePayment(ModelBuilder model)
    {
        var payment = Record<FinanceRefundPayment>(model, "FinanceRefundPayment");
        payment.ToTable(t => t.UseSqlOutputClause(false));
        Text(payment, ("DebtorKind", 20), ("Currency", 3), ("OperationKey", 200),
            ("ReviewReason", 1000), ("State", 30), ("ProviderState", 30), ("ProviderEventId", 200));
        Hash(payment, "RequestHash");
        payment.Property(x => x.Amount).HasPrecision(15, 2);
        payment.HasIndex(x => x.WorkId).IsUnique();
        payment.HasIndex(x => x.OperationKey).IsUnique();
        payment.HasIndex(x => x.PriorPaymentId).IsUnique().HasFilter("[PriorPaymentId] IS NOT NULL");
        payment.HasIndex(x => x.RefundRequestId).IsUnique().HasFilter("[State]<>'rejected'");
        payment.HasOne<RefundRequest>().WithMany().HasForeignKey(x => x.RefundRequestId).OnDelete(DeleteBehavior.NoAction);
        payment.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        payment.HasOne<IssueFinancialObligation>().WithMany().HasForeignKey(x => x.CreditObligationId).OnDelete(DeleteBehavior.NoAction);
        payment.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        payment.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        payment.HasOne<FinanceRefundPayment>().WithMany().HasForeignKey(x => x.PriorPaymentId).OnDelete(DeleteBehavior.NoAction);
        Check(payment, "Facts", "[Amount]>0 AND [Currency]='GBP' AND [DebtorKind] IN ('agency','relationship') AND [State] IN ('queued','paid','rejected','failed') AND LEN(TRIM([ReviewReason])) BETWEEN 10 AND 1000");
        Check(payment, "Provider", "([ProviderState] IS NULL AND [ProviderOperationId] IS NULL AND [ProviderEventId] IS NULL AND [AppliedAt] IS NULL) OR ([ProviderState] IN ('accepted','rejected') AND [ProviderOperationId] IS NOT NULL AND [ProviderEventId] IS NOT NULL AND [AppliedAt] IS NOT NULL)");
    }
}
