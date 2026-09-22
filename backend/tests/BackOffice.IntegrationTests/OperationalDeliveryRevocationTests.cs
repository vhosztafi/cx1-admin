using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task RealSqlOperationalDeliveryRevocationPreventsEffectAndPreservesHonestOutcome(bool recipientRevoked)=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await using(var transaction=await db.Database.BeginTransactionAsync()){await OperationalDeliverySeed.Seed(db);await transaction.CommitAsync();}
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();var contact=await db.Set<Contact>().AsNoTracking().SingleAsync(x=>x.RelationshipId==policy.RelationshipId&&x.EndedAt==null);
        var commands=new SqlCommandBoundary(f.Factory,f.Clock);var tasks=new TaskService(f.Factory,commands,f.Clock);var threads=new ThreadService(f.Factory,commands,f.Clock);
        var subject=await tasks.Register(f.Underwriter,new("policy",policy.Id),"delivery-revocation-parent",default);
        var thread=await threads.Create(f.Underwriter,subject.ResourceId,new("agency","Fictional revocation",policy.RelationshipId),"delivery-revocation-thread",default);
        var service=new MessageDeliveryService(commands,f.Clock);var leases=new SqlJobLeases(f.Factory,f.Clock);
        var worker=new MessageDeliveryWorker(f.Factory,new FileService(f.Factory,commands,new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","delivery-revocation-files",db.Database.GetDbConnection().Database)),[]),f.Clock),f.Clock);
        async Task<(OperationalDelivery Delivery,CommandOutcome Draft)> Queue(string suffix)
        {
            var draft=await threads.CreateDraft(f.Underwriter,thread.ResourceId,new("Fictional revocation message",[contact.Id],[]),"revocation-draft-"+suffix,default);
            var queued=await service.Send(f.Underwriter,draft.ResourceId,draft.Etag!,"revocation-send-"+suffix,default);
            return(await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync(x=>x.WorkId==queued.ResourceId),draft);
        }
        var before=await Queue("before");var after=await Queue("after");
        var first=(await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,before.Delivery.WorkId))!;
        var second=(await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,after.Delivery.WorkId))!;
        var alreadyPerformed=await worker.ExecuteProvider(second);Assert.NotNull(alreadyPerformed);
        if(recipientRevoked)
        {
            var ended=await db.Set<Contact>().SingleAsync(x=>x.Id==contact.Id);
            ended.IsPrimary=false;
            ended.EndedAt=f.Clock.Current<ended.CreatedAt?ended.CreatedAt:f.Clock.Current;
            ended.EndedBy=f.Underwriter.UserId;ended.EndReason="Fictional recipient authority ended";await db.SaveChangesAsync();
        }
        else await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={f.Underwriter.UserId}");
        Assert.Equal(JobFailure.Superseded,(await Assert.ThrowsAsync<MessageDeliveryException>(()=>worker.ExecuteProvider(first))).Failure);
        Assert.True(await leases.FailAsync(first,JobFailure.Superseded));
        Assert.Equal(InboxApplication.Applied,await worker.Apply(second,alreadyPerformed));
        Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==MessageDeliveryService.WorkKind));
        Assert.Equal(0,await db.Set<DemoProviderOperation>().CountAsync(x=>x.OperationKey==first.OperationKey));
        Assert.Equal(2,await db.Set<OperationalDelivery>().CountAsync(x=>x.State=="superseded"));
        var persisted=await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync(x=>x.Id==after.Delivery.Id);Assert.Equal(alreadyPerformed.OperationId,persisted.ProviderOperationId);
        var attempt=await db.Set<AdapterAttempt>().AsNoTracking().SingleAsync(x=>x.Id==persisted.AttemptId);Assert.Contains("\"providerOutcome\":\"delivered\"",attempt.Response);Assert.Contains("\"applied\":false",attempt.Response);
        var replay=await Record.ExceptionAsync(()=>service.Send(f.Underwriter,before.Draft.ResourceId,before.Draft.Etag!,"revocation-send-before",default));
        Assert.True(replay is OperationalAccessException{Status:403 or 404} or QuoteOperationException{Status:403 or 404});
        var exception=await db.Set<JobException>().AsNoTracking().SingleAsync(x=>x.WorkId==first.WorkId);
        var rule=await WorkflowRule(db,f.Clock.Current,"revoked-delivery","job-exception","data-exception");
        var workflows=new WorkflowTaskService(f.Factory,tasks,f.Clock);var taskId=await workflows.Reconcile(rule.Id,"job-exception",exception.Id,default);Assert.NotNull(taskId);
        Assert.Equal(taskId,await workflows.Reconcile(rule.Id,"job-exception",exception.Id,default));Assert.Equal(1,await db.Set<WorkflowTaskBinding>().CountAsync(x=>x.JobExceptionId==exception.Id));
        var binding=await db.Set<WorkflowTaskBinding>().AsNoTracking().SingleAsync(x=>x.JobExceptionId==exception.Id);Assert.Contains(f.Underwriter.UserId.ToString(),binding.SourceSnapshotJson);
        if(!recipientRevoked)Assert.NotEqual(f.Underwriter.UserId,binding.CreatedBy);
    });
}
