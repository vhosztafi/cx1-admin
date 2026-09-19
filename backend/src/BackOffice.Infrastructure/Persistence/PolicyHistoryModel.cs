using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigurePolicyHistory(ModelBuilder model)
    {
        model.Entity<PolicyVersion>().HasAlternateKey(x=>new{x.Id,x.TermId,x.PolicyId,x.ContentHash});
        model.Entity<PolicyVersion>().HasAlternateKey(x=>new{x.Id,x.PolicyId,x.ContentHash});
        var request=Record<PolicyReconstructionRequest>(model,"PolicyReconstructionRequest");request.ToTable(t=>t.UseSqlOutputClause(false));
        Text(request,("CoverageState",30),("Reason",2000));Hash(request,"VersionHash");Hash(request,"ManifestHash");UnderwritingJson(request,"ManifestJson");
        request.HasOne<PolicyTerm>().WithMany().HasForeignKey(x=>new{x.TermId,x.PolicyId}).HasPrincipalKey(x=>new{x.Id,x.PolicyId}).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<PolicyVersion>().WithMany().HasForeignKey(x=>new{x.VersionId,x.TermId,x.PolicyId,x.VersionHash})
            .HasPrincipalKey(x=>new{x.Id,x.TermId,x.PolicyId,x.ContentHash}).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<OutboxWork>().WithMany().HasForeignKey(x=>x.WorkId).OnDelete(DeleteBehavior.NoAction);request.HasIndex(x=>x.WorkId).IsUnique();
        request.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.NoAction);
        request.HasIndex(x=>new{x.PolicyId,x.CreatedAt,x.Id});
        Check(request,"Actor","[CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");
        Check(request,"Cutoffs","DATEPART(TZOFFSET,[EffectiveAt])=0 AND DATEPART(TZOFFSET,[KnownAt])=0");
        Check(request,"Selection","([VersionId] IS NULL AND [VersionHash] IS NULL AND [CoverageState]='not-covered') OR ([VersionId] IS NOT NULL AND [VersionHash] IS NOT NULL AND [CoverageState] IN ('scheduled','active','expired','cancelled'))");
        Check(request,"ManifestHash","[ManifestHash]=HASHBYTES('SHA2_256',CONVERT(varchar(max),[ManifestJson] COLLATE Latin1_General_100_BIN2_UTF8))");

        var clone=Record<PolicyQuoteClone>(model,"PolicyQuoteClone");clone.ToTable(t=>t.UseSqlOutputClause(false));
        Text(clone,("Reason",2000));Hash(clone,"VersionHash");UnderwritingJson(clone,"ItemMapJson");
        clone.HasOne<PolicyVersion>().WithMany().HasForeignKey(x=>new{x.VersionId,x.PolicyId,x.VersionHash})
            .HasPrincipalKey(x=>new{x.Id,x.PolicyId,x.ContentHash}).OnDelete(DeleteBehavior.NoAction);
        clone.HasOne<QuoteRevision>().WithMany().HasForeignKey(x=>new{x.RevisionId,x.QuoteId}).HasPrincipalKey(x=>new{x.Id,x.QuoteId}).OnDelete(DeleteBehavior.NoAction);
        clone.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.NoAction);
        clone.HasIndex(x=>x.QuoteId).IsUnique();clone.HasIndex(x=>new{x.PolicyId,x.CreatedAt,x.Id});
        Check(clone,"Actor","[CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");
    }
}
