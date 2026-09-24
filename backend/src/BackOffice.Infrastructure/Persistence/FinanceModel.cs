using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureFinance(ModelBuilder model)
    {
        var posting = Record<FinancePosting>(model, "FinancePosting");
        posting.ToTable(t => t.UseSqlOutputClause(false));
        Text(posting, ("SourceKind", 30), ("DebtorKind", 20), ("Currency", 3), ("Reason", 1000));
        posting.Property(x => x.DebtorDelta).HasPrecision(15, 2);
        posting.Property(x => x.ProviderDelta).HasPrecision(15, 2);
        posting.Property(x => x.CashDelta).HasPrecision(15, 2);
        posting.Property(x => x.InternalDelta).HasPrecision(15, 2);
        posting.HasIndex(x => new { x.SourceKind, x.SourceId }).IsUnique();
        posting.HasIndex(x => new { x.AgencyId, x.PostingDate, x.Id });
        posting.HasIndex(x => new { x.PolicyId, x.PostingDate });
        posting.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        posting.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x => x.RelationshipId).OnDelete(DeleteBehavior.NoAction);
        posting.HasOne<Policy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.NoAction);
        posting.HasOne<PolicyTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.NoAction);
        posting.HasOne<AccountingPeriod>().WithMany().HasForeignKey(x => x.AccountingPeriodId).OnDelete(DeleteBehavior.NoAction);
        // Allocations apply an existing receipt and never create another cash movement.
        Check(posting, "Source", "[SourceKind] IN ('receipt','receipt-reversal','refund','correction') AND [SourceId]<>'00000000-0000-0000-0000-000000000000'");
        Check(posting, "Currency", "[Currency]='GBP'");
        Check(posting, "Debtor", "[DebtorKind]='agency' OR ([DebtorKind]='relationship' AND [RelationshipId] IS NOT NULL)");
        Check(posting, "Amounts", "[DebtorDelta]<>0 OR [ProviderDelta]<>0 OR [CashDelta]<>0 OR [InternalDelta]<>0");
        // Asset debits are positive; insurer liability credits are positive.
        // InternalDelta uses the same debit-positive convention as cash/debtor.
        Check(posting, "Balance", "[DebtorDelta]-[ProviderDelta]+[CashDelta]+[InternalDelta]=0");
        Check(posting, "Reason", "LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
        Check(posting, "Posting", "[PostedAt]>=[CreatedAt]");
    }
}
