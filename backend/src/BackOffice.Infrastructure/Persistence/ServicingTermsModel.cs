using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureServicingTerms(ModelBuilder model)
    {
        var terms=Record<ServicingTermsVersion>(model,"ServicingTermsVersion");terms.ToTable(t=>t.UseSqlOutputClause(false));
        Text(terms,("TermsHash",64),("AssuranceHashAtPreparation",64));
        Check(terms,"TermsJson","ISJSON([TermsJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[TermsJson] COLLATE Latin1_General_100_BIN2_UTF8))<=8388608");
        terms.HasAlternateKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId});
        terms.HasIndex(x=>new{x.CycleId,x.Sequence}).IsUnique();
        terms.HasIndex(x=>new{x.DraftId,x.CreatedAt,x.Id});
        terms.HasOne<ServicingRatingResult>().WithMany().HasForeignKey(x=>new{x.RatingId,x.CycleId,x.DraftId,x.RevisionId})
            .HasPrincipalKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId}).OnDelete(DeleteBehavior.NoAction);
        terms.HasOne<PolicyVersion>().WithMany().HasForeignKey(x=>x.BaseVersionId).OnDelete(DeleteBehavior.NoAction);
        terms.HasOne<TemplateVersion>().WithMany().HasForeignKey(x=>x.TemplateVersionId).OnDelete(DeleteBehavior.NoAction);
        terms.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.PreparedBy).OnDelete(DeleteBehavior.NoAction);
        Check(terms,"Sequence","[Sequence]>0");
        Check(terms,"Identity","[Id]<>'00000000-0000-0000-0000-000000000000'");
        Check(terms,"Provenance","[CreatedBy] IS NOT NULL AND [CreatedBy]=[PreparedBy] AND [PreparedAt]=[CreatedAt]");
        foreach(var property in new[]{"TermsHash","AssuranceHashAtPreparation"})
        {terms.Property<string>(property).UseCollation("Latin1_General_100_BIN2");Check(terms,property,$"LEN([{property}])=64 AND [{property}] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");}
        Check(terms,"PayloadHash","[TermsHash]=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),[TermsJson] COLLATE Latin1_General_100_BIN2_UTF8)),2))");
        model.Entity<ServicingCycle>().HasOne<ServicingTermsVersion>().WithMany()
            .HasForeignKey(x=>new{x.CurrentTermsVersionId,x.Id,x.DraftId,x.RevisionId,x.CurrentRatingId})
            .HasPrincipalKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
    }
}
