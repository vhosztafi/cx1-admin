using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class DocumentService
{
    public async Task<CommandOutcome> ReadVersion(ActorContext actor,Guid versionId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        var held=await HoldReadableVersion(db,actor,versionId,"document-read",token);
        var state=await ContentState(db,held.Version,token);
        var view=VersionView(held.Version,held.Document.Kind,state.State,state.File);
        await AddSourceMetadata(db,[(held.Version,view)],token);
        var result=new CommandOutcome(versionId,200,JsonSerializer.Serialize(view,Json));
        await transaction.CommitAsync(token);return result;
    }

    public async Task<OperationalFileDownload> DownloadVersion(ActorContext actor,Guid versionId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        var held=await HoldReadableVersion(db,actor,versionId,"document-download",token);
        var state=await ContentState(db,held.Version,token);
        if(state.State!="ready"||state.File is null)throw new OperationalAccessException(409,"document-not-ready");
        var file=await db.Set<FileObject>().FromSqlInterpolated($"SELECT * FROM FileObject WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={state.File.Id}").SingleAsync(token);
        if(file.SubjectId!=held.Document.SubjectId)throw MissingDocument();
        OperationalFileDownload result;
        try{result=await files.Open(db,file,token);}
        catch(OperationalFileStoreException error) when(error.Code is "file-content-mismatch" or "file-path-invalid" or "file-bytes-missing")
        {
            file.State="quarantined";file.FailureCode=error.Code;
            db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,SubjectRecordId=held.Document.SubjectId,EventType="document.integrity-rejected",
                OccurredAt=time.GetUtcNow(),CorrelationId=Guid.NewGuid(),After=JsonSerializer.Serialize(new{documentVersionId=versionId,code=error.Code},Json)});
            await db.SaveChangesAsync(token);await transaction.CommitAsync(token);throw;
        }
        try{await transaction.CommitAsync(token);return result;}catch{await result.DisposeAsync();throw;}
    }

    private static async Task<(OperationalDocument Document,DocumentVersion Version)> HoldReadableVersion(BackOfficeDbContext db,ActorContext actor,Guid id,string capability,CancellationToken token)
    {
        var version=await db.Set<DocumentVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw MissingDocument();
        var document=await HoldDocument(db,actor,version.DocumentId,capability,token);
        return(document,version);
    }

    private static async Task<OperationalDocument> HoldDocument(BackOfficeDbContext db,ActorContext actor,Guid id,string capability,CancellationToken token)
    {
        var document=await db.Set<OperationalDocument>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw MissingDocument();
        var held=await OperationalScope.HoldSubjects(db,actor,[document.SubjectId],capability,token);
        await HoldAudience(db,held.Subjects.Single(),document.RelationshipId,token);
        return document;
    }

    private static async Task<(string State,FileObject? File)> ContentState(BackOfficeDbContext db,DocumentVersion version,CancellationToken token)
    {
        var file=await(from binding in db.Set<DocumentVersionContent>() join bytes in db.Set<FileObject>() on binding.FileObjectId equals bytes.Id
            where binding.VersionId==version.Id select bytes).AsNoTracking().SingleOrDefaultAsync(token);
        var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==version.WorkId,token);
        return(VersionState(file,work.State),file);
    }
    private static string VersionState(FileObject? file,string workState)=>file?.State=="quarantined"?"quarantined":workState=="failed"?"failed":file?.State=="ready"&&workState=="succeeded"?"ready":"pending";
    private static Dictionary<string,object?> VersionView(DocumentVersion version,string kind,string state,FileObject? file)
    {
        var view=new Dictionary<string,object?>{
            ["id"]=version.Id,["documentId"]=version.DocumentId,["number"]=version.Number,["kind"]=kind,
            ["state"]=state,["originalName"]=version.OriginalName,["bytes"]=file?.ByteLength??0,
            ["contentType"]=file?.MediaType??"application/pdf",["createdAt"]=version.CreatedAt
        };
        if(file is not null)view["sha256"]=file.Sha256;
        if(version.TemplateVersionId is Guid template)view["templateVersionId"]=template;
        if((version.PolicyVersionId??version.QuoteRevisionId??version.ServicingTermsVersionId) is Guid source)view["sourceVersionId"]=source;
        return view;
    }
    private static OperationalAccessException MissingDocument()=>new(404,"document-version-not-found");
}
