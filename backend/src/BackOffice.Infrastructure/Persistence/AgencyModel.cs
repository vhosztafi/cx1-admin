using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureAgencies(ModelBuilder model)
    {
        model.HasSequence<long>("AgencyReferenceSequence").StartsAt(1).IncrementsBy(1);
        var agency=model.Entity<Agency>();
        Text(agency,("NormalizedName",200),("RegulatoryReference",30));
        agency.Property(x=>x.OnboardingStep).HasDefaultValue(1);
        agency.Property(x=>x.NormalizedName).HasDefaultValue("");
        agency.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.RelationshipManagerId).OnDelete(DeleteBehavior.NoAction);
        agency.HasIndex(x=>new{x.State,x.NormalizedName,x.Id});agency.HasIndex(x=>new{x.RelationshipManagerId,x.Id});
        Check(agency,"OnboardingStep","[OnboardingStep] BETWEEN 1 AND 6");
        var draft=Record<AgencyOnboarding>(model,"AgencyOnboarding");
        Text(draft,("SchemaVersion",30));Json(draft,"Details");
        Check(draft,"DetailsBounds","DATALENGTH([Details]) <= 131072 AND LEFT(LTRIM([Details]),1) = '{'");
        draft.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);draft.HasIndex(x=>x.AgencyId).IsUnique();
        var product=Record<AgencyDraftProduct>(model,"AgencyDraftProduct");
        product.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        product.HasOne<ProductVersion>().WithMany().HasForeignKey(x=>x.ProductVersionId).OnDelete(DeleteBehavior.NoAction);
        product.HasIndex(x=>new{x.AgencyId,x.ProductVersionId}).IsUnique();Check(product,"Commission","[BrokerCommissionBasisPoints] BETWEEN 0 AND 10000");
        var activity=Record<AgencyActivity>(model,"AgencyActivity");Text(activity,("Action",100));
        activity.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        activity.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.NoAction);
        activity.HasIndex(x=>new{x.AgencyId,x.OccurredAt,x.Id});
        activity.ToTable(t=>t.UseSqlOutputClause(false));
    }
}
