using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureParties(ModelBuilder model)
    {
        model.HasSequence<long>("ClientReferenceSequence").StartsAt(1).IncrementsBy(1);
        var agency=Record<Agency>(model,"Agency");
        Text(agency,("Reference",40),("LegalName",200),("State",20));
        agency.HasIndex(x => x.Reference).IsUnique();
        Check(agency,"State","[State] IN ('draft','active','suspended','abandoned')");
        Check(agency,"Identity","LEN(TRIM([Reference])) > 0 AND LEN(TRIM([LegalName])) > 0");
        var client=Record<ClientAccount>(model,"ClientAccount");
        Text(client,("Reference",40),("LegalName",200),("NormalizedName",200),("EntityType",30),("CompanyNumber",30),("IdentityState",30));
        Json(client,"Address");client.HasIndex(x => x.Reference).IsUnique();
        client.HasIndex(x => x.NormalizedName);
        client.HasIndex(x => x.CompanyNumber).HasFilter("[CompanyNumber] IS NOT NULL");
        Check(client,"Identity","LEN(TRIM([Reference])) > 0 AND LEN(TRIM([LegalName])) > 0 AND LEN(TRIM([NormalizedName])) > 0");
        Check(client,"State","[IdentityState] IN ('active','inactive')");
        Check(client,"EntityType","[EntityType] IN ('sole-trader','partnership','limited-company','llp')");
        var relationship=Record<ClientAgencyRelationship>(model,"ClientAgencyRelationship");
        Text(relationship,("State",20));Check(relationship,"State","[State] IN ('active','inactive')");
        relationship.HasIndex(x => new {x.ClientId,x.AgencyId}).IsUnique();
        relationship.HasAlternateKey(x => new {x.Id,x.ClientId});
        relationship.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.NoAction);
        relationship.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        var activity=Record<ClientActivity>(model,"ClientActivity");
        activity.ToTable(t => t.UseSqlOutputClause(false));
        Text(activity,("EventType",100),("RecordKind",20));
        activity.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.NoAction);
        activity.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x => new {x.RelationshipId,x.ClientId})
            .HasPrincipalKey(x => new {x.Id,x.ClientId}).OnDelete(DeleteBehavior.NoAction);
        activity.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        activity.HasIndex(x => new {x.ClientId,x.OccurredAt,x.Id});
        Check(activity,"EventType","LEN(TRIM([EventType])) > 0");
        Check(activity,"RecordKind","[RecordKind] IS NULL OR [RecordKind] IN ('client','contact','match')");
        Check(activity,"RecordLink","([RecordId] IS NULL AND [RecordKind] IS NULL) OR ([RecordId] IS NOT NULL AND [RecordKind] IS NOT NULL)");
    }
}
