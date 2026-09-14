using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class ContactServiceTests
{
    [Fact]
    public async Task RealSqlContactServiceSerializesWritersAndRetainsAtomicHistory()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=ownedName;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var boundary=new SqlCommandBoundary(new Factory(options),TimeProvider.System);
        var relationship=PartyDemoSeed.RelationshipId(1,1);var otherRelationship=PartyDemoSeed.RelationshipId(1,2);
        ActorContext actor=null!;
        try
        {
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
                var id=await db.Set<StaffUser>().Where(x=>x.Email=="servicing@cover.example").Select(x=>x.Id).SingleAsync();
                actor=new ActorContext(id,null,null,new HashSet<string>{"servicing"});
            }
            async Task<byte[]> ParentVersion(Guid id)
            { await using var db=new BackOfficeDbContext(options);return await db.Set<ClientAgencyRelationship>().Where(x=>x.Id==id).Select(x=>x.RowVersion).SingleAsync(); }
            async Task<Contact> Read(Guid id)
            { await using var db=new BackOfficeDbContext(options);return await db.Set<Contact>().AsNoTracking().SingleAsync(x=>x.Id==id); }
            async Task<CommandOutcome> Run(string key,Guid rel,Guid? contact,object intent,Func<BackOfficeDbContext,CancellationToken,Task<Contact>> action,ActorContext? caller=null)
            {
                var currentActor=caller ?? actor;
                await using(var auth=new BackOfficeDbContext(options))await ContactService.AuthorizeAsync(auth,currentActor,rel,contact);
                return await boundary.ExecuteAsync(new CommandIdentity(currentActor.UserId,"contact-test/"+rel,key,Guid.NewGuid()),intent,"contact.test",async(db,token)=>
                {
                    var saved=await action(db,token);
                    return new CommandOutcome(saved.Id,200,JsonSerializer.Serialize(new {saved.Id,saved.DeclaredFullName,saved.IsPrimary}),Etag:"\""+Convert.ToBase64String(saved.RowVersion)+"\"");
                });
            }
            async Task<CommandOutcome> Create(string key,Guid rel,ValidatedContact input,byte[] version)=>await Run(key,rel,null,input,
                (db,token)=>ContactService.CreateAsync(db,actor,rel,version,input,DateTimeOffset.UtcNow,token));

            var initialVersion=await ParentVersion(relationship);var firstInput=Input("Fictional Alex");
            var first=await Create("first",relationship,firstInput,initialVersion);
            var firstRow=await Read(first.ResourceId);Assert.True(firstRow.IsPrimary);
            var second=await Create("second",relationship,Input("Fictional Sam"),await ParentVersion(relationship));
            var secondRow=await Read(second.ResourceId);Assert.False(secondRow.IsPrimary);

            // A failed second save must roll back the already flushed demotion, activity and receipt.
            await Assert.ThrowsAsync<DbUpdateException>(()=>Run("failed-promotion",relationship,second.ResourceId,new {invalid=true},
                (db,token)=>ContactService.UpdateAsync(db,actor,relationship,second.ResourceId,secondRow.RowVersion,
                    Input("Fictional invalid") with {IsPrimary=true,Role=""},DateTimeOffset.UtcNow,token)));
            Assert.True((await Read(first.ResourceId)).IsPrimary);Assert.False((await Read(second.ResourceId)).IsPrimary);
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.False(await db.Set<IdempotencyRecord>().AnyAsync(x=>x.Key=="failed-promotion"));
                Assert.Equal(2,await db.Set<ClientActivity>().CountAsync(x=>x.EventType.StartsWith("contact.")));
                Assert.Equal(2,await db.Set<AuditEvent>().CountAsync(x=>x.EventType=="contact.test"));
            }
            var illegalEnd=await Assert.ThrowsAsync<ContactOperationException>(()=>Run("illegal-end",relationship,first.ResourceId,new {reason="Test"},
                (db,token)=>ContactService.EndAsync(db,actor,relationship,first.ResourceId,firstRow.RowVersion,"Test",DateTimeOffset.UtcNow,token)));
            Assert.Equal("choose-replacement-primary",illegalEnd.Code);

            var third=await Create("third",relationship,Input("Fictional Jo"),await ParentVersion(relationship));
            var thirdRow=await Read(third.ResourceId);
            // Distinct targets and keys race for the same parent lock. Either serial order is legal.
            await Task.WhenAll(
                Run("promote-second",relationship,second.ResourceId,new {primary=true},(db,t)=>ContactService.MakePrimaryAsync(db,actor,relationship,second.ResourceId,secondRow.RowVersion,DateTimeOffset.UtcNow,t)),
                Run("promote-third",relationship,third.ResourceId,new {primary=true},(db,t)=>ContactService.MakePrimaryAsync(db,actor,relationship,third.ResourceId,thirdRow.RowVersion,DateTimeOffset.UtcNow,t)));
            await using(var db=new BackOfficeDbContext(options))Assert.Equal(1,await db.Set<Contact>().CountAsync(x=>x.RelationshipId==relationship && x.IsPrimary && x.EndedAt==null));

            var replay=await Create("first",relationship,firstInput,initialVersion);
            Assert.True(replay.Replayed);Assert.Equal(first.Body,replay.Body);Assert.Equal(first.Etag,replay.Etag);
            var denied=actor with {Roles=new HashSet<string>{"finance"}};
            Assert.Equal(403,(await Assert.ThrowsAsync<ContactOperationException>(()=>Run("first",relationship,null,firstInput,(_,_)=>throw new InvalidOperationException(),denied))).Status);

            var shared=await Create("reuse",otherRelationship,Input("Fictional A. alternate") with {PersonId=firstRow.PersonId},await ParentVersion(otherRelationship));
            firstRow=await Read(first.ResourceId);
            await Run("edit-first",relationship,first.ResourceId,new {name="Fictional revised"},(db,t)=>ContactService.UpdateAsync(db,actor,relationship,first.ResourceId,firstRow.RowVersion,Input("Fictional revised") with {IsPrimary=firstRow.IsPrimary},DateTimeOffset.UtcNow,t));
            Assert.Equal("Fictional A. alternate",(await Read(shared.ResourceId)).DeclaredFullName);
            await using(var db=new BackOfficeDbContext(options))Assert.Equal("Fictional Alex",(await db.Set<Person>().SingleAsync(x=>x.Id==firstRow.PersonId)).FullName);
            var sharedRow=await Read(shared.ResourceId);
            await Run("end-last",otherRelationship,shared.ResourceId,new {reason="Fictional retirement"},(db,t)=>ContactService.EndAsync(db,actor,otherRelationship,shared.ResourceId,sharedRow.RowVersion,"Fictional retirement",DateTimeOffset.UtcNow,t));
            sharedRow=await Read(shared.ResourceId);Assert.False(sharedRow.IsPrimary);Assert.NotNull(sharedRow.EndedAt);Assert.Equal(actor.UserId,sharedRow.EndedBy);Assert.Equal("Fictional retirement",sharedRow.EndReason);
            Assert.Equal("contact-ended",(await Assert.ThrowsAsync<ContactOperationException>(()=>Run("edit-ended",otherRelationship,shared.ResourceId,new {edit=true},(db,t)=>ContactService.UpdateAsync(db,actor,otherRelationship,shared.ResourceId,sharedRow.RowVersion,Input("Fictional ended edit"),DateTimeOffset.UtcNow,t)))).Code);

            // Concurrent first creations share one expected parent version: exactly one succeeds.
            var emptyRelationship=PartyDemoSeed.RelationshipId(2,1);var emptyVersion=await ParentVersion(emptyRelationship);
            async Task<bool> Race(string key)
            { try {await Create(key,emptyRelationship,Input("Fictional "+key),emptyVersion);return true;}catch(ContactOperationException e) when(e.Status==412){return false;} }
            var race=await Task.WhenAll(Race("race-a"),Race("race-b"));Assert.Single(race,x=>x);
            await using(var db=new BackOfficeDbContext(options))Assert.Single(await db.Set<Contact>().Where(x=>x.RelationshipId==emptyRelationship && x.IsPrimary).ToListAsync());
        }
        finally
        {
            if(connection.InitialCatalog!=ownedName)throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private static ValidatedContact Input(string name)=>ContactRules.Validate(new ContactWrite("Director",false,
        new MarketingConsentWrite("not-asked",false,false,DateTimeOffset.UtcNow.AddMinutes(-1),"Fictional test"),FullName:name),DateTimeOffset.UtcNow);
    private sealed class Factory(DbContextOptions<BackOfficeDbContext> options):IDbContextFactory<BackOfficeDbContext>
    { public BackOfficeDbContext CreateDbContext()=>new(options); }
}
