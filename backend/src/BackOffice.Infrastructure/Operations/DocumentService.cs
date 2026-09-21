using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class DocumentService(IDbContextFactory<BackOfficeDbContext> factory, SqlCommandBoundary commands,
    PolicyDocumentRenderService sources, TimeProvider time, FileService files)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Import the original business intent, preserving its work and originator.
    // Public explicit regeneration is a separate command and version.
    public async Task<CommandOutcome> RegisterRetainedRequest(ActorContext actor, Guid requestId, string key, CancellationToken token)
    {
        if (!actor.HasCapability("document-generate")) throw new OperationalAccessException(403,"document-generation-forbidden");
        var source = await sources.LoadRetainedRequest(actor,requestId,token);
        await using var read = await factory.CreateDbContextAsync(token);
        var original = await read.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Id==requestId,token);
        if(original.CreatedBy!=actor.UserId) throw new OperationalAccessException(403,"document-originator-required");
        var subject = await new TaskService(factory,commands,time).Register(actor,new("policy",original.PolicyId),"document-subject/"+requestId.ToString("N"),token);
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,"/internal/document-requests/"+requestId,key,Guid.NewGuid()),new{requestId},"document.request-registered",
            async(db,ct)=>{await OperationalScope.HoldSubjects(db,actor,[subject.ResourceId],"document-generate",ct);},
            async(db,ct)=>
            {
                await Lock(db,"CoverMGA.DocumentRequest."+requestId.ToString("N"),ct);
                var existing=await db.Set<DocumentVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.PolicyDocumentRequestId==requestId,ct);
                if(existing is not null) return PendingOutcome(existing,DocumentRenderContract.CanonicalKind(original.Kind));
                var now=time.GetUtcNow();
                var document=new OperationalDocument{SubjectId=subject.ResourceId,Kind=DocumentRenderContract.CanonicalKind(original.Kind),CreatedBy=actor.UserId,CreatedAt=now};
                var version=new DocumentVersion{DocumentId=document.Id,Number=1,SourceKind="policy-version",PolicyVersionId=original.VersionId,
                    TemplateVersionId=original.TemplateVersionId,SourceHash=source.SourceHash,TemplateHash=source.TemplateHash,
                    PolicyDocumentRequestId=original.Id,WorkId=original.WorkId,OriginalName=source.Reference+"-"+document.Kind+".pdf",
                    Reason="Original "+original.Purpose+" document request",CreatedBy=actor.UserId,CreatedAt=now};
                db.AddRange(document,version);await db.SaveChangesAsync(ct);return PendingOutcome(version,document.Kind);
            },token);
    }

    internal static CommandOutcome PendingOutcome(DocumentVersion version,string kind)=>new(version.Id,202,JsonSerializer.Serialize(new{
        id=version.Id,documentId=version.DocumentId,number=version.Number,kind,state="pending",originalName=version.OriginalName,
        bytes=0,contentType="application/pdf",sourceVersionId=version.PolicyVersionId??version.QuoteRevisionId??version.ServicingTermsVersionId,
        templateVersionId=version.TemplateVersionId,createdAt=version.CreatedAt
    },Json));

    internal static async Task Lock(BackOfficeDbContext db,string resource,CancellationToken token)
    {
        await using var command=db.Database.GetDbConnection().CreateCommand();command.Transaction=db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText="DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; SELECT @result;";
        var parameter=command.CreateParameter();parameter.ParameterName="@resource";parameter.Value=resource;command.Parameters.Add(parameter);
        if(Convert.ToInt32(await command.ExecuteScalarAsync(token))<0)throw new CommandBusyException();
    }
}
