using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class MessageDeliveryService(SqlCommandBoundary commands,TimeProvider time)
{
    public const string WorkKind="operational-delivery";
    public async Task<CommandOutcome> Send(ActorContext actor,Guid messageId,string etag,string key,CancellationToken token)
    {
        OperationalMessageDraft? hint=null;DeliveryContent? content=null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/messages/{messageId}/send",key,Guid.NewGuid()),new{etag},"communication.send-queued",
            async(db,ct)=>
            {
                hint=await db.Set<OperationalMessageDraft>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==messageId,ct)??throw CommunicationScope.Missing();
                var held=await CommunicationScope.HoldThread(db,actor,hint.ThreadId,"message-send",ct);
                if(held.Thread.Visibility!="agency"||held.Thread.RelationshipId is not Guid relationship)throw new OperationalAccessException(422,"agency-thread-required");
                var recipients=await db.Set<MessageDraftRecipient>().Where(x=>x.MessageId==messageId).Select(x=>x.ContactId).ToArrayAsync(ct);
                var files=await db.Set<MessageDraftAttachment>().Where(x=>x.MessageId==messageId).Select(x=>x.DocumentVersionId).ToArrayAsync(ct);
                content=await DeliverySnapshots.Capture(db,actor,held.Held.Subjects.Single(),relationship,held.Thread.Subject,new(hint.Body,recipients,files),ct);
            },async(db,ct)=>
            {
                var draft=await db.Set<OperationalMessageDraft>().FromSqlInterpolated($"SELECT * FROM OperationalMessageDraft WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={messageId}").SingleAsync(ct);
                if(TaskService.Etag(draft.RowVersion)!=etag||!draft.RowVersion.SequenceEqual(hint!.RowVersion))throw new OperationalAccessException(412,"stale-message-draft");
                if(draft.State!="draft")throw new OperationalAccessException(409,"message-already-queued");
                var recipients=await db.Set<MessageDraftRecipient>().Where(x=>x.MessageId==messageId).Select(x=>x.ContactId).ToArrayAsync(ct);
                var files=await db.Set<MessageDraftAttachment>().Where(x=>x.MessageId==messageId).Select(x=>x.DocumentVersionId).ToArrayAsync(ct);
                if(!recipients.Order().SequenceEqual(content!.Recipients.Select(x=>x.ContactId))||!files.Order().SequenceEqual(content.Attachments.Select(x=>x.VersionId)))
                    throw new OperationalAccessException(412,"stale-message-draft");
                var json=DeliverySnapshots.Serialize(content);var version=new OperationalMessageVersion{MessageId=messageId,ContentJson=json,ContentHash=DeliverySnapshots.Hash(json),CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow()};
                db.Add(version);await db.SaveChangesAsync(ct);
                var result=await Queue(db,actor,content,version.Id,null,ct);
                draft.State="queued";await db.SaveChangesAsync(ct);return result;
            },token);
    }
    internal async Task<CommandOutcome> Queue(BackOfficeDbContext db,ActorContext actor,DeliveryContent content,Guid? versionId,Guid? resendOf,CancellationToken token)
    {
        var now=time.GetUtcNow();var setting=await db.Set<SettingVersion>().Where(x=>x.Scope==WorkKind&&x.EffectiveFrom<=now).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token);
        if(setting is null||OperationalDeliverySeed.Scenario(setting) is null)throw new OperationalAccessException(503,"delivery-scenario-unavailable");
        var json=DeliverySnapshots.Serialize(content);
        var delivery=new OperationalDelivery{SubjectId=content.SubjectId,RelationshipId=content.RelationshipId,MessageVersionId=versionId,ResendOfId=resendOf,
            ScenarioVersionId=setting.Id,ContentJson=json,ContentHash=DeliverySnapshots.Hash(json),CreatedBy=actor.UserId,CreatedAt=now,UpdatedAt=now};
        var work=new OutboxWork{Kind=WorkKind,SubjectRecordId=delivery.Id,ScenarioVersionId=setting.Id,Payload=json,CreatedBy=actor.UserId,CreatedAt=now,NextAttemptAt=now,OperationKey=$"operational-delivery/{delivery.Id:N}"};
        delivery.WorkId=work.Id;db.Add(work);db.Add(delivery);await db.SaveChangesAsync(token);
        db.AddRange(content.Recipients.Select(x=>new OperationalDeliveryRecipient{DeliveryId=delivery.Id,ContactId=x.ContactId,Name=x.Name,Email=x.Email,CreatedBy=actor.UserId,CreatedAt=now}));
        db.AddRange(content.Attachments.Select(x=>new OperationalDeliveryAttachment{DeliveryId=delivery.Id,DocumentVersionId=x.VersionId,FileObjectId=x.FileId,ContentHash=x.Hash,OriginalName=x.Name,MediaType=x.MediaType,Length=x.Length,CreatedBy=actor.UserId,CreatedAt=now}));
        await db.SaveChangesAsync(token);return JobOutcome(work);
    }
    internal static CommandOutcome JobOutcome(OutboxWork work)=>new(work.Id,202,JsonSerializer.Serialize(new{id=work.Id,work.Kind,work.State,work.Attempts,work.NextAttemptAt,work.CompletedAt,work.ErrorCode,work.AttemptLimit,retryAllowed=false},CommunicationScope.Json),Etag:TaskService.Etag(work.RowVersion));
}
