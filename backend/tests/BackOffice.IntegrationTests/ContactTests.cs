using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application.Parties;
using BackOffice.Application;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class ContactTests
{
    [Fact]
    public async Task RealSqlContactConstraintsPreserveDeclarationsAndRollbackPrimaryChanges()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=ownedName;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        var firstPerson=new Person {FullName="Fictional Alex Example"};var secondPerson=new Person {FullName="Fictional Sam Example"};
        var first=NewContact(firstPerson.Id,1,true,"Fictional Alex, first declaration");
        var otherAgency=NewContact(firstPerson.Id,2,true,"Fictional A. Example, second declaration");
        var replacement=NewContact(secondPerson.Id,1,false,"Fictional Sam Example");
        Guid actor;
        try
        {
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);
                actor=await db.Set<StaffUser>().Where(x=>x.Email=="servicing@cover.example").Select(x=>x.Id).SingleAsync();
                db.AddRange(firstPerson,secondPerson,first,otherAgency,replacement);await db.SaveChangesAsync();
                Assert.False(db.Database.HasPendingModelChanges());
            }
            await Reject(options,db=>db.Add(NewContact(secondPerson.Id,1,true,"Duplicate primary")),2601,2627);
            await Reject(options,db=>{var row=NewContact(firstPerson.Id,1,false,"Wrong client");row.ClientId=PartyDemoSeed.ClientId(2);db.Add(row);},547);
            await Reject(options,db=>db.Add(NewContact(Guid.NewGuid(),1,false,"Unknown person")),547);
            await Reject(options,db=>{var row=NewContact(firstPerson.Id,1,false,"Invalid consent");row.MarketingConsent="{}";db.Add(row);},547);
            await Reject(options,db=>{var row=NewContact(firstPerson.Id,1,false,"Inconsistent consent");row.MarketingConsent=Consent("withheld",true,false);db.Add(row);},547);
            await Reject(options,db=>{var row=NewContact(firstPerson.Id,1,true,"Ended primary");row.EndedAt=DateTimeOffset.UtcNow.AddSeconds(1);row.EndedBy=actor;row.EndReason="Fictional ending";db.Add(row);},547);
            await Reject(options,db=>{var row=NewContact(firstPerson.Id,1,false,"Non UTC");row.CreatedAt=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(1));db.Add(row);},547);
            await using(var edit=new BackOfficeDbContext(options))
            {
                var row=await edit.Set<Contact>().SingleAsync(x=>x.Id==first.Id);row.DeclaredFullName="Fictional edited declaration";row.Email="edited@fictional.example";
                await edit.SaveChangesAsync();await DemoDatabase.SeedAsync(edit,password);
            }
            await using(var read=new BackOfficeDbContext(options))
            {
                Assert.Equal("Fictional Alex Example",(await read.Set<Person>().SingleAsync(x=>x.Id==firstPerson.Id)).FullName);
                var other=await read.Set<Contact>().SingleAsync(x=>x.Id==otherAgency.Id);Assert.Equal(otherAgency.DeclaredFullName,other.DeclaredFullName);Assert.Null(other.Email);
                Assert.Equal("Fictional edited declaration",(await read.Set<Contact>().SingleAsync(x=>x.Id==first.Id)).DeclaredFullName);
                var consent=JsonSerializer.Deserialize<MarketingConsentWrite>(other.MarketingConsent,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Assert.Equal("not-asked",consent.State);Assert.False(consent.Email);Assert.False(consent.Telephone);
                var firstScope=new PartyScope(new ActorContext(actor,null,PartyDemoSeed.FirstAgencyId,new HashSet<string>{"agency-admin"}));
                var secondScope=new PartyScope(new ActorContext(actor,null,PartyDemoSeed.SecondAgencyId,new HashSet<string>{"agency-admin"}));
                Assert.Equal(2,await firstScope.Contacts(read).CountAsync(x=>x.ClientId==first.ClientId));Assert.Equal(1,await secondScope.Contacts(read).CountAsync(x=>x.ClientId==first.ClientId));
                Assert.Single(await firstScope.SearchClients(read,"SAM TAYLOR").ToListAsync());
                Assert.Empty(await secondScope.SearchClients(read,"SAM TAYLOR").ToListAsync());
                Assert.NotNull(await firstScope.FindReusablePersonAsync(read,first.ClientId,firstPerson.Id));
                Assert.NotNull(await secondScope.FindReusablePersonAsync(read,first.ClientId,firstPerson.Id));
                Assert.Null(await secondScope.FindReusablePersonAsync(read,first.ClientId,secondPerson.Id));
                Assert.Null(await firstScope.FindReusablePersonAsync(read,PartyDemoSeed.ClientId(2),firstPerson.Id));
                Assert.Null(await firstScope.FindReusablePersonAsync(read,first.ClientId,Guid.NewGuid()));
            }
            // The service will use this ordered flush pattern under a parent lock. A failed
            // second save must restore the prior primary, not leave the relationship empty.
            await using(var writer=new BackOfficeDbContext(options))
            {
                await using var tx=await writer.Database.BeginTransactionAsync();
                var old=await writer.Set<Contact>().SingleAsync(x=>x.Id==first.Id);old.IsPrimary=false;await writer.SaveChangesAsync();
                var next=await writer.Set<Contact>().SingleAsync(x=>x.Id==replacement.Id);next.IsPrimary=true;next.Role="";
                await Assert.ThrowsAsync<DbUpdateException>(()=>writer.SaveChangesAsync());await tx.RollbackAsync();
            }
            await using(var read=new BackOfficeDbContext(options))
            {
                Assert.True((await read.Set<Contact>().SingleAsync(x=>x.Id==first.Id)).IsPrimary);
                Assert.False((await read.Set<Contact>().SingleAsync(x=>x.Id==replacement.Id)).IsPrimary);
            }
            await using(var writer=new BackOfficeDbContext(options))
            {
                await using var tx=await writer.Database.BeginTransactionAsync();
                var old=await writer.Set<Contact>().SingleAsync(x=>x.Id==first.Id);old.IsPrimary=false;await writer.SaveChangesAsync();
                var next=await writer.Set<Contact>().SingleAsync(x=>x.Id==replacement.Id);next.IsPrimary=true;await writer.SaveChangesAsync();await tx.CommitAsync();
            }
            await using(var writer=new BackOfficeDbContext(options))
            {
                var ended=await writer.Set<Contact>().SingleAsync(x=>x.Id==first.Id);ended.EndedAt=DateTimeOffset.UtcNow;ended.EndedBy=actor;ended.EndReason="Fictional retained history";await writer.SaveChangesAsync();
            }
            await using(var read=new BackOfficeDbContext(options))
            {
                Assert.Equal(3,await read.Set<Contact>().CountAsync(x=>x.ClientId==first.ClientId));
                Assert.Equal(1,await read.Set<Contact>().CountAsync(x=>x.RelationshipId==first.RelationshipId && x.IsPrimary && x.EndedAt==null));
                Assert.Equal("Fictional retained history",(await read.Set<Contact>().SingleAsync(x=>x.Id==first.Id)).EndReason);
                var scope=new PartyScope(new ActorContext(actor,null,PartyDemoSeed.FirstAgencyId,new HashSet<string>{"agency-admin"}));
                Assert.Equal(1,await scope.Contacts(read).CountAsync(x=>x.ClientId==first.ClientId));Assert.Equal(2,await scope.Contacts(read,includeEnded:true).CountAsync(x=>x.ClientId==first.ClientId));
                Assert.NotNull(await scope.FindReusablePersonAsync(read,first.ClientId,firstPerson.Id));
                var relationship=await read.Set<ClientAgencyRelationship>().SingleAsync(x=>x.Id==first.RelationshipId);relationship.State="inactive";await read.SaveChangesAsync();
                Assert.Null(await scope.FindReusablePersonAsync(read,first.ClientId,firstPerson.Id));
            }
            await using(var editSeed=new BackOfficeDbContext(options))
            {
                var demo=await editSeed.Set<Contact>().Where(x=>x.ClientId==PartyDemoSeed.ClientId(3)).OrderBy(x=>x.Id).ToListAsync();
                Assert.Equal(3,demo.Count);Assert.Equal(demo[0].PersonId,demo[2].PersonId);Assert.NotEqual(demo[0].DeclaredFullName,demo[2].DeclaredFullName);
                Assert.Equal(3,demo.Select(x=>JsonDocument.Parse(x.MarketingConsent).RootElement.GetProperty("state").GetString()).Distinct().Count());
                demo[0].Email="retained@fictional.example";demo[1].EndedAt=DateTimeOffset.UtcNow;demo[1].EndedBy=actor;demo[1].EndReason="Fictional retained seed history";
                await editSeed.SaveChangesAsync();await DemoDatabase.SeedAsync(editSeed,password);
            }
            await using(var inspectSeed=new BackOfficeDbContext(options))
            {
                Assert.Equal("retained@fictional.example",(await inspectSeed.Set<Contact>().SingleAsync(x=>x.Id==ContactDemoSeed.ContactId(1))).Email);
                Assert.NotNull((await inspectSeed.Set<Contact>().SingleAsync(x=>x.Id==ContactDemoSeed.ContactId(2))).EndedAt);
                Assert.Equal(3,await inspectSeed.Set<Contact>().CountAsync(x=>x.ClientId==PartyDemoSeed.ClientId(3)));
            }
            await using var current=new BackOfficeDbContext(options);await using var stale=new BackOfficeDbContext(options);
            var fresh=await current.Set<Contact>().SingleAsync(x=>x.Id==replacement.Id);var oldVersion=await stale.Set<Contact>().SingleAsync(x=>x.Id==replacement.Id);
            fresh.Telephone="0000000000";await current.SaveChangesAsync();oldVersion.Telephone="1111111111";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>stale.SaveChangesAsync());
        }
        finally
        {
            if(connection.InitialCatalog!=ownedName)throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private static Contact NewContact(Guid personId,int agency,bool primary,string name)=>new()
    {
        ClientId=PartyDemoSeed.ClientId(1),RelationshipId=PartyDemoSeed.RelationshipId(1,agency),PersonId=personId,
        DeclaredFullName=name,NormalizedName=ClientIdentity.NormalizeName(name),Role="Director",IsPrimary=primary,MarketingConsent=Consent("not-asked",false,false)
    };
    private static string Consent(string state,bool email,bool telephone)=>JsonSerializer.Serialize(new MarketingConsentWrite(state,email,telephone,DateTimeOffset.UtcNow,"Fictional SQL test"),new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static async Task Reject(DbContextOptions<BackOfficeDbContext> options,Action<BackOfficeDbContext> arrange,params int[] numbers)
    {
        await using var db=new BackOfficeDbContext(options);arrange(db);var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number,numbers);
    }
}
