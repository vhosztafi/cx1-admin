using System.Data;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class ThreadService(IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
{
    public async Task<CommandOutcome> Create(ActorContext actor,Guid subjectId,ThreadWrite input,string key,CancellationToken token)
    {
        CommunicationRules.Thread(input);HeldOperationalScope? held=null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/records/{subjectId}/threads",key,Guid.NewGuid()),input,"communication.thread-created",
            async(db,ct)=>{held=await OperationalScope.HoldSubjects(db,actor,[subjectId],"message-write",ct);await CommunicationScope.Audience(db,held.Subjects.Single(),input.RelationshipId,ct);},
            async(db,ct)=>
            {
                var row=new OperationalThread{SubjectId=subjectId,Subject=input.Subject,Visibility=input.Visibility,RelationshipId=input.RelationshipId,
                    AuthorLabel=held!.ActorLabel,CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow()};db.Add(row);await db.SaveChangesAsync(ct);
                return new(row.Id,201,JsonSerializer.Serialize(ThreadView(row),CommunicationScope.Json));
            },token);
    }

    public async Task<CommandOutcome> CreateDraft(ActorContext actor,Guid threadId,MessageDraftWrite input,string key,CancellationToken token)
    {
        OperationalThread? thread=null;HeldOperationalScope? held=null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/threads/{threadId}/messages",key,Guid.NewGuid()),input,"communication.draft-created",
            async(db,ct)=>{(thread,held)=await CommunicationScope.HoldThread(db,actor,threadId,"message-write",ct);await CommunicationScope.ValidateSelection(db,actor,thread,input,ct);},
            async(db,ct)=>
            {
                var row=new OperationalMessageDraft{ThreadId=threadId,Body=input.Body,AuthorLabel=held!.ActorLabel,CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow(),UpdatedAt=time.GetUtcNow()};
                db.Add(row);await db.SaveChangesAsync(ct);await ReplaceSelections(db,row,input,actor.UserId,ct);return await Outcome(db,row,201,ct);
            },token);
    }

    public async Task<CommandOutcome> UpdateDraft(ActorContext actor,Guid messageId,string etag,MessageDraftWrite input,string key,CancellationToken token)
    {
        Guid threadId=default;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/messages/{messageId}",key,Guid.NewGuid()),new{etag,input},"communication.draft-updated",
            async(db,ct)=>
            {
                threadId=await db.Set<OperationalMessageDraft>().Where(x=>x.Id==messageId).Select(x=>(Guid?)x.ThreadId).SingleOrDefaultAsync(ct)??throw CommunicationScope.Missing();
                var held=await CommunicationScope.HoldThread(db,actor,threadId,"message-write",ct);
                await CommunicationScope.ValidateSelection(db,actor,held.Thread,input,ct);
            },async(db,ct)=>
            {
                var row=await db.Set<OperationalMessageDraft>().FromSqlInterpolated($"SELECT * FROM OperationalMessageDraft WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={messageId}").SingleOrDefaultAsync(ct)??throw CommunicationScope.Missing();
                if(row.ThreadId!=threadId)throw CommunicationScope.Missing();
                if(TaskService.Etag(row.RowVersion)!=etag)throw new OperationalAccessException(412,"stale-message-draft");
                if(row.State!="draft")throw new OperationalAccessException(409,"message-not-editable");
                row.Body=input.Body;row.UpdatedAt=time.GetUtcNow();
                // Force a new concurrency token even when only selections changed.
                db.Entry(row).Property(x=>x.UpdatedAt).IsModified=true;
                await ReplaceSelections(db,row,input,actor.UserId,ct);return await Outcome(db,row,200,ct);
            },token);
    }

    private async Task ReplaceSelections(BackOfficeDbContext db,OperationalMessageDraft row,MessageDraftWrite input,Guid actor,CancellationToken token)
    {
        await db.Set<MessageDraftRecipient>().Where(x=>x.MessageId==row.Id).ExecuteDeleteAsync(token);
        await db.Set<MessageDraftAttachment>().Where(x=>x.MessageId==row.Id).ExecuteDeleteAsync(token);
        var now=time.GetUtcNow();
        db.AddRange(input.RecipientContactIds.Select(id=>new MessageDraftRecipient{MessageId=row.Id,ContactId=id,CreatedBy=actor,CreatedAt=now}));
        db.AddRange(input.AttachmentVersionIds.Select(id=>new MessageDraftAttachment{MessageId=row.Id,DocumentVersionId=id,CreatedBy=actor,CreatedAt=now}));
        await db.SaveChangesAsync(token);
    }
    private static async Task<CommandOutcome> Outcome(BackOfficeDbContext db,OperationalMessageDraft row,int status,CancellationToken token)
        =>new(row.Id,status,JsonSerializer.Serialize(await MessageView(db,row,token),CommunicationScope.Json),Etag:TaskService.Etag(row.RowVersion));
    public async Task<CommandOutcome> ReadDraft(ActorContext actor,Guid messageId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var threadId=await db.Set<OperationalMessageDraft>().Where(x=>x.Id==messageId).Select(x=>(Guid?)x.ThreadId).SingleOrDefaultAsync(token)??throw CommunicationScope.Missing();
        var held=await CommunicationScope.HoldThread(db,actor,threadId,"message-read",token);
        var row=await db.Set<OperationalMessageDraft>().AsNoTracking().SingleAsync(x=>x.Id==messageId&&x.ThreadId==threadId,token);
        foreach(var versionId in await db.Set<MessageDraftAttachment>().Where(x=>x.MessageId==messageId).Select(x=>x.DocumentVersionId).ToArrayAsync(token))
            await DocumentService.AttachmentVersion(db,actor,held.Thread.SubjectId,versionId,false,token);
        var result=await Outcome(db,row,200,token);await transaction.CommitAsync(token);return result;
    }
    private static object ThreadView(OperationalThread row)=>new{id=row.Id,subjectRecordId=row.SubjectId,row.Visibility,row.RelationshipId,row.Subject,row.AuthorLabel,row.CreatedAt};
    private static async Task<object> MessageView(BackOfficeDbContext db,OperationalMessageDraft row,CancellationToken token)
        =>new{id=row.Id,row.ThreadId,row.Body,row.State,row.AuthorLabel,row.CreatedAt,row.UpdatedAt,etag=TaskService.Etag(row.RowVersion),
            recipientContactIds=await db.Set<MessageDraftRecipient>().Where(x=>x.MessageId==row.Id).OrderBy(x=>x.ContactId).Select(x=>x.ContactId).ToArrayAsync(token),
            attachmentVersionIds=await db.Set<MessageDraftAttachment>().Where(x=>x.MessageId==row.Id).OrderBy(x=>x.DocumentVersionId).Select(x=>x.DocumentVersionId).ToArrayAsync(token)};

    public async Task<CommunicationPage> List(ActorContext actor,Guid subjectId,string? visibility,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);if(visibility is not(null or "internal" or "agency"))throw new OperationalAccessException(400,"invalid-thread-visibility");
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var held=await OperationalScope.HoldSubjects(db,actor,[subjectId],"message-read",token);var audience=DocumentService.AllowedAudience(db,held.Subjects.Single());
        var query=db.Set<OperationalThread>().AsNoTracking().Where(x=>x.SubjectId==subjectId&&x.CreatedAt<=asOf&&(visibility==null||x.Visibility==visibility)&&
            (x.RelationshipId==null||audience.Any(r=>r.Id==x.RelationshipId)));
        var total=await query.CountAsync(token);
        if(before is Guid id){var cursor=await query.SingleOrDefaultAsync(x=>x.Id==id,token)??throw CommunicationScope.BadCursor();query=query.Where(x=>x.CreatedAt<cursor.CreatedAt||x.CreatedAt==cursor.CreatedAt&&x.Id.CompareTo(id)<0);}
        var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(size+1).ToArrayAsync(token);
        await transaction.CommitAsync(token);return new(rows.Take(size).Select(ThreadView).ToArray(),total,rows.Length>size?rows[size-1].Id:null);
    }

    public async Task<CommunicationPage> Messages(ActorContext actor,Guid threadId,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var held=await CommunicationScope.HoldThread(db,actor,threadId,"message-read",token);
        var query=db.Set<OperationalMessageDraft>().AsNoTracking().Where(x=>x.ThreadId==threadId&&x.CreatedAt<=asOf);var total=await query.CountAsync(token);
        if(before is Guid id){var cursor=await query.SingleOrDefaultAsync(x=>x.Id==id,token)??throw CommunicationScope.BadCursor();query=query.Where(x=>x.CreatedAt<cursor.CreatedAt||x.CreatedAt==cursor.CreatedAt&&x.Id.CompareTo(id)<0);}
        var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(size+1).ToArrayAsync(token);var items=new List<object>();
        foreach(var row in rows.Take(size))
        {
            // Ended recipients remain visible in an owned draft so they can be
            // removed; saving/queuing them is rejected by current validation.
            foreach(var versionId in await db.Set<MessageDraftAttachment>().Where(x=>x.MessageId==row.Id).Select(x=>x.DocumentVersionId).ToArrayAsync(token))
                await DocumentService.AttachmentVersion(db,actor,held.Thread.SubjectId,versionId,false,token);
            items.Add(await MessageView(db,row,token));
        }
        await transaction.CommitAsync(token);return new(items,total,rows.Length>size?rows[size-1].Id:null);
    }
}
