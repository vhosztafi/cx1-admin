using System.Text.Json;
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
    public Task RealSqlOperationalDeliveryTimeoutAndExplicitRetryRetainOneEffectAndExceptionTask()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();var contact=await db.Set<Contact>().AsNoTracking().SingleAsync(x=>x.RelationshipId==policy.RelationshipId&&x.EndedAt==null);
        var commands=new SqlCommandBoundary(f.Factory,f.Clock);var tasks=new TaskService(f.Factory,commands,f.Clock);var threads=new ThreadService(f.Factory,commands,f.Clock);
        var subject=await tasks.Register(f.Underwriter,new("policy",policy.Id),"delivery-recovery-parent",default);
        var thread=await threads.Create(f.Underwriter,subject.ResourceId,new("agency","Fictional recovery",policy.RelationshipId),"delivery-recovery-thread",default);
        var service=new MessageDeliveryService(commands,f.Clock);var leases=new SqlJobLeases(f.Factory,f.Clock);
        var worker=new MessageDeliveryWorker(f.Factory,new FileService(f.Factory,commands,new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","delivery-recovery-files",db.Database.GetDbConnection().Database)),[]),f.Clock),f.Clock);
        var sequence=0;
        async Task<OperationalDelivery> Queue(string scenario)
        {
            sequence++;db.Add(new SettingVersion{Scope=MessageDeliveryService.WorkKind,Version=sequence,EffectiveFrom=f.Clock.Current.AddSeconds(-1),Values=JsonSerializer.Serialize(new{demo=true,kind=MessageDeliveryService.WorkKind,schemaVersion="1",scenario})});await db.SaveChangesAsync();
            var draft=await threads.CreateDraft(f.Underwriter,thread.ResourceId,new("Fictional recovery message",[contact.Id],[]),$"recovery-draft-{sequence}",default);
            var queued=await service.Send(f.Underwriter,draft.ResourceId,draft.Etag!,$"recovery-send-{sequence}",default);
            return await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync(x=>x.WorkId==queued.ResourceId);
        }
        var timeout=await Queue("timeout-after-success");var first=(await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,timeout.WorkId))!;
        Assert.Equal(JobFailure.ProviderTimeout,(await Assert.ThrowsAsync<MessageDeliveryException>(()=>worker.ExecuteProvider(first))).Failure);
        Assert.NotNull(await db.Set<DemoProviderOperation>().Where(x=>x.OperationKey==first.OperationKey).Select(x=>x.Result).SingleAsync());
        Assert.True(await leases.FailAsync(first,JobFailure.ProviderTimeout));f.Clock.Current=f.Clock.Current.AddHours(1);
        var recovered=(await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,timeout.WorkId))!;var outcome=await worker.ExecuteProvider(recovered);Assert.NotNull(outcome);
        Assert.Equal(InboxApplication.Applied,await worker.Apply(recovered,outcome));Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.OperationKey==first.OperationKey));
        var retry=await Queue("retry-required");
        for(var n=1;n<=6;n++)
        {
            var lease=(await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,retry.WorkId))!;Assert.Equal(n,lease.Attempt);
            Assert.Equal(JobFailure.ProviderUnavailable,(await Assert.ThrowsAsync<MessageDeliveryException>(()=>worker.ExecuteProvider(lease))).Failure);
            Assert.True(await leases.FailAsync(lease,JobFailure.ProviderUnavailable));f.Clock.Current=f.Clock.Current.AddHours(1);
        }
        var failure=await db.Set<JobException>().AsNoTracking().SingleAsync(x=>x.WorkId==retry.WorkId);
        var rule=await WorkflowRule(db,f.Clock.Current,"communication-failure","job-exception","data-exception");
        var workflows=new WorkflowTaskService(f.Factory,tasks,f.Clock);var taskId=await workflows.Reconcile(rule.Id,"job-exception",failure.Id,default);Assert.NotNull(taskId);
        Assert.Equal(taskId,await workflows.Reconcile(rule.Id,"job-exception",failure.Id,default));
        Assert.Equal(1,await db.Set<WorkflowTaskBinding>().CountAsync(x=>x.JobExceptionId==failure.Id));
        var current=await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync(x=>x.Id==retry.Id);Assert.Equal("failed",current.State);
        var retried=await service.Recover(f.Underwriter,retry.Id,TaskService.Etag(current.RowVersion),"Fictional manual recovery",false,true,"delivery-manual-retry",default);Assert.Equal(retry.WorkId,retried.ResourceId);
        var next=(await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,retry.WorkId))!;Assert.Equal(7,next.Attempt);
        var result=await worker.ExecuteProvider(next);Assert.NotNull(result);Assert.Equal(InboxApplication.Applied,await worker.Apply(next,result));
        Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.OperationKey==next.OperationKey));Assert.Equal(1,await db.Set<JobException>().CountAsync(x=>x.WorkId==retry.WorkId));
        Assert.Equal(taskId,await workflows.Reconcile(rule.Id,"job-exception",failure.Id,default));
        var rejected=await Queue("reject");var rejectLease=(await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,rejected.WorkId))!;
        var rejectedResult=await worker.ExecuteProvider(rejectLease);Assert.NotNull(rejectedResult);Assert.Equal("rejected",rejectedResult.State);Assert.Equal(InboxApplication.Applied,await worker.Apply(rejectLease,rejectedResult));
        var rejectedSaved=await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync(x=>x.Id==rejected.Id);
        Assert.Equal(409,(await Assert.ThrowsAsync<OperationalAccessException>(()=>service.Recover(f.Underwriter,rejected.Id,TaskService.Etag(rejectedSaved.RowVersion),"Cannot retry definite rejection",false,true,"rejected-retry",default))).Status);
    });
}
