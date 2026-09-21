using System.Data;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed record DocumentReadPage(IReadOnlyList<object> Items,int TotalCount,int? NextNumber=null,Guid? NextDocumentId=null);

public sealed partial class DocumentService
{
    public async Task<DocumentReadPage> ListDocuments(ActorContext actor,Guid subjectId,Guid? beforeId,int size,DateTimeOffset asOf,CancellationToken token)
    {
        ValidatePage(0,size);
        await using var db=await factory.CreateDbContextAsync(token);
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        await OperationalScope.HoldSubjects(db,actor,[subjectId],"document-read",token);
        var query=db.Set<OperationalDocument>().AsNoTracking().Where(x=>x.SubjectId==subjectId && x.CreatedAt<=asOf &&
            (x.RelationshipId==null || db.Set<ClientAgencyRelationship>().Any(r=>r.Id==x.RelationshipId && r.State=="active")));
        var count=await query.CountAsync(token);
        if(beforeId is Guid previousId)
        {
            var previous=await query.SingleOrDefaultAsync(x=>x.Id==previousId,token) ?? throw new OperationalAccessException(400,"invalid-document-cursor");
            query=query.Where(x=>x.CreatedAt<previous.CreatedAt || x.CreatedAt==previous.CreatedAt && x.Id.CompareTo(previous.Id)<0);
        }
        var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(size+1)
            .Select(x=>new{x.Id,subjectRecordId=x.SubjectId,x.Kind,x.Visibility,x.RelationshipId,
                currentVersionId=db.Set<DocumentVersion>().Where(v=>v.DocumentId==x.Id && v.CreatedAt<=asOf).OrderByDescending(v=>v.Number).Select(v=>(Guid?)v.Id).FirstOrDefault()})
            .ToArrayAsync(token);
        await transaction.CommitAsync(token);
        return new(rows.Take(size).Cast<object>().ToArray(),count,NextDocumentId:rows.Length>size?rows[size-1].Id:null);
    }

    public async Task<DocumentReadPage> ListVersions(ActorContext actor,Guid documentId,int offset,int size,DateTimeOffset asOf,CancellationToken token)
    {
        ValidatePage(offset,size);
        await using var db=await factory.CreateDbContextAsync(token);
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var document=await HoldDocument(db,actor,documentId,"document-read",token);
        var query=db.Set<DocumentVersion>().AsNoTracking().Where(x=>x.DocumentId==documentId && x.CreatedAt<=asOf);
        var count=await query.CountAsync(token);
        // Cursor position is the last immutable sequence, not a shifting row offset.
        var page=query.Where(x=>offset==0 || x.Number<offset).OrderByDescending(x=>x.Number).Take(size+1);
        var rows=await(from version in page join work in db.Set<OutboxWork>() on version.WorkId equals work.Id
            join binding in db.Set<DocumentVersionContent>() on version.Id equals binding.VersionId into bindings
            from binding in bindings.DefaultIfEmpty()
            join file in db.Set<FileObject>() on binding.FileObjectId equals file.Id into files
            from file in files.DefaultIfEmpty()
            orderby version.Number descending select new{Version=version,File=file,work.State}).AsNoTracking().ToArrayAsync(token);
        var items=rows.Take(size).Select(x=>(object)VersionView(x.Version,document.Kind,VersionState(x.File,x.State),x.File)).ToArray();
        await transaction.CommitAsync(token);
        return new(items,count,NextNumber:rows.Length>size?rows[size-1].Version.Number:null);
    }

    private static void ValidatePage(int offset,int size)
    {
        if(size is <1 or >100 || offset<0 || offset>int.MaxValue-size) throw new OperationalAccessException(400,"invalid-document-page");
    }
}
