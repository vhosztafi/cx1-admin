using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureFinanceBank(ModelBuilder model)
    {
        var line = Record<BankLine>(model, "BankLine");
        line.ToTable(t => t.UseSqlOutputClause(false));
        Text(line, ("ImportKey", 200), ("Reference", 200), ("Currency", 3));
        line.Property(x => x.ImportKey).UseCollation("Latin1_General_100_BIN2");
        line.Property(x => x.SignedAmount).HasPrecision(15, 2);
        Hash(line, "RawHash"); Json(line, "RawJson");
        line.HasIndex(x => new { x.AgencyId, x.ImportKey }).IsUnique();
        line.HasIndex(x => new { x.AgencyId, x.ValueDate, x.Reference, x.SignedAmount });
        line.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        Check(line, "Facts", "[SignedAmount]<>0 AND [Currency]='GBP' AND LEN(TRIM([Reference])) BETWEEN 1 AND 200 AND LEN(TRIM([ImportKey])) BETWEEN 1 AND 200");
        Check(line, "RawHash", "[RawHash]=HASHBYTES('SHA2_256',CONVERT(varbinary(max),[RawJson]))");

        var reconciliation = Record<Reconciliation>(model, "Reconciliation");
        reconciliation.ToTable(t => t.UseSqlOutputClause(false));
        reconciliation.HasIndex(x => new { x.AgencyId, x.From, x.To }).IsUnique();
        reconciliation.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        reconciliation.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.CompletedBy).OnDelete(DeleteBehavior.NoAction);
        Check(reconciliation, "Window", "[From]<[To] AND ([CompletedAt] IS NULL AND [CompletedBy] IS NULL OR [CompletedAt] IS NOT NULL AND [CompletedBy] IS NOT NULL)");

        var match = Record<ReconciliationMatch>(model, "ReconciliationMatch");
        match.ToTable(t => t.UseSqlOutputClause(false));
        Text(match, ("Reason", 1000));
        match.Property(x => x.SignedAmount).HasPrecision(15, 2);
        match.HasIndex(x => x.ReversalOfId).IsUnique().HasFilter("[ReversalOfId] IS NOT NULL");
        match.HasIndex(x => new { x.BankLineId, x.FinancePostingId });
        match.HasIndex(x => new { x.FinancePostingId, x.BankLineId });
        match.HasOne<Reconciliation>().WithMany().HasForeignKey(x => x.ReconciliationId).OnDelete(DeleteBehavior.NoAction);
        match.HasOne<BankLine>().WithMany().HasForeignKey(x => x.BankLineId).OnDelete(DeleteBehavior.NoAction);
        match.HasOne<FinancePosting>().WithMany().HasForeignKey(x => x.FinancePostingId).OnDelete(DeleteBehavior.NoAction);
        match.HasOne<ReconciliationMatch>().WithMany().HasForeignKey(x => x.ReversalOfId).OnDelete(DeleteBehavior.NoAction);
        Check(match, "Facts", "[SignedAmount]<>0 AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");

        var exclusion = Record<BankLineExclusion>(model, "BankLineExclusion");
        exclusion.ToTable(t => t.UseSqlOutputClause(false));
        Text(exclusion, ("EvidenceReference", 300), ("Reason", 1000));
        exclusion.HasIndex(x => x.BankLineId).IsUnique();
        exclusion.HasOne<Reconciliation>().WithMany().HasForeignKey(x => x.ReconciliationId).OnDelete(DeleteBehavior.NoAction);
        exclusion.HasOne<BankLine>().WithMany().HasForeignKey(x => x.BankLineId).OnDelete(DeleteBehavior.NoAction);
        exclusion.HasOne<BankLine>().WithMany().HasForeignKey(x => x.DuplicateOfBankLineId).OnDelete(DeleteBehavior.NoAction);
        Check(exclusion, "Evidence", "[BankLineId]<>[DuplicateOfBankLineId] AND LEN(TRIM([EvidenceReference])) BETWEEN 10 AND 300 AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");

        var variance = Record<ReconciliationVariance>(model, "ReconciliationVariance");
        variance.ToTable(t => t.UseSqlOutputClause(false));
        Text(variance, ("Reason", 1000));
        variance.Property(x => x.SignedResidual).HasPrecision(15, 2);
        variance.HasIndex(x => new { x.ReconciliationId, x.BankLineId, x.ExplainedAt });
        variance.HasOne<Reconciliation>().WithMany().HasForeignKey(x => x.ReconciliationId).OnDelete(DeleteBehavior.NoAction);
        variance.HasOne<BankLine>().WithMany().HasForeignKey(x => x.BankLineId).OnDelete(DeleteBehavior.NoAction);
        Check(variance, "Evidence", "[SignedResidual]<>0 AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");

        var targetVariance = Record<ReconciliationTargetVariance>(model, "ReconciliationTargetVariance");
        targetVariance.ToTable(t => t.UseSqlOutputClause(false));
        Text(targetVariance, ("Reason", 1000));
        targetVariance.Property(x => x.SignedResidual).HasPrecision(15, 2);
        targetVariance.HasIndex(x => new { x.ReconciliationId, x.FinancePostingId, x.ExplainedAt });
        targetVariance.HasOne<Reconciliation>().WithMany().HasForeignKey(x => x.ReconciliationId).OnDelete(DeleteBehavior.NoAction);
        targetVariance.HasOne<FinancePosting>().WithMany().HasForeignKey(x => x.FinancePostingId).OnDelete(DeleteBehavior.NoAction);
        Check(targetVariance, "Evidence", "[SignedResidual]<>0 AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
    }
}
