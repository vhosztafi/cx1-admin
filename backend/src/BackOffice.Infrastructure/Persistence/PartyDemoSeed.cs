using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Parties;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public static class PartyDemoSeed
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web) {DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
    public static readonly Guid FirstAgencyId=Guid.Parse("31000000-0000-4000-8000-000000000001");
    public static readonly Guid SecondAgencyId=Guid.Parse("31000000-0000-4000-8000-000000000002");
    public static Guid ClientId(int index) => Guid.Parse($"32000000-0000-4000-8000-{index:D12}");
    public static Guid RelationshipId(int client,int agency) => Guid.Parse($"33000000-0000-4000-8000-{client*10+agency:D12}");

    // The caller owns the foundation seed transaction/application lock. Stable IDs preserve edited demo records.
    public static async Task SeedAsync(BackOfficeDbContext db,CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Party seed requires the shared seed transaction.");
        var actor=await db.Set<StaffUser>().Where(x => x.Email=="servicing@cover.example").Select(x => x.Id).SingleAsync(token);
        foreach (var (agencyId,reference,name) in new[] {(FirstAgencyId,"AG-DEMO-01","Fictional Brightside Agency"),(SecondAgencyId,"AG-DEMO-02","Fictional Kingsway Agency")})
            if (await db.Set<Agency>().FindAsync([agencyId],token) is null)
                db.Add(new Agency {Id=agencyId,Reference=reference,LegalName=name,State="draft",CreatedBy=actor});
        for (var index=1;index<=32;index++)
        {
            var clientId=ClientId(index);
            var client=await db.Set<ClientAccount>().FindAsync([clientId],token);
            if (client is null)
            {
                var name=index==1 ? "Fictional Smith Motor Traders Ltd" : $"Fictional Demo Traders {index:D2}";
                var type=index%3==0 ? "sole-trader" : index%3==1 ? "limited-company" : "partnership";
                var identity=ClientIdentity.Validate(new ClientWrite(name,type,new AddressWrite($"{index} Fictional Road","Sheffield","S1 1AA","GB"),type=="limited-company" ? $"DEMO{index:D4}" : null));
                client=new ClientAccount {Id=clientId,Reference=await ClientReferences.NextAsync(db,token),LegalName=identity.LegalName,
                    NormalizedName=identity.NormalizedName,EntityType=identity.EntityType,CompanyNumber=identity.CompanyNumber,
                    Address=JsonSerializer.Serialize(identity.Address,Json),CreatedBy=actor};
                db.Add(client);
                db.Add(new ClientActivity {Id=Guid.Parse($"34000000-0000-4000-8000-{index:D12}"),ClientId=clientId,ActorId=actor,CreatedBy=actor,
                    EventType="client.demo-created",RecordId=clientId,RecordKind="client"});
            }
            foreach (var agencyNumber in index<=3 ? new[] {1,2} : new[] {index%2+1})
            {
                var relationshipId=RelationshipId(index,agencyNumber);
                if (await db.Set<ClientAgencyRelationship>().FindAsync([relationshipId],token) is null)
                    db.Add(new ClientAgencyRelationship {Id=relationshipId,ClientId=clientId,AgencyId=agencyNumber==1 ? FirstAgencyId : SecondAgencyId,CreatedBy=actor});
            }
        }
    }
}
