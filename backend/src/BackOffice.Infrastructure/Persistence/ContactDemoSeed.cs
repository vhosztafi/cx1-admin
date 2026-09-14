using System.Text.Json;
using BackOffice.Application.Parties;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public static class ContactDemoSeed
{
    public static Guid ContactId(int index)=>Guid.Parse($"36000000-0000-4000-8000-{index:D12}");
    public static Guid PersonId(int index)=>Guid.Parse($"35000000-0000-4000-8000-{index:D12}");
    public static async Task SeedAsync(BackOfficeDbContext db,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Contact seed requires the shared seed transaction.");
        var actor=await db.Set<StaffUser>().Where(x=>x.Email=="servicing@cover.example").Select(x=>x.Id).SingleAsync(token);
        var evidence=new DateTimeOffset(2026,1,1,12,0,0,TimeSpan.Zero);
        foreach(var agency in new[] {1,2})
        {
            var relationshipId=PartyDemoSeed.RelationshipId(3,agency);
            // Serialize with live contact commands. Never replace an edited, ended or already populated set.
            var parent=await db.Set<ClientAgencyRelationship>().FromSqlInterpolated($"SELECT * FROM [ClientAgencyRelationship] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={relationshipId}").SingleAsync(token);
            if(parent.State!="active" || await db.Set<Contact>().AnyAsync(x=>x.RelationshipId==relationshipId,token))continue;
            foreach(var number in agency==1 ? new[] {1,2} : new[] {3})
            {
                var personNumber=number==2 ? 2 : 1;var personId=PersonId(personNumber);
                if(await db.Set<Person>().FindAsync([personId],token) is null)
                    db.Add(new Person {Id=personId,FullName=personNumber==1 ? "Fictional Alex Morgan" : "Fictional Sam Taylor",CreatedBy=actor});
                var name=number==3 ? "Fictional A. Morgan" : number==1 ? "Fictional Alex Morgan" : "Fictional Sam Taylor";
                var state=number==1 ? "given" : number==2 ? "withheld" : "not-asked";
                db.Add(new Contact {Id=ContactId(number),ClientId=parent.ClientId,RelationshipId=parent.Id,PersonId=personId,
                    DeclaredFullName=name,NormalizedName=ClientIdentity.NormalizeName(name),Role=number==2 ? "Accounts" : "Director",
                    Email=number==3 ? "alternate@fictional.example" : number==1 ? "alex@fictional.example" : "accounts@fictional.example",
                    Telephone=number==3 ? "01632 960003" : "01632 960001",IsPrimary=number!=2,CreatedBy=actor,
                    MarketingConsent=JsonSerializer.Serialize(new MarketingConsentWrite(state,number==1,false,evidence,"Fictional demo consent evidence"),new JsonSerializerOptions(JsonSerializerDefaults.Web))});
                await db.SaveChangesAsync(token);
            }
            parent.UpdatedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync(token);
        }
    }
}
