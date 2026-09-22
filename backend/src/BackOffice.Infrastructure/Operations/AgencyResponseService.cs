using System.Data;
using System.Globalization;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Operations;

public sealed class AgencyResponseService(IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
{
    private static async Task<OperationalThread> Scope(BackOfficeDbContext db,ActorContext actor,Guid messageId,string capability,CancellationToken token)
    {
        var draft=await db.Set<OperationalMessageDraft>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==messageId,token)??throw CommunicationScope.Missing();
        var (thread,_)=await CommunicationScope.HoldThread(db,actor,draft.ThreadId,capability,token);
        if(thread.Visibility!="agency"||thread.RelationshipId is null)throw CommunicationScope.Missing();
        return thread;
    }

    public async Task<CommandOutcome> Track(ActorContext actor,Guid messageId,string reason,string key,CancellationToken token)
    {
        OperationalThread? thread=null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/messages/{messageId}/agency-response",key,Guid.NewGuid()),new{reason},"communication.response-tracked",
            async(db,ct)=>{thread=await Scope(db,actor,messageId,"message-write",ct);},async(db,ct)=>
            {
                // Lock the message to serialize separate keys trying to track the same immutable send.
                await db.Set<OperationalMessageDraft>().FromSqlInterpolated($"SELECT * FROM OperationalMessageDraft WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={messageId}").SingleAsync(ct);
                var version=await db.Set<OperationalMessageVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.MessageId==messageId,ct);
                var delivered=version is not null&&await db.Set<OperationalDelivery>().AnyAsync(x=>x.MessageVersionId==version.Id&&x.State=="delivered"&&x.SubjectId==thread!.SubjectId&&x.RelationshipId==thread.RelationshipId,ct);
                var content=version is null?null:JsonSerializer.Deserialize<DeliveryContent>(version.ContentJson,CommunicationScope.Json);
                AgencyResponseRules.Track(delivered?"delivered":"undelivered",content?.Body??"",reason);
                if(content is null||content.SubjectId!=thread!.SubjectId||content.RelationshipId!=thread.RelationshipId)throw CommunicationScope.Missing();
                if(await db.Set<AgencyResponseRequest>().AnyAsync(x=>x.MessageVersionId==version!.Id,ct))throw new OperationalAccessException(409,"agency-response-already-tracked");
                await using var number=db.Database.GetDbConnection().CreateCommand();number.Transaction=db.Database.CurrentTransaction!.GetDbTransaction();
                number.CommandText="SELECT NEXT VALUE FOR AgencyResponseReferenceSequence";
                var reference="ARQ-"+Convert.ToInt64(await number.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture).ToString("D7",CultureInfo.InvariantCulture);
                var row=new AgencyResponseRequest{MessageVersionId=version!.Id,SubjectId=thread.SubjectId,RelationshipId=content.RelationshipId,
                    Reference=reference,Subject=content.Subject,Instruction=content.Body,Reason=reason.Trim(),CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow(),UpdatedAt=time.GetUtcNow()};
                db.Add(row);await db.SaveChangesAsync(ct);return await Outcome(db,row,201,ct);
            },token);
    }

    public async Task<CommandOutcome> Read(ActorContext actor,Guid messageId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        await Scope(db,actor,messageId,"message-read",token);
        var row=await(from request in db.Set<AgencyResponseRequest>() join version in db.Set<OperationalMessageVersion>() on request.MessageVersionId equals version.Id where version.MessageId==messageId select request).AsNoTracking().SingleOrDefaultAsync(token);
        var result=row is null?new CommandOutcome(messageId,200,"null"):await Outcome(db,row,200,token);await transaction.CommitAsync(token);return result;
    }

    public async Task<CommandOutcome> Resolve(ActorContext actor,Guid id,string outcome,string reason,string etag,string key,CancellationToken token)
        =>await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/agency-responses/{id}/resolve",key,Guid.NewGuid()),new{outcome,reason,etag},"communication.response-resolved",
            async(db,ct)=>
            {
                var messageId=await(from request in db.Set<AgencyResponseRequest>() join version in db.Set<OperationalMessageVersion>() on request.MessageVersionId equals version.Id where request.Id==id select (Guid?)version.MessageId).SingleOrDefaultAsync(ct)??throw CommunicationScope.Missing();
                await Scope(db,actor,messageId,"message-write",ct);
            },async(db,ct)=>
            {
                var row=await db.Set<AgencyResponseRequest>().FromSqlInterpolated($"SELECT * FROM AgencyResponseRequest WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={id}").SingleAsync(ct);
                if(TaskService.Etag(row.RowVersion)!=etag)throw new OperationalAccessException(412,"stale-agency-response");
                AgencyResponseRules.Resolve(row.State,outcome,reason);
                row.State=outcome;row.ResolutionReason=reason.Trim();row.ResolvedBy=actor.UserId;row.ResolvedAt=time.GetUtcNow();
                await db.SaveChangesAsync(ct);return await Outcome(db,row,200,ct);
            },token);

    private static async Task<CommandOutcome> Outcome(BackOfficeDbContext db,AgencyResponseRequest row,int status,CancellationToken token)
    {
        var messageId=await db.Set<OperationalMessageVersion>().Where(x=>x.Id==row.MessageVersionId).Select(x=>x.MessageId).SingleAsync(token);
        return new(row.Id,status,JsonSerializer.Serialize(new{row.Id,messageId,row.Reference,row.Subject,row.Instruction,row.State,row.CreatedAt,row.ResolvedAt,row.Reason,row.ResolutionReason,etag=TaskService.Etag(row.RowVersion)},CommunicationScope.Json),Etag:TaskService.Etag(row.RowVersion));
    }
}
