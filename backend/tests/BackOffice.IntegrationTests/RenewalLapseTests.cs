using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public async Task RealSqlRenewalLapseRollbackAndMissingRecipientRemainRecoverable(string product)
    {
        await WithDatabase(async(db,password)=>
        {
            var setup=await AcceptedIssue(db,password,product);var f=setup.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();
            await db.Set<Contact>().ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Email,(string?)null));
            var service=new RenewalLifecycleService(f.Factory,f.Clock);var initial=await service.ReadAsync(f.Underwriter,term.Id);
            var key=Guid.NewGuid().ToString();var version=Convert.FromBase64String(initial.Etag.Trim('"'));
            Task<BackOffice.Infrastructure.Platform.CommandOutcome> Lapse()=>service.LapseAsync(f.Underwriter,term.Id,version,"Fictional no recipient lapse",key,Guid.NewGuid());
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_TestLapseAuditFailure ON AuditEvent AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE EventType='policy.renewal-lapsed') THROW 51799,'Deliberate late lapse failure.',1; END;");
            await Assert.ThrowsAsync<DbUpdateException>(()=>Lapse());
            Assert.Empty(await db.Set<RenewalLapseEvent>().ToArrayAsync());
            Assert.False(await db.Set<OutboxWork>().AnyAsync(x=>x.Kind==RenewalLifecycleService.NotificationKind));
            Assert.Equal(initial.Etag,(await service.ReadAsync(f.Underwriter,term.Id)).Etag);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_TestLapseAuditFailure;");
            var applied=await Lapse();Assert.False(applied.Replayed);
            var row=await db.Set<RenewalLapseEvent>().AsNoTracking().SingleAsync();
            var jobs=new SqlJobLeases(f.Factory,f.Clock);var lease=Assert.IsType<JobLease>(await jobs.ClaimWorkAsync(RenewalLifecycleService.NotificationKind,row.WorkId));
            var worker=new RenewalLapseNotificationWorker(f.Factory,f.Clock);var receipt=await worker.Deliver(lease);Assert.NotNull(receipt);
            Assert.True(await worker.Apply(lease,receipt.Value));
            var view=await service.ReadAsync(f.Underwriter,term.Id);Assert.Equal("lapsed",view.State);Assert.Equal("failed",view.NotificationState);
            var attempt=Assert.Single(view.NotificationAttempts);Assert.Equal("rejected",attempt.Outcome);Assert.Equal("demo-no-recipient",attempt.ErrorCode);
            Assert.Equal("demo-no-recipient",(await db.Set<JobException>().AsNoTracking().SingleAsync(x=>x.WorkId==row.WorkId)).Code);
            Assert.Equal(1,await db.Set<RenewalLapseNotificationReceipt>().CountAsync());Assert.Equal(1,await db.Set<PolicyTerm>().CountAsync());
        });
    }

    [Theory]
    [InlineData("motor-trade-road-risks",false)]
    [InlineData("motor-trade-combined",false)]
    [InlineData("motor-trade-road-risks",true)]
    [InlineData("motor-trade-combined",true)]
    public async Task RealSqlRenewalLifecycleLapseDeduplicatesWithoutInventingOrTruncatingCover(string product,bool automatic)
    {
        await WithDatabase(async(db,password)=>
        {
            var setup=await AcceptedIssue(db,password,product);var f=setup.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();
            var service=new RenewalLifecycleService(f.Factory,f.Clock);var before=await service.ReadAsync(f.Underwriter,term.Id);
            Assert.Equal("not-due",before.State);Assert.True(before.CanLapse);
            await using(var rejected=await db.Database.BeginTransactionAsync())
            {
                var invalidEvent=new RenewalLapseEvent{PolicyId=term.PolicyId,TermId=term.Id,RuleSettingVersionId=before.RuleSettingVersionId,
                    EffectiveAt=term.EndsAt,AutoLapseAt=before.Timeline.AutoLapseAt,Mode="manual",Reason="Fictional malformed notification test",RecipientSnapshotJson="[]",CreatedBy=f.Underwriter.UserId,CreatedAt=f.Clock.GetUtcNow()};
                var invalidWork=new OutboxWork{Kind=RenewalLifecycleService.NotificationKind,SubjectRecordId=invalidEvent.Id,ScenarioVersionId=before.RuleSettingVersionId,
                    OperationKey=$"renewal-lapse/{term.Id:N}",Payload="{}",NextAttemptAt=f.Clock.GetUtcNow(),CreatedAt=f.Clock.GetUtcNow()};
                db.Add(invalidWork);await db.SaveChangesAsync();invalidEvent.WorkId=invalidWork.Id;db.Add(invalidEvent);
                var malformed=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
                Assert.Equal(51762,Assert.IsType<SqlException>(malformed.InnerException).Number);
                await rejected.RollbackAsync();db.ChangeTracker.Clear();
            }
            await VerifyRenewalLapseHttp(db,f,password,term.Id,before.Etag);
            Assert.Null(await service.LapseDueAsync(term.Id));
            Guid eventId;
            if(automatic)
            {
                f.Clock.Current=before.Timeline.AutoLapseAt.AddTicks(-1);Assert.Null(await service.LapseDueAsync(term.Id));
                f.Clock.Current=before.Timeline.AutoLapseAt;
                var attempts=await Task.WhenAll(service.LapseDueAsync(term.Id),service.LapseDueAsync(term.Id));
                Assert.NotNull(attempts[0]);Assert.Equal(attempts[0],attempts[1]);eventId=attempts[0]!.Value;
            }
            else
            {
                var version=Convert.FromBase64String(before.Etag.Trim('"'));var key=Guid.NewGuid().ToString();
                Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.LapseAsync(f.Underwriter,term.Id,version,"short",key,Guid.NewGuid()))).Status);
                Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.LapseAsync(f.Underwriter,term.Id,new byte[8],"Fictional insured declined renewal",key,Guid.NewGuid()))).Status);
                var result=await service.LapseAsync(f.Underwriter,term.Id,version,"Fictional insured declined renewal",key,Guid.NewGuid());eventId=result.ResourceId;
                Assert.True((await service.LapseAsync(f.Underwriter,term.Id,version,"Fictional insured declined renewal",key,Guid.NewGuid())).Replayed);
                await VerifyRenewalLapseHttp(db,f,password,term.Id,before.Etag,key,result.Body);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={f.Underwriter.UserId}");
                try{Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.LapseAsync(f.Underwriter,term.Id,version,"Fictional insured declined renewal",key,Guid.NewGuid()))).Status);}
                finally{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'active' WHERE Id={f.Underwriter.UserId}");}
                Assert.Equal(eventId,(await service.LapseAsync(f.Underwriter,term.Id,version,"Fictional duplicate lapse attempt",Guid.NewGuid().ToString(),Guid.NewGuid())).ResourceId);
                Assert.Equal(basis.Id,(await new PolicyReadService(f.Factory,f.Clock).ReadAsync(f.Underwriter,term.PolicyId))["versionId"]);
            }
            var row=await db.Set<RenewalLapseEvent>().AsNoTracking().SingleAsync();Assert.Equal(eventId,row.Id);Assert.Equal(term.EndsAt,row.EffectiveAt);
            await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RenewalLapseEvent SET Reason='Mutated lapse reason' WHERE Id={row.Id}"));
            await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RenewalLapseEvent WHERE Id={row.Id}"));
            await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OutboxWork SET Payload=N'{{}}' WHERE Id={row.WorkId}"));
            Assert.Equal(automatic?"automatic":"manual",row.Mode);Assert.Equal(before.RuleSettingVersionId,row.RuleSettingVersionId);
            Assert.Equal(1,await db.Set<PolicyTerm>().CountAsync());Assert.Equal(term.EndsAt,(await db.Set<PolicyTerm>().AsNoTracking().SingleAsync()).EndsAt);
            Assert.Equal(basis.SnapshotJson,(await db.Set<PolicyVersion>().AsNoTracking().SingleAsync()).SnapshotJson);
            var after=await service.ReadAsync(f.Underwriter,term.Id);Assert.Equal("lapsed",after.State);Assert.False(after.CanLapse);
            Assert.Equal("renewal-already-lapsed",(await Assert.ThrowsAsync<QuoteOperationException>(()=>new ServicingDraftService(f.Factory,f.Clock).CreateAsync(f.Underwriter,term.Id,
                Convert.FromBase64String(after.Etag.Trim('"')),new("renewal",basis.Id,JsonSerializer.SerializeToElement(new{localDate="2027-09-16",localTime="00:00",timeZone="Europe/London",utcOffsetMinutes=60}),
                    "Fictional attempt to reopen a lapsed renewal"),Guid.NewGuid().ToString(),Guid.NewGuid()))).Code);
            Assert.Equal(eventId,await service.LapseDueAsync(term.Id));
            var expired=await new PolicyReadService(f.Factory,f.Clock).ReadAtAsync(f.Underwriter,term.PolicyId,term.EndsAt,f.Clock.GetUtcNow());
            Assert.Equal("expired",expired["coverageState"]);Assert.Equal(term.Id,expired["termId"]);Assert.Equal(basis.Id,expired["versionId"]);
            Assert.Equal(1,await db.Set<OutboxWork>().CountAsync(x=>x.Kind==RenewalLifecycleService.NotificationKind));
            var leases=new SqlJobLeases(f.Factory,f.Clock);var lease=await leases.ClaimWorkAsync(RenewalLifecycleService.NotificationKind,row.WorkId);Assert.NotNull(lease);
            var provider=new RenewalLapseNotificationWorker(f.Factory,f.Clock);var receipt=await provider.Deliver(lease);Assert.NotNull(receipt);
            // Lose the response after the provider commits, expire the process
            // lease, then restart with fresh service instances and a new attempt.
            f.Clock.Current=f.Clock.Current.AddMinutes(10);
            var restartedLease=await new SqlJobLeases(f.Factory,f.Clock).ClaimWorkAsync(RenewalLifecycleService.NotificationKind,row.WorkId);Assert.NotNull(restartedLease);
            var restarted=new RenewalLapseNotificationWorker(f.Factory,f.Clock);
            Assert.Equal(receipt,await restarted.Deliver(restartedLease));Assert.False(await restarted.Apply(lease,receipt.Value));
            Assert.True(await restarted.Apply(restartedLease,receipt.Value));Assert.False(await restarted.Apply(restartedLease,receipt.Value));
            Assert.Equal(1,await db.Set<RenewalLapseNotificationReceipt>().CountAsync());Assert.Equal(2,await db.Set<AdapterAttempt>().CountAsync(x=>x.WorkId==row.WorkId));
            Assert.Equal("succeeded",(await service.ReadAsync(f.Underwriter,term.Id)).NotificationState);
            Assert.Equal("renewal-already-lapsed",(await Assert.ThrowsAsync<QuoteOperationException>(()=>new RenewalPreparationService(f.Factory,f.Clock).PreviewAsync(f.Underwriter,term.Id))).Code);
        });
    }
}
