using System.Security.Cryptography;
using BackOffice.Application;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class ClientTests
{
    [Fact]
    public async Task RealSqlClientIdentityReferencesRelationshipIntegrityAndRepeatSeed()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");
        connection.InitialCatalog=ownedName;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql => sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        try
        {
            await using (var seed=new BackOfficeDbContext(options))
            {
                await seed.Database.MigrateAsync();await DemoDatabase.SeedAsync(seed,password);
                var edited=await seed.Set<ClientAccount>().SingleAsync(x => x.Id==PartyDemoSeed.ClientId(1));
                edited.LegalName="Fictional revised declared name";edited.NormalizedName="FICTIONAL REVISED DECLARED NAME";
                await seed.SaveChangesAsync();
                await DemoDatabase.SeedAsync(seed,password);await seed.Database.MigrateAsync();
                Assert.False(seed.Database.HasPendingModelChanges());
            }
            await using (var reload=new BackOfficeDbContext(options))
            {
                Assert.Equal(32,await reload.Set<ClientAccount>().CountAsync());
                Assert.Equal(2,await reload.Set<Agency>().CountAsync());
                Assert.All(await reload.Set<Agency>().ToListAsync(),x => Assert.Equal("draft",x.State));
                Assert.Equal(35,await reload.Set<ClientAgencyRelationship>().CountAsync());
                Assert.Equal(32,await reload.Set<ClientActivity>().CountAsync());
                var edited=await reload.Set<ClientAccount>().SingleAsync(x => x.Id==PartyDemoSeed.ClientId(1));
                Assert.Equal("Fictional revised declared name",edited.LegalName);
                Assert.Equal("CN-0000001",edited.Reference);
                await Assert.ThrowsAsync<InvalidOperationException>(() => ClientReferences.NextAsync(reload));
                var internalActor=new ActorContext(Guid.NewGuid(),null,null,new HashSet<string>{"servicing"});
                var firstActor=internalActor with {AgencyId=PartyDemoSeed.FirstAgencyId,Roles=new HashSet<string>{"agency-admin"}};
                var secondActor=firstActor with {AgencyId=PartyDemoSeed.SecondAgencyId};
                var firstScope=new PartyScope(firstActor);var secondScope=new PartyScope(secondActor);
                Assert.Equal(32,await new PartyScope(internalActor).Clients(reload).CountAsync());
                Assert.Equal(0,await new PartyScope(internalActor with {Roles=new HashSet<string>{"system-admin"}}).Clients(reload).CountAsync());
                var firstIds=await firstScope.Clients(reload).Select(x=>x.Id).ToArrayAsync();
                var secondIds=await secondScope.Clients(reload).Select(x=>x.Id).ToArrayAsync();
                Assert.Equal(3,firstIds.Intersect(secondIds).Count());Assert.Equal(32,firstIds.Union(secondIds).Count());
                Assert.All(await firstScope.Relationships(reload).ToListAsync(),x=>Assert.Equal(PartyDemoSeed.FirstAgencyId,x.AgencyId));
                Assert.Equal(1,await firstScope.Agencies(reload).CountAsync());
                Assert.Equal(0,await firstScope.Activity(reload).CountAsync());
                var visibleEvent=new ClientActivity {ClientId=PartyDemoSeed.ClientId(1),RelationshipId=PartyDemoSeed.RelationshipId(1,1),EventType="client.updated"};
                reload.Add(visibleEvent);await reload.SaveChangesAsync();
                Assert.True(await firstScope.Activity(reload).AnyAsync(x=>x.Id==visibleEvent.Id));
                Assert.False(await secondScope.Activity(reload).AnyAsync(x=>x.Id==visibleEvent.Id));
                Assert.Equal(0,await new PartyScope(firstActor with {AgencyId=Guid.NewGuid()}).Clients(reload).CountAsync());
                // Revocation takes effect in the shared SQL predicates before counts or projection.
                var revoked=await reload.Set<ClientAgencyRelationship>().SingleAsync(x=>x.Id==PartyDemoSeed.RelationshipId(1,1));
                revoked.State="inactive";await reload.SaveChangesAsync();
                Assert.False(await firstScope.Clients(reload).AnyAsync(x=>x.Id==PartyDemoSeed.ClientId(1)));
                Assert.False(await firstScope.Activity(reload).AnyAsync(x=>x.Id==visibleEvent.Id));
                Assert.True(await secondScope.Clients(reload).AnyAsync(x=>x.Id==PartyDemoSeed.ClientId(1)));
            }
            var refs=await Task.WhenAll(Enumerable.Range(1,12).Select(async index =>
            {
                await using var writer=new BackOfficeDbContext(options);await using var tx=await writer.Database.BeginTransactionAsync();
                var reference=await ClientReferences.NextAsync(writer);
                writer.Add(Client(reference));await writer.SaveChangesAsync();await tx.CommitAsync();return reference;
            }));
            Assert.Equal(12,refs.Distinct().Count());
            string abandoned;
            await using (var rollback=new BackOfficeDbContext(options))
            {
                await using var tx=await rollback.Database.BeginTransactionAsync();
                abandoned=await ClientReferences.NextAsync(rollback);rollback.Add(Client(abandoned));await rollback.SaveChangesAsync();
                await tx.RollbackAsync();
            }
            await using (var check=new BackOfficeDbContext(options))
            {
                Assert.False(await check.Set<ClientAccount>().AnyAsync(x => x.Reference==abandoned));
                await using var tx=await check.Database.BeginTransactionAsync();
                Assert.NotEqual(abandoned,await ClientReferences.NextAsync(check));await tx.CommitAsync();
            }
            await Reject(options,db=>db.Add(Client("CN-0000001")),2601,2627);
            await Reject(options,db=>{var invalid=Client("bad-json");invalid.Address="invalid";db.Add(invalid);},547);
            await Reject(options,db=>db.Add(new ClientAgencyRelationship {ClientId=PartyDemoSeed.ClientId(1),AgencyId=PartyDemoSeed.FirstAgencyId}),2601,2627);
            await Reject(options,db=>db.Add(new ClientAgencyRelationship {ClientId=PartyDemoSeed.ClientId(1),AgencyId=Guid.NewGuid()}),547);
            await Reject(options,db=>db.Add(new ClientActivity {ClientId=PartyDemoSeed.ClientId(2),RelationshipId=PartyDemoSeed.RelationshipId(1,1),EventType="client.updated"}),547);
            await Reject(options,db=>db.Add(new ClientActivity {ClientId=PartyDemoSeed.ClientId(1),EventType="client.updated",OccurredAt=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(1))}),547);
            await using (var check=new BackOfficeDbContext(options))
            {
                var update=await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlRawAsync("UPDATE [ClientActivity] SET [EventType]='tampered'"));Assert.Equal(51004,update.Number);
                var delete=await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlRawAsync("DELETE FROM [ClientActivity]"));Assert.Equal(51004,delete.Number);
            }
            await using var first=new BackOfficeDbContext(options);await using var second=new BackOfficeDbContext(options);
            var current=await first.Set<ClientAccount>().SingleAsync(x=>x.Id==PartyDemoSeed.ClientId(1));
            var stale=await second.Set<ClientAccount>().SingleAsync(x=>x.Id==current.Id);
            current.LegalName="Fictional newer version";await first.SaveChangesAsync();
            stale.LegalName="Stale overwrite";await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>second.SaveChangesAsync());
        }
        finally
        {
            if(connection.InitialCatalog!=ownedName)throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static ClientAccount Client(string reference) => new() {Reference=reference,LegalName="Fictional test business",NormalizedName="FICTIONAL TEST BUSINESS",EntityType="limited-company",Address="{\"line1\":\"1 Fictional Road\",\"town\":\"Sheffield\",\"postcode\":\"S1 1AA\",\"country\":\"GB\"}"};
    private static async Task Reject(DbContextOptions<BackOfficeDbContext> options,Action<BackOfficeDbContext> arrange,params int[] numbers)
    {
        await using var db=new BackOfficeDbContext(options);arrange(db);
        var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number,numbers);
    }
}
