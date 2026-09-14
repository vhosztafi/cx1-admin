using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class SupportFlagTests
{
    [Fact]
    public async Task RealSqlSupportFlagStorageRequiresExplicitGrantsAndRetainsRestrictedHistory()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=ownedName;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var client=PartyDemoSeed.ClientId(3);var origin=PartyDemoSeed.RelationshipId(3,1);var target=PartyDemoSeed.RelationshipId(3,2);var person=ContactDemoSeed.PersonId(1);
        var shared=NewFlag();var hidden=NewFlag();hidden.AgencyInstruction=null;
        var grant=new FlagVisibility {FlagId=shared.Id,ClientId=client,RelationshipId=target};
        Guid user;ActorContext staff=null!;
        try
        {
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
                user=await db.Set<StaffUser>().Where(x=>x.Email=="servicing@cover.example").Select(x=>x.Id).SingleAsync();staff=new ActorContext(user,null,null,new HashSet<string>{"servicing"});
                var scope=new SupportFlagScope(staff);
                Assert.Equal(client,await scope.ValidateMembershipAsync(db,origin,person,[origin,target]));
                foreach(var invalid in new[] {new[] {PartyDemoSeed.RelationshipId(1,1)},new[] {Guid.NewGuid()},new[] {target,target}})
                    await Assert.ThrowsAsync<SupportFlagAccessException>(()=>scope.ValidateMembershipAsync(db,origin,person,invalid));
                await Assert.ThrowsAsync<SupportFlagAccessException>(()=>scope.ValidateMembershipAsync(db,origin,ContactDemoSeed.PersonId(2),[target]));
                await Assert.ThrowsAsync<SupportFlagAccessException>(()=>scope.ValidateMembershipAsync(db,origin,Guid.NewGuid(),[]));
                // Rejected consent never reaches the persistence boundary or leaves cached/audit detail.
                const string sensitive="FICTIONAL-DECLINED-DETAIL";
                Assert.Throws<PartyValidationException>(()=>SupportFlagRules.Validate(new FlagWrite("vulnerability","health",sensitive,"declined",new DateOnly(2026,9,14),sensitive,[]),new DateOnly(2026,9,14)));
                Assert.False(await db.Set<SupportFlag>().AnyAsync());Assert.False(await db.Set<IdempotencyRecord>().AnyAsync());
                Assert.False(await db.Set<AuditEvent>().AnyAsync(x=>x.After!=null && x.After.Contains(sensitive)));
                db.AddRange(shared,hidden,grant);
                db.Add(new SupportFlagHistory {FlagId=shared.Id,ActorId=user,CreatedBy=user,Action="created",Reason="Fictional restricted provenance",Snapshot=JsonSerializer.Serialize(new {id=shared.Id,internalInstruction=shared.InternalInstruction})});
                await db.SaveChangesAsync();Assert.False(db.Database.HasPendingModelChanges());
            }
            await Reject(options,db=>db.Add(new FlagVisibility {FlagId=shared.Id,ClientId=client,RelationshipId=target}),2601,2627);
            await Reject(options,db=>db.Add(new FlagVisibility {FlagId=shared.Id,ClientId=client,RelationshipId=PartyDemoSeed.RelationshipId(1,1)}),547);
            await Reject(options,db=>db.Add(new FlagVisibility {FlagId=shared.Id,ClientId=PartyDemoSeed.ClientId(1),RelationshipId=PartyDemoSeed.RelationshipId(1,1)}),547);
            await Reject(options,db=>{var bad=NewFlag();bad.PersonId=Guid.NewGuid();db.Add(bad);},547);
            await Reject(options,db=>{var bad=NewFlag();bad.ConsentBasis="declined";db.Add(bad);},547);
            await Reject(options,db=>{var bad=NewFlag();bad.InternalCategory="unknown";db.Add(bad);},547);
            await Reject(options,db=>{var bad=NewFlag();bad.CreatedAt=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(1));db.Add(bad);},547);
            await Reject(options,db=>db.Add(new SupportFlagHistory {FlagId=shared.Id,ActorId=user,Action="amended",Reason="Fictional invalid JSON",Snapshot="not-json"}),547);
            await using(var fresh=new BackOfficeDbContext(options))
            await using(var stale=new BackOfficeDbContext(options))
            {
                var current=await fresh.Set<SupportFlag>().SingleAsync(x=>x.Id==hidden.Id);var previous=await stale.Set<SupportFlag>().SingleAsync(x=>x.Id==hidden.Id);
                current.Reason="Fictional latest review";await fresh.SaveChangesAsync();previous.Reason="Fictional stale review";
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>stale.SaveChangesAsync());
            }
            var firstAgency=new SupportFlagScope(new ActorContext(user,null,PartyDemoSeed.FirstAgencyId,new HashSet<string>{"agency-admin"}));
            var secondAgency=new SupportFlagScope(new ActorContext(user,null,PartyDemoSeed.SecondAgencyId,new HashSet<string>{"agency-admin"}));
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal(2,await new SupportFlagScope(staff).InternalFlags(db).CountAsync());Assert.Single(await new SupportFlagScope(staff).InternalHistory(db).ToListAsync());
                Assert.Empty(await firstAgency.InternalFlags(db).ToListAsync());Assert.Empty(await secondAgency.InternalHistory(db).ToListAsync());
                foreach(var role in new[] {"agency-admin","finance","system-admin"})
                    Assert.Empty(await new SupportFlagScope(staff with {Roles=new HashSet<string>{role}}).InternalFlags(db).ToListAsync());
                Assert.Empty(await firstAgency.SafeInstructions(db,origin).ToListAsync());Assert.Empty(await firstAgency.SafeInstructions(db,target).ToListAsync());
                var safe=Assert.Single(await secondAgency.SafeInstructions(db,target).ToListAsync());Assert.Equal("Allow additional time.",safe.Instruction);
                var json=JsonSerializer.Serialize(safe,new JsonSerializerOptions(JsonSerializerDefaults.Web));
                using var parsed=JsonDocument.Parse(json);Assert.Equal(new[] {"id","instruction","personId","reviewOn"},parsed.RootElement.EnumerateObject().Select(x=>x.Name).Order());
                Assert.DoesNotContain("FICTIONAL-INTERNAL",json);Assert.DoesNotContain(hidden.Id.ToString(),json);
                Assert.Single(await new SupportFlagScope(staff).SafeInstructions(db,target).ToListAsync());
                var update=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE [SupportFlagHistory] SET [Reason]='tampered'"));Assert.Equal(51005,update.Number);
                var delete=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlRawAsync("DELETE FROM [SupportFlagHistory]"));Assert.Equal(51005,delete.Number);
                db.Remove(await db.Set<FlagVisibility>().SingleAsync(x=>x.Id==grant.Id));await db.SaveChangesAsync();
                Assert.Empty(await secondAgency.SafeInstructions(db,target).ToListAsync());
                db.Add(new FlagVisibility {FlagId=shared.Id,ClientId=client,RelationshipId=target});await db.SaveChangesAsync();
                var relationship=await db.Set<ClientAgencyRelationship>().SingleAsync(x=>x.Id==target);relationship.State="inactive";await db.SaveChangesAsync();
                Assert.Empty(await secondAgency.SafeInstructions(db,target).ToListAsync());
                await Assert.ThrowsAsync<SupportFlagAccessException>(()=>new SupportFlagScope(staff).ValidateMembershipAsync(db,origin,person,[target]));
                relationship.State="active";await db.SaveChangesAsync();Assert.Single(await secondAgency.SafeInstructions(db,target).ToListAsync());
                var contact=await db.Set<Contact>().SingleAsync(x=>x.Id==ContactDemoSeed.ContactId(3));contact.IsPrimary=false;contact.EndedAt=DateTimeOffset.UtcNow;contact.EndedBy=user;contact.EndReason="Fictional contact ended";await db.SaveChangesAsync();
                Assert.Empty(await secondAgency.SafeInstructions(db,target).ToListAsync());
                db.Add(new FlagVisibility {FlagId=shared.Id,ClientId=client,RelationshipId=origin});await db.SaveChangesAsync();Assert.Single(await firstAgency.SafeInstructions(db,origin).ToListAsync());
                var flag=await db.Set<SupportFlag>().SingleAsync(x=>x.Id==shared.Id);flag.EndedAt=DateTimeOffset.UtcNow;flag.EndedBy=user;flag.Reason="Fictional resolution";await db.SaveChangesAsync();
                Assert.Empty(await firstAgency.SafeInstructions(db,origin).ToListAsync());Assert.Single(await new SupportFlagScope(staff).InternalHistory(db).ToListAsync());
            }
        }
        finally
        {
            if(connection.InitialCatalog!=ownedName)throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();
        }
        SupportFlag NewFlag()=>new() {ClientId=client,PersonId=person,OriginRelationshipId=origin,TypeCode="vulnerability",InternalCategory="health",
            InternalInstruction="FICTIONAL-INTERNAL-INSTRUCTION",AgencyInstruction="Allow additional time.",ConsentBasis="written-consent",ReviewOn=new DateOnly(2027,1,1),Reason="FICTIONAL-INTERNAL-REASON"};
    }
    private static async Task Reject(DbContextOptions<BackOfficeDbContext> options,Action<BackOfficeDbContext> change,params int[] codes)
    {
        await using var db=new BackOfficeDbContext(options);change(db);var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number,codes);
    }
}
