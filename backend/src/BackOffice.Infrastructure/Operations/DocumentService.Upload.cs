using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class DocumentService
{
    // Attach the public upload identity. Bytes and technical finalization stay
    // in the existing upload flow; no second file or generation intent is made.
    public Task<CommandOutcome> AttachUpload(ActorContext actor,Guid subjectId,DocumentUploadInput input,string key,CancellationToken token)
    {
        DocumentRules.Validate(input);
        FileObject? file=null;OperationalDocument? document=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,"/api/v1/records/"+subjectId+"/documents/upload",key,Guid.NewGuid()),input,"document.upload-attached",
            async(db,ct)=>
            {
                var held=await OperationalScope.HoldSubjects(db,actor,[subjectId],"document-upload",ct);
                await HoldAudience(db,held.Subjects.Single(),input.RelationshipId,ct);
                file=await db.Set<FileObject>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==input.UploadId && x.SubjectId==subjectId && x.CreatedBy==actor.UserId && x.StorageKind=="local",ct)
                    ?? throw MissingDocument();
                if(input.DocumentId is Guid documentId)
                {
                    document=await db.Set<OperationalDocument>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==documentId && x.SubjectId==subjectId,ct) ?? throw MissingDocument();
                    if(document.Kind!=input.Kind || document.Visibility!=input.Visibility || document.RelationshipId!=input.RelationshipId)
                        throw new DocumentRuleException("document-regeneration-target-mismatch");
                }
            },
            async(db,ct)=>
            {
                await Lock(db,"CoverMGA.File."+file!.Id.ToString("N"),ct);
                file=await db.Set<FileObject>().FromSqlInterpolated($"SELECT * FROM FileObject WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={file.Id}").SingleAsync(ct);
                if(file.State=="quarantined")throw new OperationalAccessException(409,"document-upload-quarantined");
                var existing=await(from binding in db.Set<DocumentVersionContent>() join candidate in db.Set<DocumentVersion>() on binding.VersionId equals candidate.Id
                    where binding.FileObjectId==file.Id select candidate).AsNoTracking().SingleOrDefaultAsync(ct);
                if(existing is not null)
                {
                    var owner=await db.Set<OperationalDocument>().AsNoTracking().SingleAsync(x=>x.Id==existing.DocumentId,ct);
                    if(existing.SourceKind!="upload" || existing.WorkId!=input.UploadId || owner.SubjectId!=subjectId || owner.Kind!=input.Kind || owner.Visibility!=input.Visibility || owner.RelationshipId!=input.RelationshipId ||
                        input.DocumentId is Guid target && target!=owner.Id || existing.Reason!=input.Reason)
                        throw new OperationalAccessException(409,"document-upload-already-attached");
                    return await UploadVersionOutcome(db,existing,owner.Kind,file,ct);
                }
                var now=time.GetUtcNow();var number=1;
                if(document is null)
                {
                    document=new OperationalDocument{SubjectId=subjectId,Kind=input.Kind,Visibility=input.Visibility,RelationshipId=input.RelationshipId,CreatedBy=actor.UserId,CreatedAt=now};
                    db.Add(document);
                }
                else
                {
                    await Lock(db,"CoverMGA.Document."+document.Id.ToString("N"),ct);
                    number=1+(await db.Set<DocumentVersion>().Where(x=>x.DocumentId==document.Id).MaxAsync(x=>(int?)x.Number,ct)??0);
                }
                var version=new DocumentVersion{DocumentId=document.Id,Number=number,SourceKind="upload",WorkId=input.UploadId,
                    OriginalName=file.FileName,Reason=input.Reason,CreatedBy=actor.UserId,CreatedAt=now};
                db.AddRange(version,new DocumentVersionContent{VersionId=version.Id,FileObjectId=file.Id,CreatedBy=actor.UserId,CreatedAt=now});
                await db.SaveChangesAsync(ct);
                return await UploadVersionOutcome(db,version,document.Kind,file,ct);
            },token);
    }

    private static async Task<CommandOutcome> UploadVersionOutcome(BackOfficeDbContext db,DocumentVersion version,string kind,FileObject file,CancellationToken token)
    {
        var state=await db.Set<OutboxWork>().Where(x=>x.Id==version.WorkId).Select(x=>x.State).SingleAsync(token);
        return new(version.Id,202,JsonSerializer.Serialize(VersionView(version,kind,VersionState(file,state),file),Json));
    }
}
