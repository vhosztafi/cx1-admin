using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

// Parent, identity and audience authorization use explicit held locks. Delivery
// and work status are polling snapshots: do not retain shared delivery locks
// while waiting for the work head, which workers acquire before delivery.
public sealed class DeliveryReadService(IDbContextFactory<BackOfficeDbContext> factory)
{
    internal static async Task<OperationalDelivery> Hold(BackOfficeDbContext db,ActorContext actor,Guid deliveryId,CancellationToken token)
    {
        var row=await db.Set<OperationalDelivery>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==deliveryId,token)??throw CommunicationScope.Missing();
        var held=await OperationalScope.HoldSubjects(db,actor,[row.SubjectId],row.MessageVersionId is null?"document-read":"message-read",token);
        await CommunicationScope.Audience(db,held.Subjects.Single(),row.RelationshipId,token);
        foreach(var id in await db.Set<OperationalDeliveryAttachment>().Where(x=>x.DeliveryId==row.Id).OrderBy(x=>x.DocumentVersionId).Select(x=>x.DocumentVersionId).ToArrayAsync(token))
            await DocumentService.AttachmentVersion(db,actor,row.SubjectId,id,false,token);
        return row;
    }
    public async Task<CommandOutcome> Read(ActorContext actor,Guid deliveryId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        var row=await Hold(db,actor,deliveryId,token);var view=await View(db,row,token);await transaction.CommitAsync(token);return new(row.Id,200,JsonSerializer.Serialize(view,CommunicationScope.Json),Etag:TaskService.Etag(row.RowVersion));
    }
    public async Task<(OutboxWork Work,bool RetryAllowed)> Job(ActorContext actor,Guid workId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        var id=await db.Set<OperationalDelivery>().Where(x=>x.WorkId==workId).Select(x=>(Guid?)x.Id).SingleOrDefaultAsync(token)??throw CommunicationScope.Missing();
        var row=await Hold(db,actor,id,token);var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==workId,token);
        var retry=actor.HasCapability(row.MessageVersionId is null?"document-send":"message-send")&&JobRetryBudget.ExpandedLimit(work.State,work.ErrorCode,work.Attempts,work.AttemptLimit) is not null;
        await transaction.CommitAsync(token);return(work,retry);
    }
    public async Task<CommunicationPage> List(ActorContext actor,Guid id,bool message,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        IQueryable<OperationalDelivery> query;
        if(message)
        {
            var thread=await db.Set<OperationalMessageDraft>().Where(x=>x.Id==id).Select(x=>(Guid?)x.ThreadId).SingleOrDefaultAsync(token)??throw CommunicationScope.Missing();
            await CommunicationScope.HoldThread(db,actor,thread,"message-read",token);
            query=from delivery in db.Set<OperationalDelivery>() join version in db.Set<OperationalMessageVersion>() on delivery.MessageVersionId equals version.Id where version.MessageId==id select delivery;
        }
        else
        {
            var held=await OperationalScope.HoldSubjects(db,actor,[id],"document-read",token);var audience=DocumentService.AllowedAudience(db,held.Subjects.Single());
            query=db.Set<OperationalDelivery>().Where(x=>x.SubjectId==id&&x.MessageVersionId==null&&audience.Any(r=>r.Id==x.RelationshipId));
        }
        query=query.AsNoTracking().Where(x=>x.CreatedAt<=asOf);var total=await query.CountAsync(token);
        if(before is Guid cursorId){var cursor=await query.SingleOrDefaultAsync(x=>x.Id==cursorId,token)??throw CommunicationScope.BadCursor();query=query.Where(x=>x.CreatedAt<cursor.CreatedAt||x.CreatedAt==cursor.CreatedAt&&x.Id.CompareTo(cursorId)<0);}
        var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(size+1).ToArrayAsync(token);var items=new List<object>();
        foreach(var row in rows.Take(size)){await Hold(db,actor,row.Id,token);items.Add(await View(db,row,token));}
        await transaction.CommitAsync(token);return new(items,total,rows.Length>size?rows[size-1].Id:null);
    }
    public async Task<object> Attempts(ActorContext actor,Guid id,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        var row=await Hold(db,actor,id,token);var rows=await db.Set<AdapterAttempt>().AsNoTracking().Where(x=>x.WorkId==row.WorkId).OrderBy(x=>x.AttemptNumber).ToArrayAsync(token);
        var items=rows.Select(x=>new{id=x.Id,deliveryId=id,number=x.AttemptNumber,x.StartedAt,x.EndedAt,outcome=x.Outcome is "transient-failure" or "lease-expired"?"retryable":x.Outcome,x.ErrorCode}).ToArray();
        await transaction.CommitAsync(token);return new{items,totalCount=items.Length};
    }
    private static async Task<object> View(BackOfficeDbContext db,OperationalDelivery row,CancellationToken token)
    {
        var content=JsonSerializer.Deserialize<DeliveryContent>(row.ContentJson,CommunicationScope.Json)!;
        var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==row.WorkId,token);
        var providerState=row.ProviderOperationId is Guid operationId?await db.Set<DemoProviderOperation>().Where(x=>x.Id==operationId).Select(x=>x.State).SingleAsync(token):null;
        return new{id=row.Id,jobId=row.WorkId,subjectRecordId=row.SubjectId,row.State,row.CreatedAt,row.CompletedAt,documentVersionIds=content.Attachments.Select(x=>x.VersionId).ToArray(),
            recipientLabels=content.Recipients.Select(x=>x.Name+" · "+x.Email).ToArray(),content.Subject,content.Body,errorCode=row.OutcomeCode??work.ErrorCode,
            etag=TaskService.Etag(row.RowVersion),row.ResendOfId,providerOutcome=providerState=="succeeded"?"delivered":providerState=="rejected"?"rejected":null,
            retryAllowed=JobRetryBudget.ExpandedLimit(work.State,work.ErrorCode,work.Attempts,work.AttemptLimit) is not null};
    }
}
