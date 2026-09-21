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
        var view=new Dictionary<string,object?>{
            ["id"]=held.Version.Id,["documentId"]=held.Document.Id,["number"]=held.Version.Number,["kind"]=held.Document.Kind,
            ["state"]=state.State,["originalName"]=held.Version.OriginalName,["bytes"]=state.File?.ByteLength??0,
            ["contentType"]=state.File?.MediaType??"application/pdf",["createdAt"]=held.Version.CreatedAt
        };
        if(state.File is not null)view["sha256"]=state.File.Sha256;
        if(held.Version.TemplateVersionId is Guid template)view["templateVersionId"]=template;
        if((held.Version.PolicyVersionId??held.Version.QuoteRevisionId??held.Version.ServicingTermsVersionId) is Guid source)view["sourceVersionId"]=source;
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
        var document=await db.Set<OperationalDocument>().AsNoTracking().SingleAsync(x=>x.Id==version.DocumentId,token);
        await OperationalScope.HoldSubjects(db,actor,[document.SubjectId],capability,token);
        if(document.RelationshipId is Guid relationship&&!await db.Set<ClientAgencyRelationship>().AnyAsync(x=>x.Id==relationship&&x.State=="active",token))throw MissingDocument();
        return(document,version);
    }

    private static async Task<(string State,FileObject? File)> ContentState(BackOfficeDbContext db,DocumentVersion version,CancellationToken token)
    {
        var file=await(from binding in db.Set<DocumentVersionContent>() join bytes in db.Set<FileObject>() on binding.FileObjectId equals bytes.Id
            where binding.VersionId==version.Id select bytes).AsNoTracking().SingleOrDefaultAsync(token);
        var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==version.WorkId,token);
        var state=file?.State=="quarantined"?"quarantined":work.State=="failed"?"failed":file?.State=="ready"&&work.State=="succeeded"?"ready":"pending";
        return(state,file);
    }
    private static OperationalAccessException MissingDocument()=>new(404,"document-version-not-found");
}
