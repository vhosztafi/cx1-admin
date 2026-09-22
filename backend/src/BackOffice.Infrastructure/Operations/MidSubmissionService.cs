using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;
public sealed class MidSubmissionService(IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
{
    public async Task<CommunicationPage> List(ActorContext actor,Guid versionId,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await HoldVersion(db,actor,versionId,token);var query=db.Set<MidSubmission>().AsNoTracking().Where(x=>x.VersionId==versionId&&x.CreatedAt<=asOf);var total=await query.CountAsync(token);
        if(before is Guid cursor){var row=await query.SingleOrDefaultAsync(x=>x.Id==cursor,token)??throw CommunicationScope.BadCursor();query=query.Where(x=>x.CreatedAt<row.CreatedAt||x.CreatedAt==row.CreatedAt&&x.Id.CompareTo(cursor)<0);}
        var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(size+1).ToArrayAsync(token);var items=new List<object>();
        foreach(var row in rows.Take(size))items.Add(await View(db,row,token));await tx.CommitAsync(token);return new(items,total,rows.Length>size?rows[size-1].Id:null);
    }
    public async Task<(OutboxWork Work,bool RetryAllowed)> Job(ActorContext actor,Guid jobId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var row=await db.Set<MidSubmission>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==jobId,token)??throw MidSnapshots.Missing();await HoldVersion(db,actor,row.VersionId,token);
        var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==jobId,token);await tx.CommitAsync(token);return(work,Retryable(work));
    }
    public async Task<CommandOutcome> Retry(ActorContext actor,Guid id,string etag,string reason,string key,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000)throw new OperationalAccessException(422,"mid-retry-reason-required");MidSubmission? row=null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/mid-submissions/{id}/retry",key,Guid.NewGuid()),new{etag,reason},"mid.retry-requested",
            async(db,ct)=>{row=await db.Set<MidSubmission>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw MidSnapshots.Missing();await MidAuthority.Hold(db,actor,row,"mid-retry",ct);await MidAuthority.HoldSender(db,row,ct);},
            async(db,ct)=>
            {
                var work=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,ROWLOCK) WHERE Id={row!.WorkId}").SingleAsync(ct);
                if(TaskService.Etag(work.RowVersion)!=etag)throw new OperationalAccessException(412,"stale-mid-submission");
                var limit=JobRetryBudget.ExpandedLimit(work.State,work.ErrorCode,work.Attempts,work.AttemptLimit);if(limit is null)throw new OperationalAccessException(409,"mid-submission-not-retryable");
                work.AttemptLimit=limit.Value;work.State="pending";work.ErrorCode=null;work.CompletedAt=null;work.LeaseToken=null;work.LeaseExpiresAt=null;work.NextAttemptAt=time.GetUtcNow();
                db.Add(new AuditEvent{ActorId=actor.UserId,SubjectRecordId=row.PolicyId,EventType="mid.retry-reason",Reason=reason,OccurredAt=time.GetUtcNow(),After=MidSnapshots.Serialize(new{submissionId=id,versionId=row.VersionId})});
                await db.SaveChangesAsync(ct);return MessageDeliveryService.JobOutcome(work);
            },token);
    }
    private static bool Retryable(OutboxWork work)=>JobRetryBudget.ExpandedLimit(work.State,work.ErrorCode,work.Attempts,work.AttemptLimit)is not null;
    private static async Task HoldVersion(BackOfficeDbContext db,ActorContext actor,Guid id,CancellationToken token)
    {
        var row=await db.Set<PolicyVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw MidSnapshots.Missing();await OperationalScope.HoldParents(db,actor,[new("policy",row.PolicyId)],"mid-read",token);
        using var source=JsonDocument.Parse(row.SnapshotJson);if(source.RootElement.GetProperty("productCode").GetString() is not("motor-trade-road-risks" or "motor-trade-combined"))throw MidSnapshots.Missing();
    }
    private static async Task<object> View(BackOfficeDbContext db,MidSubmission row,CancellationToken token)
    {
        var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==row.WorkId,token);var snapshot=JsonSerializer.Deserialize<MidSnapshot>(row.RequestJson,MidSnapshots.Json)!;
        var saved=await db.Set<MidResult>().AsNoTracking().SingleOrDefaultAsync(x=>x.SubmissionId==row.Id,token);var outcome=saved is null?null:JsonSerializer.Deserialize<MidProviderOutcome>(saved.ResultJson,MidSnapshots.Json);
        var attempts=await db.Set<AdapterAttempt>().AsNoTracking().Where(x=>x.WorkId==work.Id).OrderBy(x=>x.AttemptNumber).Select(x=>new{x.Id,number=x.AttemptNumber,x.StartedAt,x.EndedAt,x.Outcome,x.ErrorCode}).ToArrayAsync(token);
        var task=await(from exception in db.Set<JobException>() join binding in db.Set<WorkflowTaskBinding>() on exception.Id equals binding.JobExceptionId where exception.WorkId==work.Id select (Guid?)binding.TaskId).FirstOrDefaultAsync(token);
        return new{row.Id,intentId=snapshot.IntentId,policyVersionId=row.VersionId,jobId=row.WorkId,state=outcome?.State??(work.State=="failed"?work.ErrorCode=="mid-context-unavailable"?"superseded":"failed":"pending"),snapshot.Items,
            reasonCodes=outcome?.ReasonCodes??(work.ErrorCode is{} code?new[]{code}:[]),providerReference=outcome?.ProviderReference,row.CreatedAt,work.CompletedAt,attempts,exceptionTaskId=task,retryAllowed=Retryable(work),etag=TaskService.Etag(work.RowVersion),
            purpose=snapshot.Purpose,baseVersionId=row.BaseVersionId,contentHash=snapshot.ContentHash,effectiveAt=snapshot.EffectiveAt,endsAt=snapshot.EndsAt};
    }
}
