using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed class AgencyTermsServiceTests
{
    [Fact]public async Task RealSqlTermsDecisionsAreIndependentAtomicReplayableAndVersionBound()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        try
        {
            ActorContext requester,reviewer,otherReviewer;Guid agencyId,productId;byte[] initialBase;
            var clock=new MovingClock();var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
                async Task<ActorContext> Actor(string email,string role){var user=await db.Set<StaffUser>().SingleAsync(x=>x.Email==email);return new(user.Id,user.TeamId,null,new HashSet<string>{role});}
                requester=await Actor("agency-admin@cover.example","agency-admin");reviewer=await Actor("agency-reviewer@cover.example","agency-admin");otherReviewer=await Actor("system-admin@cover.example","system-admin");
                productId=await db.Set<ProductVersion>().Select(x=>x.Id).FirstAsync();
                var agency=new Agency{Reference="AG-TERMS-SERVICE",LegalName="Fictional approved terms fixture",State="active"};agencyId=agency.Id;db.Add(agency);await db.SaveChangesAsync();
                // Initial activation remains a separate workflow: establish its storage
                // provenance explicitly to exercise subsequent terms commands only.
                var activation=new AgencyStateRequest{AgencyId=agencyId,BaseVersion=agency.RowVersion,ProposedInputFingerprint=new string('a',64),RequestedBy=requester.UserId,CreatedBy=requester.UserId,RequestReason="Fictional fixture activation"};db.Add(activation);await db.SaveChangesAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'applied',DecisionBy={reviewer.UserId},DecisionReason=N'Fixture approval',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={activation.Id}");
                var snapshot=Input(today,productId);snapshot.Remove("reason");
                db.Add(new AgencyTermsVersion{AgencyId=agencyId,Version=1,EffectiveFrom=today,ApprovedStateRequestId=activation.Id,CreatedBy=reviewer.UserId,Snapshot=snapshot.ToJsonString()});await db.SaveChangesAsync();initialBase=agency.RowVersion.ToArray();
            }
            var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);var boundary=new SqlCommandBoundary(factory,clock);var drafts=new AgencyDraftService(factory,boundary,clock);var service=new AgencyTermsService(drafts,boundary,clock);
            string Key()=>Guid.NewGuid().ToString();byte[] Version(CommandOutcome result)=>Convert.FromBase64String(result.Etag!.Trim('"'));
            async Task<byte[]> Base(){await using var db=new BackOfficeDbContext(options);return await db.Set<Agency>().Where(x=>x.Id==agencyId).Select(x=>x.RowVersion).SingleAsync();}
            async Task<CommandOutcome> Propose(string key,byte[] basis,DateOnly date){using var doc=JsonDocument.Parse(Input(date,productId).ToJsonString());return await service.Propose(requester,agencyId,key,basis,doc.RootElement);}
            async Task Status(int expected,Func<Task<CommandOutcome>> action)=>Assert.Equal(expected,(await Assert.ThrowsAsync<AgencyCommandException>(action)).Status);
            var requestKey=Key();var proposal=await Propose(requestKey,initialBase,today.AddDays(1));Assert.Equal(202,proposal.Status);Assert.Equal(initialBase,await Base());
            Assert.True((await Propose(requestKey,initialBase,today.AddDays(1))).Replayed);
            await Status(409,()=>Propose(Key(),initialBase,today.AddDays(1)));
            await Status(403,()=>service.Decide(requester,agencyId,proposal.ResourceId,Key(),Version(proposal),true,"Self approval"));
            await Status(403,()=>service.Decide(reviewer with{Roles=new HashSet<string>{"underwriter"}},agencyId,proposal.ResourceId,Key(),Version(proposal),true,"Wrong authority"));
            var fail=new FailPublication();var failingOptions=new DbContextOptionsBuilder<BackOfficeDbContext>(options).AddInterceptors(fail).Options;
            var failing=new AgencyTermsService(drafts,new SqlCommandBoundary(new PooledDbContextFactory<BackOfficeDbContext>(failingOptions),clock),clock);
            var failedKey=Key();
            await Assert.ThrowsAsync<InvalidOperationException>(()=>failing.Decide(reviewer,agencyId,proposal.ResourceId,failedKey,Version(proposal),true,"Injected rollback"));
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal("pending",(await db.Set<AgencyTermsRequest>().SingleAsync(x=>x.Id==proposal.ResourceId)).State);
                Assert.Equal(1,await db.Set<AgencyTermsVersion>().CountAsync(x=>x.AgencyId==agencyId));Assert.Equal(initialBase,await Base());
                Assert.False(await db.Set<IdempotencyRecord>().AnyAsync(x=>x.Key==failedKey));Assert.False(await db.Set<AgencyActivity>().AnyAsync(x=>x.AgencyId==agencyId));
            }
            var firstKey=Key();var secondKey=Key();
            async Task<(int Status,ActorContext Actor,string Key,CommandOutcome? Result)> Decide(ActorContext actor,string key)
            {try{return(200,actor,key,await service.Decide(actor,agencyId,proposal.ResourceId,key,Version(proposal),true,"Independent approval"));}catch(AgencyCommandException error){return(error.Status,actor,key,null);}}
            var race=await Task.WhenAll(Decide(reviewer,firstKey),Decide(otherReviewer,secondKey));Assert.Single(race,x=>x.Status==200);Assert.Single(race,x=>x.Status==412);
            var winner=race.Single(x=>x.Status==200);var replay=await service.Decide(winner.Actor,agencyId,proposal.ResourceId,winner.Key,Version(proposal),true,"Independent approval");Assert.True(replay.Replayed);Assert.Equal(winner.Result!.Etag,replay.Etag);
            Assert.NotEqual(initialBase,await Base());
            await using(var db=new BackOfficeDbContext(options))
            {
                var versions=await db.Set<AgencyTermsVersion>().Where(x=>x.AgencyId==agencyId).OrderBy(x=>x.Version).ToListAsync();Assert.Equal(2,versions.Count);Assert.Equal(today,versions[0].EffectiveFrom);Assert.Equal(today.AddDays(1),versions[1].EffectiveFrom);
                Assert.Equal(2,await db.Set<AgencyProduct>().CountAsync(x=>db.Set<AgencyTermsVersion>().Any(v=>v.Id==x.AgencyTermsVersionId&&v.AgencyId==agencyId)));
                Assert.Equal("active",(await db.Set<Agency>().SingleAsync(x=>x.Id==agencyId)).State);
            }
            var next=await Propose(Key(),await Base(),today.AddDays(2));
            await using(var db=new BackOfficeDbContext(options)){var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==agencyId);agency.LegalName="Fictional changed base";await db.SaveChangesAsync();}
            await Status(409,()=>service.Decide(reviewer,agencyId,next.ResourceId,Key(),Version(next),true,"Stale approval"));
            var replacement=await Propose(Key(),await Base(),today.AddDays(2));
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal("stale",(await db.Set<AgencyTermsRequest>().SingleAsync(x=>x.Id==next.ResourceId)).State);
                var rule=await db.Set<SettingVersion>().SingleAsync(x=>x.Scope=="agency-distribution");db.Add(new SettingVersion{Scope=rule.Scope,Version=2,EffectiveFrom=clock.GetUtcNow(),Values=rule.Values});await db.SaveChangesAsync();
            }
            await Status(409,()=>service.Decide(reviewer,agencyId,replacement.ResourceId,Key(),Version(replacement),true,"Old rule approval"));
            var beforeReject=await Base();await service.Decide(reviewer,agencyId,replacement.ResourceId,Key(),Version(replacement),false,"Reject stale rule proposal");Assert.Equal(beforeReject,await Base());
            // A committed proposal remains replayable after its date has passed.
            clock.Offset=TimeSpan.FromDays(3);Assert.True((await Propose(requestKey,initialBase,today.AddDays(1))).Replayed);
            await using(var db=new BackOfficeDbContext(options)){var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==requester.UserId);user.State="suspended";await db.SaveChangesAsync();}
            await Status(403,()=>Propose(requestKey,initialBase,today.AddDays(1)));
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
    private static JsonObject Input(DateOnly date,Guid product)=>JsonNode.Parse(JsonSerializer.Serialize(new{effectiveFrom=date.ToString("yyyy-MM-dd"),reason="Fictional changed terms",commercialTerms=new{effectiveFrom=date.ToString("yyyy-MM-dd"),commissionBasis="per-product",feeSharing="none",volumeCommitmentMode="none",minimumPremiumOverrideMode="none",referralRouting="standard-internal-underwriting"},settlement=new{statementCycle="monthly",method="bank-transfer",premiumCollection="agency",commissionSettlement="net-remittance"},paymentTermsDays=30,creditLimit="1000.00",products=new[]{new{productVersionId=product,effectiveFrom=date.ToString("yyyy-MM-dd"),brokerCommissionBasisPoints=1250}}}))!.AsObject();
    private sealed class MovingClock:TimeProvider{public TimeSpan Offset;public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow+Offset;}
    private sealed class FailPublication:SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {if(data.Context!.ChangeTracker.Entries<AgencyTermsVersion>().Any(x=>x.State==EntityState.Added))throw new InvalidOperationException("Injected failure after decision before publication.");return ValueTask.FromResult(result);}
    }
}
