using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureFinanceBordereaux(ModelBuilder model)
    {
        var batch = Record<FinanceBordereauBatch>(model, "FinanceBordereauBatch");
        batch.ToTable(t => t.UseSqlOutputClause(false));
        batch.HasOne<CapacityProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.NoAction);
        batch.HasOne<AccountingPeriod>().WithMany().HasForeignKey(x => x.AccountingPeriodId).OnDelete(DeleteBehavior.NoAction);
        batch.HasIndex(x => new { x.ProviderId, x.AccountingPeriodId, x.CreatedAt });
        var version = Record<FinanceBordereauVersion>(model, "FinanceBordereauVersion");
        version.ToTable(t => t.UseSqlOutputClause(false));
        version.HasOne<FinanceBordereauBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.NoAction);
        version.HasOne<FinanceBordereauVersion>().WithMany().HasForeignKey(x => x.ParentVersionId).OnDelete(DeleteBehavior.NoAction);
        version.HasIndex(x => new { x.BatchId, x.Number }).IsUnique();
        Text(version, ("SchemaVersion", 30), ("State", 20));
        Hash(version, "SourceHash"); Hash(version, "MembersHash");
        version.Property(x => x.ContentHash).HasColumnType("binary(32)");
        version.Property(x => x.ContentBytes).HasColumnType("varbinary(max)");
        Json(version, "ValidationJson");
        Check(version, "State", "[State] IN ('unvalidated','invalid','valid') AND [Number]>0");
        Check(version, "Content", "([State]='valid' AND [ContentBytes] IS NOT NULL AND [ContentHash]=HASHBYTES('SHA2_256',[ContentBytes])) OR ([State]<>'valid' AND [ContentBytes] IS NULL AND [ContentHash] IS NULL)");
        var member = Record<FinanceBordereauMember>(model, "FinanceBordereauMember");
        member.ToTable(t => t.UseSqlOutputClause(false));
        member.HasOne<FinanceBordereauVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.NoAction);
        member.HasOne<Journal>().WithMany().HasForeignKey(x => x.SourceJournalId).OnDelete(DeleteBehavior.NoAction);
        member.HasOne<Policy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.NoAction);
        member.HasOne<PolicyTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.NoAction);
        member.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        member.HasOne<ProductVersion>().WithMany().HasForeignKey(x => x.ProductVersionId).OnDelete(DeleteBehavior.NoAction);
        member.HasOne<AgencyTermsVersion>().WithMany().HasForeignKey(x => x.AgencyTermsVersionId).OnDelete(DeleteBehavior.NoAction);
        member.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.CorrectionActorId).OnDelete(DeleteBehavior.NoAction);
        member.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.ExclusionActorId).OnDelete(DeleteBehavior.NoAction);
        member.HasIndex(x => new { x.VersionId, x.SourceJournalId }).IsUnique();
        foreach (var name in new[] { "Premium", "Tax", "Fee", "Commission", "NetDue" }) member.Property(name).HasPrecision(15, 2);
        Text(member, ("Currency", 3), ("PolicyReference", 100), ("ProviderProductCode", 100),
            ("AgencyReference", 100), ("CorrectionReason", 1000), ("ExclusionReason", 1000));
        Check(member, "Currency", "[Currency]='GBP'");
        Check(member, "Correction", "([CorrectionActorId] IS NULL AND [CorrectionReason] IS NULL AND [CorrectedAt] IS NULL) OR ([CorrectionActorId] IS NOT NULL AND LEN(TRIM([CorrectionReason])) BETWEEN 10 AND 1000 AND [CorrectedAt] IS NOT NULL)");
        Check(member, "Exclusion", "([ExclusionActorId] IS NULL AND [ExclusionReason] IS NULL AND [ExcludedAt] IS NULL) OR ([ExclusionActorId] IS NOT NULL AND LEN(TRIM([ExclusionReason])) BETWEEN 10 AND 1000 AND [ExcludedAt] IS NOT NULL)");
    }
}
