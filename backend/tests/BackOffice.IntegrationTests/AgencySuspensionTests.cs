using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencySuspensionTests
{
    [Fact]
    public async Task RealSqlSuspensionRequiresIndependentReviewAndRevokesAccessAtomically()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var id=PartyDemoSeed.FirstAgencyId;
        try
        {
            ActorContext actor,reviewer,other;Guid pendingInvitation;
            var clock=TimeProvider.System;var payload=new AgencyNotificationPayload(new EphemeralDataProtectionProvider());
            var notifications=new AgencyNotificationService(payload,clock);var issuer=new InvitationService(notifications,clock);
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Guid.NewGuid().ToString("N")+"a1",includeMatches:true);
                async Task<ActorContext> Actor(string email,string role){var user=await db.Set<StaffUser>().SingleAsync(x=>x.Email==email);return new(user.Id,user.TeamId,null,new HashSet<string>{role});}
                actor=await Actor("agency-admin@cover.example","agency-admin");reviewer=await Actor("agency-reviewer@cover.example","agency-admin");other=await Actor("system-admin@cover.example","system-admin");
                var role=await db.Set<Role>().Where(x=>x.Code=="broker-admin").Select(x=>x.Id).SingleAsync();
                var users=new[]{User("active",id),User("invited",id),User("suspended",id),User("invited",id),User("active",PartyDemoSeed.SecondAgencyId)};
                var states=users.Select(x=>x.State).ToArray();foreach(var user in users)user.State="invited";
                db.AddRange(users);await db.SaveChangesAsync();
                foreach(var user in users)db.Add(new UserRole{UserId=user.Id,RoleId=role});await db.SaveChangesAsync();
                for(var index=0;index<users.Length;index++)users[index].State=states[index];
                foreach(var user in users)
                {
                    db.Add(new UserSession{UserId=user.Id,TokenHash=RandomNumberGenerator.GetBytes(32),SecurityStamp=user.SecurityStamp,ExpiresAt=clock.GetUtcNow().AddDays(1),LastSeenAt=clock.GetUtcNow(),DeviceLabel="Fictional suspension test",TicketCiphertext=[1]});
                    if(user.State=="active")db.Add(new UserCredential{UserId=user.Id,ProviderSubject=user.Id.ToString(),PasswordHash="fictional-test-credential-preserved"});
                }
                var invitation=new AgencyInvitation{AgencyId=id,UserId=users[1].Id,CreatedBy=actor.UserId};pendingInvitation=invitation.Id;
                db.AddRange(invitation,new AgencyInvitation{AgencyId=id,UserId=users[3].Id,CreatedBy=actor.UserId});
                var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==id);agency.State="active";await db.SaveChangesAsync();
                await using var tx=await db.Database.BeginTransactionAsync();
                await issuer.IssueStaged(db,id,pendingInvitation,actor.UserId,await issuer.DefaultScenario(db),default);await tx.CommitAsync();
            }
            var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);var commands=new SqlCommandBoundary(factory,clock);var drafts=new AgencyDraftService(factory,commands,clock);var service=new AgencySuspensionService(drafts,commands,clock);
            string Key()=>Guid.NewGuid().ToString("N");byte[] Version(CommandOutcome result)=>Convert.FromBase64String(result.Etag!.Trim('"'));
            async Task<byte[]> Base(){await using var db=new BackOfficeDbContext(options);return await db.Set<Agency>().Where(x=>x.Id==id).Select(x=>x.RowVersion).SingleAsync();}
            async Task Status(int expected,Func<Task<CommandOutcome>> action)=>Assert.Equal(expected,(await Assert.ThrowsAsync<AgencyCommandException>(action)).Status);
            async Task<string> Access(){await using var db=new BackOfficeDbContext(options);return JsonSerializer.Serialize(new{users=await db.Set<StaffUser>().Where(x=>x.AgencyId!=null).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.State,x.SecurityStamp}).ToListAsync(),sessions=await db.Set<UserSession>().OrderBy(x=>x.Id).ToListAsync(),invitations=await db.Set<AgencyInvitation>().OrderBy(x=>x.Id).ToListAsync(),credentials=await db.Set<UserCredential>().OrderBy(x=>x.Id).ToListAsync()});}
            async Task<string> History(){await using var db=new BackOfficeDbContext(options);return JsonSerializer.Serialize(new{clients=await db.Set<ClientAccount>().OrderBy(x=>x.Id).ToListAsync(),relationships=await db.Set<ClientAgencyRelationship>().OrderBy(x=>x.Id).ToListAsync(),matches=await db.Set<MatchReview>().OrderBy(x=>x.Id).ToListAsync(),intakes=await db.Set<MatchSubmission>().OrderBy(x=>x.Id).ToListAsync(),terms=await db.Set<AgencyTermsVersion>().OrderBy(x=>x.Id).ToListAsync()});}
            var before=await Access();var history=await History();var basis=await Base();var key=Key();
            await Status(422,()=>service.Propose(actor,id,Key(),basis," "));
            var request=await service.Propose(actor,id,key,basis,"Fictional suspension requiring review");Assert.Equal(202,request.Status);
            Assert.Equal(basis,await Base());Assert.Equal(before,await Access());
            var proposalReplay=await service.Propose(actor,id,key,basis,"Fictional suspension requiring review");Assert.True(proposalReplay.Replayed);Assert.Equal(request.Etag,proposalReplay.Etag);
            await Status(409,()=>service.Propose(actor,id,Key(),basis,"Duplicate proposal"));
            await Status(403,()=>service.Decide(actor,id,request.ResourceId,Key(),Version(request),true,"Self approval"));
            await Status(404,()=>service.Decide(reviewer,PartyDemoSeed.SecondAgencyId,request.ResourceId,Key(),Version(request),true,"Wrong agency"));
            await Status(412,()=>service.Decide(reviewer,id,request.ResourceId,Key(),new byte[8],true,"Wrong request version"));
            await service.Decide(reviewer,id,request.ResourceId,Key(),Version(request),false,"Do not suspend");Assert.Equal(before,await Access());Assert.Equal(basis,await Base());
            request=await service.Propose(actor,id,Key(),basis,"Stale proposal test");
            await using(var db=new BackOfficeDbContext(options)){var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==id);agency.UpdatedAt=clock.GetUtcNow();await db.SaveChangesAsync();}
            await Status(409,()=>service.Decide(reviewer,id,request.ResourceId,Key(),Version(request),true,"Changed base"));
            var stale=request.ResourceId;basis=await Base();request=await service.Propose(actor,id,Key(),basis,"Current suspension proposal");
            await using(var db=new BackOfficeDbContext(options))Assert.Equal("stale",await db.Set<AgencyStateRequest>().Where(x=>x.Id==stale).Select(x=>x.State).SingleAsync());
            var failingFactory=new PooledDbContextFactory<BackOfficeDbContext>(new DbContextOptionsBuilder<BackOfficeDbContext>(options).AddInterceptors(new FailAfterRevocation()).Options);
            var failing=new AgencySuspensionService(drafts,new SqlCommandBoundary(failingFactory,clock),clock);var failureKey=Key();
            await Assert.ThrowsAsync<InjectedFailure>(()=>failing.Decide(reviewer,id,request.ResourceId,failureKey,Version(request),true,"Fictional rollback"));
            Assert.Equal(before,await Access());Assert.Equal(basis,await Base());
            await using(var db=new BackOfficeDbContext(options)){Assert.Equal("pending",await db.Set<AgencyStateRequest>().Where(x=>x.Id==request.ResourceId).Select(x=>x.State).SingleAsync());Assert.False(await db.Set<IdempotencyRecord>().AnyAsync(x=>x.Key==failureKey));Assert.False(await db.Set<AgencyActivity>().AnyAsync(x=>x.AgencyId==id&&x.Action=="agency.suspended"));}
            async Task<(ActorContext Actor,string Key,CommandOutcome? Result,int Status)> Race(ActorContext who){var raceKey=Key();try{var result=await service.Decide(who,id,request.ResourceId,raceKey,Version(request),true,"Independent suspension");return(who,raceKey,result,result.Status);}catch(AgencyCommandException ex){return(who,raceKey,null,ex.Status);}}
            var race=await Task.WhenAll(Race(reviewer),Race(other));Assert.Single(race,x=>x.Status==200);Assert.Single(race,x=>x.Status==412);var winner=race.Single(x=>x.Status==200);
            var replay=await service.Decide(winner.Actor,id,request.ResourceId,winner.Key,Version(request),true,"Independent suspension");Assert.True(replay.Replayed);Assert.Equal(winner.Result!.Etag,replay.Etag);
            Assert.Equal(history,await History());
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal("suspended",await db.Set<Agency>().Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
                var users=await db.Set<StaffUser>().Where(x=>x.AgencyId==id).ToListAsync();Assert.Equal(new[]{"active","invited","invited","suspended"},users.Select(x=>x.State).Order());
                var ids=users.Select(x=>x.Id).ToArray();var sessions=await db.Set<UserSession>().Where(x=>ids.Contains(x.UserId)).ToListAsync();Assert.Equal(4,sessions.Count);Assert.All(sessions,x=>{Assert.NotNull(x.RevokedAt);Assert.NotEqual(x.SecurityStamp,users.Single(u=>u.Id==x.UserId).SecurityStamp);});
                Assert.All(await db.Set<AgencyInvitation>().Where(x=>x.AgencyId==id).ToListAsync(),x=>{Assert.Equal("revoked",x.State);Assert.NotNull(x.RevokedAt);});
                Assert.NotNull((await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==pendingInvitation)).TokenHash);
                var outside=await db.Set<StaffUser>().SingleAsync(x=>x.AgencyId==PartyDemoSeed.SecondAgencyId);var outsideSession=await db.Set<UserSession>().SingleAsync(x=>x.UserId==outside.Id);Assert.Null(outsideSession.RevokedAt);Assert.Equal(outside.SecurityStamp,outsideSession.SecurityStamp);
                Assert.All(await db.Set<UserCredential>().Where(x=>ids.Contains(x.UserId)).ToListAsync(),x=>Assert.Equal("fictional-test-credential-preserved",x.PasswordHash));
                var reviewerUser=await db.Set<StaffUser>().SingleAsync(x=>x.Id==winner.Actor.UserId);reviewerUser.State="suspended";await db.SaveChangesAsync();
            }
            await Status(403,()=>service.Decide(winner.Actor,id,request.ResourceId,winner.Key,Version(request),true,"Independent suspension"));
            basis=await Base();await Status(409,()=>service.Propose(actor,id,Key(),basis,"Already suspended"));
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private static StaffUser User(string state,Guid agency)=>new(){AgencyId=agency,State=state,Email=Guid.NewGuid()+"@example.test",NormalizedEmail=Guid.NewGuid()+"@EXAMPLE.TEST",DisplayName="Fictional suspension user"};
    private sealed class InjectedFailure:Exception;
    private sealed class FailAfterRevocation:SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {if(data.Context!.ChangeTracker.Entries<AgencyActivity>().Any(x=>x.State==EntityState.Added&&x.Entity.Action=="agency.suspended"))throw new InjectedFailure();return ValueTask.FromResult(result);}
    }
}
