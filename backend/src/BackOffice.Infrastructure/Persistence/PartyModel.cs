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
        var person=Record<Person>(model,"Person");
        Text(person,("FullName",200),("FirstName",100),("Surname",100));
        Check(person,"Name","LEN(TRIM([FullName])) > 0");
        var contact=Record<Contact>(model,"Contact");
        Text(contact,("DeclaredFullName",200),("NormalizedName",200),("DeclaredFirstName",100),("DeclaredSurname",100),("Role",100),("Email",254),("Telephone",50),("EndReason",1000));
        Json(contact,"MarketingConsent");
        contact.HasOne<Person>().WithMany().HasForeignKey(x=>x.PersonId).OnDelete(DeleteBehavior.NoAction);
        contact.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x=>new {x.RelationshipId,x.ClientId})
            .HasPrincipalKey(x=>new {x.Id,x.ClientId}).OnDelete(DeleteBehavior.NoAction);
        contact.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.EndedBy).OnDelete(DeleteBehavior.NoAction);
        contact.HasIndex(x=>x.RelationshipId).IsUnique().HasFilter("[IsPrimary] = 1 AND [EndedAt] IS NULL");
        contact.HasIndex(x=>new {x.RelationshipId,x.EndedAt,x.Id});
        contact.HasIndex(x=>new {x.PersonId,x.ClientId});contact.HasIndex(x=>x.NormalizedName);
        Check(contact,"Identity","LEN(TRIM([DeclaredFullName])) > 0 AND LEN(TRIM([NormalizedName])) > 0 AND LEN(TRIM([Role])) > 0");
        Check(contact,"Ending","([EndedAt] IS NULL AND [EndedBy] IS NULL AND [EndReason] IS NULL) OR ([EndedAt] IS NOT NULL AND [EndedAt] >= [CreatedAt] AND [EndedBy] IS NOT NULL AND [EndReason] IS NOT NULL AND LEN(TRIM([EndReason])) > 0 AND [IsPrimary] = 0)");
        Check(contact,"Consent","COALESCE(JSON_VALUE([MarketingConsent],'$.state'),'') IN ('given','withheld','not-asked') AND COALESCE(JSON_VALUE([MarketingConsent],'$.email'),'') IN ('true','false') AND COALESCE(JSON_VALUE([MarketingConsent],'$.telephone'),'') IN ('true','false') AND COALESCE(LEN(TRIM(JSON_VALUE([MarketingConsent],'$.source'))),0) > 0 AND TRY_CONVERT(datetimeoffset,JSON_VALUE([MarketingConsent],'$.recordedAt'),127) IS NOT NULL AND DATEPART(TZOFFSET,TRY_CONVERT(datetimeoffset,JSON_VALUE([MarketingConsent],'$.recordedAt'),127))=0 AND ((JSON_VALUE([MarketingConsent],'$.state')='given' AND (JSON_VALUE([MarketingConsent],'$.email')='true' OR JSON_VALUE([MarketingConsent],'$.telephone')='true')) OR (JSON_VALUE([MarketingConsent],'$.state') IN ('withheld','not-asked') AND JSON_VALUE([MarketingConsent],'$.email')='false' AND JSON_VALUE([MarketingConsent],'$.telephone')='false'))");
    }
}
