using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureSupportFlags(ModelBuilder model)
    {
        var flag=Record<SupportFlag>(model,"SupportFlag");
        Text(flag,("TypeCode",60),("InternalCategory",100),("InternalInstruction",2000),("AgencyInstruction",1000),("ConsentBasis",200),("Reason",1000));
        flag.HasAlternateKey(x=>new {x.Id,x.ClientId});
        flag.HasOne<Person>().WithMany().HasForeignKey(x=>x.PersonId).OnDelete(DeleteBehavior.NoAction);
        flag.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x=>new {x.OriginRelationshipId,x.ClientId})
            .HasPrincipalKey(x=>new {x.Id,x.ClientId}).OnDelete(DeleteBehavior.NoAction);
        flag.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.EndedBy).OnDelete(DeleteBehavior.NoAction);
        flag.HasIndex(x=>new {x.PersonId,x.ReviewOn});flag.HasIndex(x=>new {x.OriginRelationshipId,x.EndedAt,x.Id});
        Check(flag,"TypeCode","[TypeCode] IN ('vulnerability','third-party-authority','financial-difficulty','accessible-format','interpreter-required','deceased-or-business-ceased')");
        Check(flag,"Category","[InternalCategory] IN ('health','life-event','resilience','capability','authority')");
        Check(flag,"ConsentBasis","[ConsentBasis] IN ('verbal-consent','written-consent','third-party-authority')");
        Check(flag,"Text","LEN(TRIM([InternalInstruction]))>0 AND LEN(TRIM([Reason]))>0 AND ([AgencyInstruction] IS NULL OR LEN(TRIM([AgencyInstruction]))>0)");
        Check(flag,"ReviewOn","[ReviewOn] > CONVERT(date,'00010101',112)");
        Check(flag,"Ending","([EndedAt] IS NULL AND [EndedBy] IS NULL) OR ([EndedAt] IS NOT NULL AND [EndedAt]>=[CreatedAt] AND [EndedBy] IS NOT NULL)");
        var grant=Record<FlagVisibility>(model,"FlagVisibility");
        grant.HasOne<SupportFlag>().WithMany().HasForeignKey(x=>new {x.FlagId,x.ClientId}).HasPrincipalKey(x=>new {x.Id,x.ClientId}).OnDelete(DeleteBehavior.NoAction);
        grant.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x=>new {x.RelationshipId,x.ClientId})
            .HasPrincipalKey(x=>new {x.Id,x.ClientId}).OnDelete(DeleteBehavior.NoAction);
        grant.HasIndex(x=>new {x.FlagId,x.RelationshipId}).IsUnique();grant.HasIndex(x=>new {x.RelationshipId,x.FlagId});
        var history=Record<SupportFlagHistory>(model,"SupportFlagHistory");
        history.ToTable(t=>t.UseSqlOutputClause(false));
        Text(history,("Action",20),("Reason",1000));Json(history,"Snapshot");
        history.HasOne<SupportFlag>().WithMany().HasForeignKey(x=>x.FlagId).OnDelete(DeleteBehavior.NoAction);
        history.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.NoAction);
        history.HasIndex(x=>new {x.FlagId,x.OccurredAt,x.Id});
        Check(history,"Action","[Action] IN ('created','amended','reviewed','ended')");Check(history,"Reason","LEN(TRIM([Reason]))>0");
    }
}
