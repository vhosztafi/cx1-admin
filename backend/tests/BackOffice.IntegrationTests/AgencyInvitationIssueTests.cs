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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyInvitationIssueTests
{
    [Fact]
    public async Task RealSqlAgencyInvitationIssueBindsTokenAndDeliveryWithRollbackAndStaleSuppression()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        try
        {
            ActorContext actor;Guid scenarioId;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();Assert.False(db.Database.HasPendingModelChanges());await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
                var staff=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");actor=new(staff.Id,staff.TeamId,null,new HashSet<string>{"agency-admin"});
                var scenario=new SettingVersion{Scope="agency-notification",Version=1,Values="{\"demo\":true,\"scenario\":\"pass\"}",EffectiveFrom=DateTimeOffset.UtcNow};db.Add(scenario);await db.SaveChangesAsync();scenarioId=scenario.Id;
            }
            var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);
            foreach(var testCase in new[]{"delivered","delivered-then-revoked","revoked","expired","rollback","retry"})
            {
                var clock=new Clock();var payload=new AgencyNotificationPayload(new EphemeralDataProtectionProvider());var commands=new SqlCommandBoundary(factory,clock);
                var drafts=new AgencyDraftService(factory,commands,clock);var users=new AgencyUserService(drafts,commands,clock);
                var notificationService=new AgencyNotificationService(payload,clock);var issuer=new InvitationService(notificationService,clock);
                using var details=JsonDocument.Parse("{\"legalName\":\"Fictional invitation issue test\"}");
                var agency=await drafts.Save(actor,null,Guid.NewGuid().ToString(),null,AgencyDraftRules.Validate(details.RootElement),2,null,default);
                var staged=await users.Stage(actor,agency.ResourceId,Guid.NewGuid().ToString(),Convert.FromBase64String(agency.Etag!.Trim('"')),AgencyUserRules.Validate(testCase+"@cover.example","Fictional invited broker","broker-user"));
                var invitationId=JsonDocument.Parse(staged.Body).RootElement.GetProperty("invitationId").GetGuid();
                await using(var db=new BackOfficeDbContext(options))
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(()=>issuer.IssueStaged(db,agency.ResourceId,invitationId,actor.UserId,scenarioId));
                    await using(var tx=await db.Database.BeginTransactionAsync())
                    {Assert.Equal(409,(await Assert.ThrowsAsync<AgencyCommandException>(()=>issuer.IssueStaged(db,agency.ResourceId,invitationId,actor.UserId,scenarioId))).Status);await tx.RollbackAsync();}
                    db.ChangeTracker.Clear();var active=await db.Set<Agency>().SingleAsync(x=>x.Id==agency.ResourceId);active.State="active";await db.SaveChangesAsync();
                    await using var issue=await db.Database.BeginTransactionAsync();
                    Assert.Equal(invitationId,await issuer.IssueStaged(db,agency.ResourceId,invitationId,actor.UserId,scenarioId));
                    if(testCase=="rollback")await issue.RollbackAsync();else await issue.CommitAsync();
                }
                Guid workId,notificationId;string raw;
                await using(var db=new BackOfficeDbContext(options))
                {
                    var invitation=await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==invitationId);
                    if(testCase=="rollback")
                    {Assert.Equal("staged",invitation.State);Assert.Null(invitation.TokenHash);Assert.False(await db.Set<AgencyNotification>().AnyAsync(x=>x.AgencyId==agency.ResourceId));Assert.False(await db.Set<OutboxWork>().AnyAsync(x=>x.SubjectRecordId==agency.ResourceId));continue;}
                    Assert.Equal("pending",invitation.State);Assert.Equal(clock.GetUtcNow().AddDays(14),invitation.ExpiresAt);
                    var message=await db.Set<AgencyNotification>().SingleAsync(x=>x.Id==invitation.NotificationId);notificationId=message.Id;workId=message.WorkId;Assert.Equal(invitationId,message.InvitationId);
                    raw=payload.Unprotect(agency.ResourceId,message.Id,message.ProtectedPayload).Content;
                    Assert.True(InvitationToken.TryHash(raw,out var hash));Assert.Equal(invitation.TokenHash,hash);
                    Assert.DoesNotContain(raw,(await db.Set<OutboxWork>().SingleAsync(x=>x.Id==workId)).Payload);
                    Assert.DoesNotContain(await db.Set<IdempotencyRecord>().ToListAsync(),x=>x.ResultBody.Contains(raw));
                    Assert.False(await db.Set<UserCredential>().AnyAsync(x=>x.UserId==staged.ResourceId));
                    await using var repeated=await db.Database.BeginTransactionAsync();
                    Assert.Equal(409,(await Assert.ThrowsAsync<AgencyCommandException>(()=>issuer.IssueStaged(db,agency.ResourceId,invitationId,actor.UserId,scenarioId))).Status);
                    await repeated.RollbackAsync();
                }
                if(testCase=="revoked")
                {await using var db=new BackOfficeDbContext(options);var invitation=await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==invitationId);invitation.State="revoked";invitation.RevokedAt=clock.GetUtcNow();await db.SaveChangesAsync();}
                if(testCase=="expired")clock.Advance(TimeSpan.FromDays(14));
                var leases=new SqlJobLeases(factory,clock);var worker=new AgencyNotificationWorker(factory,payload,clock);
                if(testCase=="retry")
                {
                    async Task Exhaust(int count){for(var i=0;i<count;i++){var attempt=(await leases.ClaimWorkAsync(AgencyNotificationService.Kind,workId))!;await leases.FailAsync(attempt,JobFailure.ProviderUnavailable);clock.Advance(TimeSpan.FromHours(1));}}
                    await Exhaust(6);byte[] version;
                    await using(var db=new BackOfficeDbContext(options)){version=(await db.Set<OutboxWork>().SingleAsync(x=>x.Id==workId)).RowVersion;}
                    var retry=new AgencyNotificationRetry(drafts,commands,clock);var key=Guid.NewGuid().ToString();
                    var recovered=await retry.Execute(actor,agency.ResourceId,notificationId,key,version,"Fictional invitation outage recovery",default);Assert.Equal(202,recovered.Status);
                    Assert.True((await retry.Execute(actor,agency.ResourceId,notificationId,key,version,"Fictional invitation outage recovery",default)).Replayed);
                    await Exhaust(6);
                    await using(var db=new BackOfficeDbContext(options))
                    {var invitation=await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==invitationId);invitation.State="revoked";invitation.RevokedAt=clock.GetUtcNow();await db.SaveChangesAsync();version=(await db.Set<OutboxWork>().SingleAsync(x=>x.Id==workId)).RowVersion;}
                    Assert.Equal(409,(await Assert.ThrowsAsync<AgencyCommandException>(()=>retry.Execute(actor,agency.ResourceId,notificationId,Guid.NewGuid().ToString(),version,"Revoked recovery denied",default))).Status);
                    continue;
                }
                var lease=(await leases.ClaimWorkAsync(AgencyNotificationService.Kind,workId))!;
                if(testCase.StartsWith("delivered",StringComparison.Ordinal))
                {
                    var receipt=(await worker.Deliver(lease))!.Value;
                    if(testCase=="delivered-then-revoked")
                    {await using var db=new BackOfficeDbContext(options);var invitation=await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==invitationId);invitation.State="revoked";invitation.RevokedAt=clock.GetUtcNow();await db.SaveChangesAsync();}
                    Assert.Equal(receipt,await worker.Deliver(lease));Assert.True(await worker.Apply(lease,receipt));
                }
                else
                {var failure=await Assert.ThrowsAsync<AgencyNotificationProviderException>(()=>worker.Deliver(lease));Assert.Equal(JobFailure.Superseded,failure.Failure);await leases.FailAsync(lease,failure.Failure);}
                await using(var db=new BackOfficeDbContext(options))
                {
                    var job=await db.Set<OutboxWork>().SingleAsync(x=>x.Id==workId);Assert.Equal(testCase.StartsWith("delivered",StringComparison.Ordinal)?"succeeded":"failed",job.State);
                    if(!testCase.StartsWith("delivered",StringComparison.Ordinal))Assert.Equal("invitation-superseded",job.ErrorCode);
                    Assert.Equal(testCase.StartsWith("delivered",StringComparison.Ordinal)?1:0,await db.Set<AgencyNotificationReceipt>().CountAsync(x=>x.NotificationId==notificationId));
                    Assert.Equal("invited",(await db.Set<StaffUser>().SingleAsync(x=>x.Id==staged.ResourceId)).State);Assert.False(await db.Set<UserCredential>().AnyAsync(x=>x.UserId==staged.ResourceId));
                    Assert.DoesNotContain(await db.Set<AdapterAttempt>().Where(x=>x.WorkId==workId).ToListAsync(),x=>x.Request.Contains(raw)||(x.Response??"").Contains(raw));
                }
            }
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private sealed class Clock:TimeProvider{private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance(TimeSpan value)=>now+=value;}
}
