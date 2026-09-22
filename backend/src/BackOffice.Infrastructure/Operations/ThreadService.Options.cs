using System.Data;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed record CommunicationOptionsPage(IReadOnlyList<object> Items,Guid? NextId);
public sealed partial class ThreadService
{
    public async Task<CommunicationOptionsPage> PackRecipients(ActorContext actor,Guid subjectId,Guid relationshipId,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);await using var db=await factory.CreateDbContextAsync(token);
        await using var transaction=await db.Database.BeginTransactionAsync(token);
        var held=await OperationalScope.HoldSubjects(db,actor,[subjectId],"document-send",token);
        await CommunicationScope.Audience(db,held.Subjects.Single(),relationshipId,token);
        var query=db.Set<Contact>().AsNoTracking().Where(x=>x.RelationshipId==relationshipId&&x.EndedAt==null&&x.Email!=null&&x.CreatedAt<=asOf);
        if(before is Guid id)query=query.Where(x=>x.Id.CompareTo(id)<0);
        var rows=await query.OrderByDescending(x=>x.Id).Take(size+1).Select(x=>new{x.Id,label=x.DeclaredFullName,email=x.Email}).ToArrayAsync(token);
        await transaction.CommitAsync(token);return new(rows.Take(size).Where(x=>CommunicationRules.Email(x.email)).Cast<object>().ToArray(),rows.Length>size?rows[size-1].Id:null);
    }
    public async Task<CommunicationOptionsPage> Relationships(ActorContext actor,Guid subjectId,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);await using var db=await factory.CreateDbContextAsync(token);
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var held=await OperationalScope.HoldSubjects(db,actor,[subjectId],"message-read",token);
        var query=DocumentService.AllowedAudience(db,held.Subjects.Single()).Where(x=>x.CreatedAt<=asOf);
        if(before is Guid id)query=query.Where(x=>x.Id.CompareTo(id)<0);
        var rows=await(from relationship in query join client in db.Set<ClientAccount>() on relationship.ClientId equals client.Id
            orderby relationship.Id descending select new{relationship.Id,label=client.Reference+" · "+client.LegalName}).Take(size+1).ToArrayAsync(token);
        await transaction.CommitAsync(token);return new(rows.Take(size).Cast<object>().ToArray(),rows.Length>size?rows[size-1].Id:null);
    }
    public async Task<CommunicationOptionsPage> RecipientOptions(ActorContext actor,Guid threadId,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);await using var db=await factory.CreateDbContextAsync(token);
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var held=await CommunicationScope.HoldThread(db,actor,threadId,"message-read",token);
        if(held.Thread.Visibility=="internal"){await transaction.CommitAsync(token);return new([],null);}
        var query=db.Set<Contact>().AsNoTracking().Where(x=>x.RelationshipId==held.Thread.RelationshipId&&x.EndedAt==null&&x.Email!=null&&x.CreatedAt<=asOf);
        if(before is Guid id)query=query.Where(x=>x.Id.CompareTo(id)<0);
        var rows=await query.OrderByDescending(x=>x.Id).Take(size+1).Select(x=>new{x.Id,label=x.DeclaredFullName,email=x.Email}).ToArrayAsync(token);
        // A page may be sparse because invalid mailboxes are never offered. Its
        // cursor advances over examined rows; no misleading eligible total.
        await transaction.CommitAsync(token);return new(rows.Take(size).Where(x=>CommunicationRules.Email(x.email)).Cast<object>().ToArray(),rows.Length>size?rows[size-1].Id:null);
    }
    public async Task<CommunicationOptionsPage> AttachmentOptions(ActorContext actor,Guid threadId,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);await using var db=await factory.CreateDbContextAsync(token);
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var held=await CommunicationScope.HoldThread(db,actor,threadId,"message-read",token);var thread=held.Thread;
        var query=from version in db.Set<DocumentVersion>() join document in db.Set<OperationalDocument>() on version.DocumentId equals document.Id
            join content in db.Set<DocumentVersionContent>() on version.Id equals content.VersionId join file in db.Set<FileObject>() on content.FileObjectId equals file.Id
            join work in db.Set<OutboxWork>() on version.WorkId equals work.Id
            where document.SubjectId==thread.SubjectId&&version.CreatedAt<=asOf&&file.State=="ready"&&work.State=="succeeded"&&
                (thread.Visibility=="internal"||document.Visibility=="agency"&&document.RelationshipId==thread.RelationshipId)
            select version;
        if(before is Guid id)query=query.Where(x=>x.Id.CompareTo(id)<0);
        var rows=await query.OrderByDescending(x=>x.Id).Take(size+1).Select(x=>x.Id).ToArrayAsync(token);var items=new List<object>();
        foreach(var versionId in rows.Take(size))items.Add(await DocumentService.AttachmentVersion(db,actor,thread.SubjectId,versionId,false,token));
        await transaction.CommitAsync(token);return new(items,rows.Length>size?rows[size-1]:null);
    }
}
