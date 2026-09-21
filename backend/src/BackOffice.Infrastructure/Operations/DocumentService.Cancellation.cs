using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class DocumentService
{
    // The retained notice work/receipt represent delivery. Generating its PDF is
    // separate technical work, linked once to the same immutable consequence.
    public async Task<CommandOutcome> RegisterCancellationNotice(ActorContext actor,Guid noticeId,string key,CancellationToken token)
    {
        await using var read=await factory.CreateDbContextAsync(token);
        var hint=await read.Set<CancellationConsequence>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==noticeId && x.Kind=="notice",token) ?? throw MissingDocument();
        var registered=await new TaskService(factory,commands,time).Register(actor,new("policy",hint.PolicyId),"cancellation-document-subject/"+noticeId.ToString("N"),token);
        PolicyDocumentRenderService.CancellationSource? original=null;OperationalSubject? subject=null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,"/internal/cancellation-documents/"+noticeId,key,Guid.NewGuid()),new{noticeId},"document.cancellation-registered",
            async(db,ct)=>
            {
                var held=await OperationalScope.HoldSubjects(db,actor,[registered.ResourceId],"document-generate",ct);subject=held.Subjects.Single();
                original=await PolicyDocumentRenderService.LoadCancellation(db,noticeId,hint.PolicyId,actor.UserId,ct);
            },async(db,ct)=>
            {
                await Lock(db,"CoverMGA.CancellationDocument."+noticeId.ToString("N"),ct);
                var existing=await db.Set<DocumentVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.CancellationConsequenceId==noticeId,ct);
                if(existing is not null)return PendingOutcome(existing,"cancellation-notice");
                var now=time.GetUtcNow();
                var template=await db.Set<TemplateVersion>().FromSqlInterpolated($"SELECT * FROM TemplateVersion WITH(HOLDLOCK,ROWLOCK) WHERE ProductId={original!.Policy.ProductId} AND Kind='cancellation-notice'")
                    .AsNoTracking().Where(x=>x.State=="published" && x.EffectiveFrom<=now && x.EffectiveTo>now)
                    .OrderByDescending(x=>x.EffectiveFrom).ThenByDescending(x=>x.Version).ThenBy(x=>x.Id).FirstOrDefaultAsync(ct)
                    ?? throw new DocumentRenderException("document-template-unavailable");
                var input=new DocumentGenerateInput("cancellation-notice",new("policy-version",PolicyVersionId:original.Version.Id),template.Id,"internal","Render original cancellation notice without resending delivery");
                var source=(await PolicyDocumentRenderService.LoadSelected(db,subject!,input,ct)).Input;
                var document=new OperationalDocument{SubjectId=registered.ResourceId,Kind="cancellation-notice",CreatedBy=actor.UserId,CreatedAt=now};
                var version=new DocumentVersion{DocumentId=document.Id,Number=1,SourceKind="policy-version",PolicyVersionId=original.Version.Id,TemplateVersionId=template.Id,
                    SourceHash=source.SourceHash,TemplateHash=source.TemplateHash,CancellationConsequenceId=noticeId,OriginalName=original.Policy.Reference+"-cancellation-notice.pdf",
                    Reason=input.Reason,CreatedBy=actor.UserId,CreatedAt=now};
                var work=new OutboxWork{Kind="document-generation",SubjectRecordId=version.Id,OperationKey="document/"+version.Id.ToString("N"),
                    Payload=GenerationPayload(version),CreatedBy=actor.UserId,CreatedAt=now,UpdatedAt=now,NextAttemptAt=now};
                version.WorkId=work.Id;db.AddRange(document,work);await db.SaveChangesAsync(ct);db.Add(version);await db.SaveChangesAsync(ct);
                return PendingOutcome(version,document.Kind);
            },token);
    }
}
