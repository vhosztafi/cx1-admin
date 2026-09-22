using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalClaimsRejectionRequiresRevisionAndUncertaintyKeepsOperation()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();f.Clock.Current=term.EndsAt.AddDays(2);
        await using(var tx=await db.Database.BeginTransactionAsync()){await OperationalClaimsSeed.Seed(db);await tx.CommitAsync();}
        var scenarioNumber=1;
        async Task Scenario(string scenario){db.Add(new SettingVersion{Scope=ClaimsHandoffService.WorkKind,Version=++scenarioNumber,EffectiveFrom=f.Clock.GetUtcNow(),Values=JsonSerializer.Serialize(new{demo=true,kind=ClaimsHandoffService.WorkKind,schemaVersion="2",scenario})});await db.SaveChangesAsync();}
        var boundary=new SqlCommandBoundary(f.Factory,f.Clock);var resolver=new IncidentOccurrenceResolver(f.Factory,f.Clock);var incidents=new IncidentService(f.Factory,boundary,resolver,f.Clock);var handoffs=new ClaimsHandoffService(boundary,resolver,f.Clock);var summaries=new ClaimsSummaryService(f.Factory,boundary,handoffs,resolver,f.Clock);
        var files=new FileService(f.Factory,boundary,new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","claims-recovery-files",db.Database.GetDbConnection().Database)),[]),f.Clock);var worker=new ClaimsHandoffWorker(f.Factory,resolver,files,f.Clock);var leases=new SqlJobLeases(f.Factory,f.Clock);
        var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(version.EffectiveAt,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime).AddDays(1);
        var draft=JsonSerializer.SerializeToElement(new{policyId=version.PolicyId,productCode="motor-trade-road-risks",occurrence=new{occurredOn=day.ToString("yyyy-MM-dd"),timeZone="Europe/London",precision="date"},kind="other",thirdPartyInvolvement="unknown",reportedBy="Fictional reporter",reportingRoute="agency",bestContactDescription="01632 960001",description="A fictional third-party incident requiring administrator review.",motorSubject=new{kind="third-party-only",itemDescription="Fictional boundary wall"}});
        async Task<CommandOutcome> Ready(){var created=await incidents.Create(f.Underwriter,draft,Guid.NewGuid().ToString(),default);return await incidents.Resolve(f.Underwriter,created.ResourceId,created.Etag!,true,Guid.NewGuid().ToString(),default);}
        async Task<CommandOutcome> Send(Guid id,string etag,string key){var row=await db.Set<OperationalIncident>().AsNoTracking().SingleAsync(x=>x.Id==id);return await handoffs.Handoff(f.Underwriter,id,etag,row.CurrentRevisionId!.Value,row.CurrentResolutionId!.Value,OperationalClaimsSeed.AdministratorId,false,key,default);}
        async Task Finish(Guid workId){var lease=await leases.ClaimWorkAsync(ClaimsHandoffService.WorkKind,workId);Assert.NotNull(lease);var result=await worker.ExecuteProvider(lease);Assert.NotNull(result);Assert.Equal(InboxApplication.Applied,await worker.Apply(lease,result));}
        await Scenario("reject");var ready=await Ready();var firstKey=Guid.NewGuid().ToString();var queued=await Send(ready.ResourceId,ready.Etag!,firstKey);await Finish(queued.ResourceId);
        var rejected=await incidents.Read(f.Underwriter,ready.ResourceId,default);Assert.Equal("failed",JsonDocument.Parse(rejected.Body).RootElement.GetProperty("state").GetString());
        var oldRevision=await db.Set<OperationalIncident>().Where(x=>x.Id==ready.ResourceId).Select(x=>x.CurrentRevisionId).SingleAsync();
        var correction=await incidents.SaveDescription(f.Underwriter,ready.ResourceId,rejected.Etag!,"Corrected factual incident details supplied after the rejection.",Guid.NewGuid().ToString(),default);
        var corrected=await incidents.Resolve(f.Underwriter,ready.ResourceId,correction.Etag!,true,Guid.NewGuid().ToString(),default);
        await Assert.ThrowsAsync<CommandKeyConflictException>(()=>Send(ready.ResourceId,corrected.Etag!,firstKey));Assert.Equal(1,await db.Set<ClaimsHandoff>().CountAsync());
        await Scenario("success");var acceptedWork=await Send(ready.ResourceId,corrected.Etag!,Guid.NewGuid().ToString());await Finish(acceptedWork.ResourceId);
        var accepted=await db.Set<ClaimsHandoff>().AsNoTracking().SingleAsync(x=>x.State=="acknowledged");Assert.NotEqual(oldRevision,accepted.RevisionId);Assert.Equal(2,await db.Set<ClaimsHandoff>().CountAsync());
        // Two refreshes arrive in reverse order. ReceivedAt must not replace provider chronology.
        var current=await incidents.Read(f.Underwriter,ready.ResourceId,default);
        var refreshA=await summaries.FollowUp(f.Underwriter,ready.ResourceId,current.Etag!,"refresh",null,Guid.NewGuid().ToString(),default);
        var refreshB=await summaries.FollowUp(f.Underwriter,ready.ResourceId,current.Etag!,"refresh",null,Guid.NewGuid().ToString(),default);
        var leaseA=await leases.ClaimWorkAsync(ClaimsHandoffService.WorkKind,refreshA.ResourceId);Assert.NotNull(leaseA);f.Clock.Current=f.Clock.Current.AddSeconds(1);var older=await worker.ExecuteProvider(leaseA);Assert.NotNull(older);
        var leaseB=await leases.ClaimWorkAsync(ClaimsHandoffService.WorkKind,refreshB.ResourceId);Assert.NotNull(leaseB);f.Clock.Current=f.Clock.Current.AddSeconds(1);var newer=await worker.ExecuteProvider(leaseB);Assert.NotNull(newer);
        Assert.Equal(InboxApplication.Applied,await worker.Apply(leaseB,newer));f.Clock.Current=f.Clock.Current.AddSeconds(1);Assert.Equal(InboxApplication.Applied,await worker.Apply(leaseA,older));
        var page=await summaries.Summaries(f.Underwriter,ready.ResourceId,null,1,f.Clock.GetUtcNow(),default);Assert.NotNull(page.NextId);var latest=JsonSerializer.SerializeToElement(page.Items[0],new JsonSerializerOptions(JsonSerializerDefaults.Web));Assert.Equal(newer.Summary!.AsOf,latest.GetProperty("asOf").GetDateTimeOffset());Assert.Equal(JsonValueKind.Null,latest.GetProperty("paid").ValueKind);
        var secondPage=await summaries.Summaries(f.Underwriter,ready.ResourceId,page.NextId,1,f.Clock.GetUtcNow(),default);var next=JsonSerializer.SerializeToElement(secondPage.Items[0],new JsonSerializerOptions(JsonSerializerDefaults.Web));Assert.Equal(older.Summary!.AsOf,next.GetProperty("asOf").GetDateTimeOffset());Assert.True(next.GetProperty("receivedAt").GetDateTimeOffset()>latest.GetProperty("receivedAt").GetDateTimeOffset());
        await Assert.ThrowsAsync<OperationalAccessException>(()=>summaries.FollowUp(f.Underwriter,Guid.NewGuid(),current.Etag!,"contact","Fictional request",Guid.NewGuid().ToString(),default));
        var outsider=new ActorContext(f.Underwriter.UserId,f.Underwriter.TeamId,Guid.NewGuid(),f.Underwriter.Roles);
        await Assert.ThrowsAsync<OperationalAccessException>(()=>summaries.Summaries(outsider,ready.ResourceId,null,10,f.Clock.GetUtcNow(),default));
        // Exhaust transient work; a failed head cannot be edited into a duplicate submission.
        await Scenario("retry-required");var uncertain=await Ready();var uncertainJob=await Send(uncertain.ResourceId,uncertain.Etag!,Guid.NewGuid().ToString());
        for(var index=0;index<6;index++)
        {
            var lease=await leases.ClaimWorkAsync(ClaimsHandoffService.WorkKind,uncertainJob.ResourceId);Assert.NotNull(lease);
            var failure=await Assert.ThrowsAsync<ClaimsWorkerException>(()=>worker.ExecuteProvider(lease));Assert.Equal(JobFailure.ProviderUnavailable,failure.Failure);Assert.True(await leases.FailAsync(lease,failure.Failure));
            var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==uncertainJob.ResourceId);f.Clock.Current=work.NextAttemptAt>f.Clock.Current?work.NextAttemptAt.AddSeconds(1):f.Clock.Current.AddSeconds(1);
        }
        var exception=await db.Set<JobException>().AsNoTracking().SingleAsync(x=>x.WorkId==uncertainJob.ResourceId);
        var rule=await WorkflowRule(db,f.Clock.Current,"claims-failure","job-exception","data-exception");var tasks=new TaskService(f.Factory,boundary,f.Clock);var workflows=new WorkflowTaskService(f.Factory,tasks,f.Clock);
        var taskId=await workflows.Reconcile(rule.Id,"job-exception",exception.Id,default);Assert.NotNull(taskId);Assert.Equal(taskId,await workflows.Reconcile(rule.Id,"job-exception",exception.Id,default));Assert.Equal(1,await db.Set<WorkflowTaskBinding>().CountAsync(x=>x.JobExceptionId==exception.Id));
        var failed=await incidents.Read(f.Underwriter,uncertain.ResourceId,default);
        await Assert.ThrowsAsync<OperationalAccessException>(()=>incidents.SaveDescription(f.Underwriter,uncertain.ResourceId,failed.Etag!,"An edit must not bypass the unresolved provider operation.",Guid.NewGuid().ToString(),default));
        var retainedRequest=await db.Set<ClaimsRequest>().AsNoTracking().SingleAsync(x=>x.WorkId==uncertainJob.ResourceId);var retainedWork=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==uncertainJob.ResourceId);
        var retry=await summaries.Retry(f.Underwriter,uncertain.ResourceId,retainedRequest.Id,TaskService.Etag(retainedWork.RowVersion),"Retry the same fictional provider request",Guid.NewGuid().ToString(),default);Assert.Equal(uncertainJob.ResourceId,retry.ResourceId);await Finish(retry.ResourceId);
        Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==ClaimsHandoffService.WorkKind&&x.OperationKey==retainedWork.OperationKey));Assert.Equal(1,await db.Set<ClaimsHandoff>().CountAsync(x=>x.IncidentId==uncertain.ResourceId));
    });
}
