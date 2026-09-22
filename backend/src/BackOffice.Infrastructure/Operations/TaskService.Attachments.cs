using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class TaskService
{
    public Task<CommandOutcome> AttachDocument(ActorContext actor,Guid taskId,string etag,string key,Guid versionId,string reason,CancellationToken token)
    {
        if(versionId==Guid.Empty)throw new OperationalAccessException(422,"invalid-document-version");
        return Mutate(actor,[new(taskId,etag)],key,$"/api/v1/tasks/{taskId}/attachments","task.document-attached",new{versionId,reason},"task-write",null,
            async(db,row,ct)=>
            {
                Editable(row);
                await DocumentService.AttachmentVersion(db,actor,row.SubjectId,versionId,true,ct);
                if(await db.Set<TaskDocumentAttachment>().AnyAsync(x=>x.TaskId==taskId&&x.DocumentVersionId==versionId&&x.RemovedAt==null,ct))throw new OperationalAccessException(409,"document-already-attached");
                if(await db.Set<TaskDocumentAttachment>().CountAsync(x=>x.TaskId==taskId&&x.RemovedAt==null,ct)>=20)throw new OperationalAccessException(409,"task-attachment-limit");
                var label=await db.Set<StaffUser>().Where(x=>x.Id==actor.UserId).Select(x=>x.DisplayName).SingleAsync(ct);
                db.Add(new TaskDocumentAttachment{TaskId=taskId,DocumentVersionId=versionId,AuthorLabel=label,Reason=reason,CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow()});
            },reason,false,token,authorize:async(db,subjects,ct)=>{await DocumentService.AttachmentVersion(db,actor,subjects.Single().Id,versionId,false,ct);});
    }

    public Task<CommandOutcome> RemoveDocumentAttachment(ActorContext actor,Guid taskId,string etag,string key,Guid attachmentId,string reason,CancellationToken token)
    {
        if(attachmentId==Guid.Empty)throw new OperationalAccessException(422,"invalid-task-attachment");
        return Mutate(actor,[new(taskId,etag)],key,$"/api/v1/tasks/{taskId}/attachments/{attachmentId}/remove","task.document-removed",new{attachmentId,reason},"task-write",null,
            async(db,row,ct)=>
            {
                Editable(row);
                var link=await db.Set<TaskDocumentAttachment>().SingleAsync(x=>x.Id==attachmentId&&x.TaskId==taskId,ct);
                if(link.RemovedAt is not null)throw new OperationalAccessException(409,"task-attachment-removed");
                link.RemovedAt=time.GetUtcNow();link.RemovedBy=actor.UserId;link.RemovalReason=reason;
            },reason,false,token,authorize:async(db,subjects,ct)=>
            {
                var link=await db.Set<TaskDocumentAttachment>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==attachmentId&&x.TaskId==taskId,ct)??throw new OperationalAccessException(404,"task-attachment-not-found");
                await DocumentService.AttachmentVersion(db,actor,subjects.Single().Id,link.DocumentVersionId,false,ct);
            });
    }

    public async Task<IReadOnlyList<object>> ListDocumentAttachments(ActorContext actor,Guid taskId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        var task=await HoldRead(db,actor,taskId,token);
        var rows=await db.Set<TaskDocumentAttachment>().AsNoTracking().Where(x=>x.TaskId==taskId&&x.RemovedAt==null).OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).Take(20).ToArrayAsync(token);
        var items=new List<object>();
        foreach(var row in rows)
        {
            var version=await DocumentService.AttachmentVersion(db,actor,task.SubjectId,row.DocumentVersionId,false,token);
            items.Add(new{id=row.Id,taskId,version,row.AuthorLabel,row.CreatedAt,row.Reason});
        }
        await transaction.CommitAsync(token);return items;
    }
}
