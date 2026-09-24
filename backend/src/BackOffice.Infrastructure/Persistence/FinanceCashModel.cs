using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureFinanceCash(ModelBuilder model)
    {
        var receipt = Record<Receipt>(model, "Receipt");
        receipt.ToTable(t => t.UseSqlOutputClause(false));
        Text(receipt, ("OriginKind", 30), ("BankReference", 200), ("Currency", 3));
        receipt.Property(x => x.Amount).HasPrecision(15, 2);
        receipt.HasIndex(x => new { x.OriginKind, x.OriginId }).IsUnique();
        receipt.HasIndex(x => new { x.AgencyId, x.PostingDate });
        receipt.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        receipt.HasOne<AccountingPeriod>().WithMany().HasForeignKey(x => x.AccountingPeriodId).OnDelete(DeleteBehavior.NoAction);
        Check(receipt, "Amount", "[Amount]>0 AND [Currency]='GBP'");
        Check(receipt, "Origin", "[OriginKind] IN ('manual','bank-import') AND [OriginId]<>'00000000-0000-0000-0000-000000000000'");
        Check(receipt, "Reference", "LEN(TRIM([BankReference])) BETWEEN 1 AND 200");

        var assignment = Record<ReceiptPayerAssignment>(model, "ReceiptPayerAssignment");
        assignment.ToTable(t => t.UseSqlOutputClause(false));
        Text(assignment, ("PayerKind", 20), ("Reason", 1000));
        assignment.HasIndex(x => new { x.ReceiptId, x.Ordinal }).IsUnique();
        assignment.HasIndex(x => x.OperationId).IsUnique();
        assignment.HasOne<Receipt>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.NoAction);
        assignment.HasOne<Agency>().WithMany().HasForeignKey(x => x.PayerAgencyId).OnDelete(DeleteBehavior.NoAction);
        assignment.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x => x.PayerRelationshipId).OnDelete(DeleteBehavior.NoAction);
        Check(assignment, "Payer", "([PayerKind]='unidentified' AND [PayerAgencyId] IS NULL AND [PayerRelationshipId] IS NULL) OR ([PayerKind]='agency' AND [PayerAgencyId] IS NOT NULL AND [PayerRelationshipId] IS NULL) OR ([PayerKind]='relationship' AND [PayerAgencyId] IS NULL AND [PayerRelationshipId] IS NOT NULL)");
        Check(assignment, "Reason", "LEN(TRIM([Reason])) BETWEEN 10 AND 1000 AND [Ordinal]>0");

        var allocation = Record<Allocation>(model, "Allocation");
        allocation.ToTable(t => t.UseSqlOutputClause(false));
        Text(allocation, ("Reason", 1000));
        allocation.Property(x => x.Amount).HasPrecision(15, 2);
        allocation.HasIndex(x => new { x.OperationId, x.Ordinal }).IsUnique();
        allocation.HasIndex(x => x.ReversalOfId).IsUnique().HasFilter("[ReversalOfId] IS NOT NULL");
        allocation.HasIndex(x => new { x.ReceiptId, x.ObligationId });
        allocation.HasOne<Receipt>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.NoAction);
        allocation.HasOne<IssueFinancialObligation>().WithMany().HasForeignKey(x => x.ObligationId).OnDelete(DeleteBehavior.NoAction);
        allocation.HasOne<Allocation>().WithMany().HasForeignKey(x => x.ReversalOfId).OnDelete(DeleteBehavior.NoAction);
        allocation.HasOne<AccountingPeriod>().WithMany().HasForeignKey(x => x.AccountingPeriodId).OnDelete(DeleteBehavior.NoAction);
        Check(allocation, "Amount", "[Amount]>0 AND [Ordinal]>0");
        Check(allocation, "Reason", "LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
    }
}
