using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class MessageDeliveryService
{
    public async Task<CommandOutcome> Recover(ActorContext actor,Guid deliveryId,string etag,string reason,bool resend,bool message,string key,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000)throw new CommunicationRuleException("delivery-reason-required");
        OperationalDelivery? hint=null;DeliveryContent? snapshot=null;var correlation=Guid.NewGuid();
        var route=$"/api/v1/{(message?"message":"document")}-deliveries/{deliveryId}/{(resend?"resend":"retry")}";
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,route,key,correlation),new{etag,reason},resend?"communication.delivery-resent":"communication.delivery-retry",
            async(db,ct)=>
            {
                hint=await db.Set<OperationalDelivery>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==deliveryId,ct)??throw CommunicationScope.Missing();
                if(message!=(hint.MessageVersionId is not null))throw CommunicationScope.Missing();
                var held=await OperationalScope.HoldSubjects(db,actor,[hint.SubjectId],message?"message-send":"document-send",ct);
                var original=JsonSerializer.Deserialize<DeliveryContent>(hint.ContentJson,CommunicationScope.Json)!;
                snapshot=await DeliverySnapshots.Capture(db,actor,held.Subjects.Single(),hint.RelationshipId,original.Subject,new(original.Body,original.Recipients.Select(x=>x.ContactId).ToArray(),original.Attachments.Select(x=>x.VersionId).ToArray()),ct);
                if(DeliverySnapshots.Serialize(snapshot)!=hint.ContentJson)throw new OperationalAccessException(409,"delivery-context-changed");
            },async(db,ct)=>
            {
                // Lease/failure/application paths own the work head before the
                // mutable delivery head. Retry must use that same order.
                OutboxWork? retryWork=null;
                if(!resend)retryWork=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,ROWLOCK) WHERE Id={hint!.WorkId}").SingleAsync(ct);
                var row=await db.Set<OperationalDelivery>().FromSqlInterpolated($"SELECT * FROM OperationalDelivery WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={deliveryId}").SingleAsync(ct);
                if(TaskService.Etag(row.RowVersion)!=etag)throw new OperationalAccessException(412,"stale-delivery");
                db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,SubjectRecordId=row.Id,EventType="communication.delivery-recovery-reason",Reason=reason,CorrelationId=correlation,OccurredAt=time.GetUtcNow(),After=JsonSerializer.Serialize(new{action=resend?"resend":"retry"})});
                if(resend)
                {
                    if(row.State=="queued")throw new OperationalAccessException(409,"delivery-still-queued");
                    var result=await Queue(db,actor,snapshot!,row.MessageVersionId,row.Id,ct);
                    // New operation, frozen original content; never alter the previous receipt.
                    if(row.MessageVersionId is not null)
                    {
                        var newlyQueued=await db.Set<OperationalDelivery>().SingleAsync(x=>x.WorkId==result.ResourceId,ct);
                        await DeliverySnapshots.UpdateMessageState(db,newlyQueued,ct);await db.SaveChangesAsync(ct);
                    }
                    return result;
                }
                var work=retryWork!;
                var expanded=JobRetryBudget.ExpandedLimit(work.State,work.ErrorCode,work.Attempts,work.AttemptLimit);
                if(expanded is null||row.State!="failed")throw new OperationalAccessException(409,"delivery-not-retryable");
                work.AttemptLimit=expanded.Value;work.State="pending";work.NextAttemptAt=time.GetUtcNow();work.CompletedAt=null;work.ErrorCode=null;work.LeaseToken=null;work.LeaseExpiresAt=null;
                row.State="queued";row.CompletedAt=null;row.OutcomeCode=null;
                await DeliverySnapshots.UpdateMessageState(db,row,ct);
                await db.SaveChangesAsync(ct);return JobOutcome(work);
            },token);
    }
}
