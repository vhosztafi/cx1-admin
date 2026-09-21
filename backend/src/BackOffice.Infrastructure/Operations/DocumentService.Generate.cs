using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class DocumentService
{
    public Task<CommandOutcome> Generate(ActorContext actor, Guid subjectId, DocumentGenerateInput input, string key, CancellationToken token)
    {
        DocumentRules.Validate(input);
        PolicyDocumentRenderService.SelectedSource? selected = null;
        OperationalDocument? document = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,"/api/v1/subjects/"+subjectId+"/documents/generate",key,Guid.NewGuid()),input,"document.generation-requested",
            async(db,ct)=>
            {
                var held = await OperationalScope.HoldSubjects(db,actor,[subjectId],"document-generate",ct);
                selected = await PolicyDocumentRenderService.LoadSelected(db,held.Subjects.Single(),input,ct);
                if(input.DocumentId is Guid documentId)
                {
                    document = await db.Set<OperationalDocument>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==documentId && x.SubjectId==subjectId,ct)
                        ?? throw MissingDocument();
                    if(document.Kind!=input.Kind || document.Visibility!=input.Visibility || document.RelationshipId!=input.RelationshipId)
                        throw new DocumentRuleException("document-regeneration-target-mismatch");
                }
            },
            async(db,ct)=>
            {
                var now=time.GetUtcNow();
                var template=await db.Set<TemplateVersion>().AsNoTracking().SingleAsync(x=>x.Id==input.TemplateVersionId,ct);
                if(template.State!="published" || template.EffectiveFrom>now || template.EffectiveTo<=now)
                    throw new DocumentRuleException("document-template-unavailable");
                var number=1;
                if(document is null)
                {
                    document=new OperationalDocument{SubjectId=subjectId,Kind=input.Kind,Visibility=input.Visibility,RelationshipId=input.RelationshipId,
                        CreatedBy=actor.UserId,CreatedAt=now};
                    db.Add(document);
                }
                else
                {
                    await Lock(db,"CoverMGA.Document."+document.Id.ToString("N"),ct);
                    number=1+(await db.Set<DocumentVersion>().Where(x=>x.DocumentId==document.Id).MaxAsync(x=>(int?)x.Number,ct)??0);
                }
                var source=selected!.Input;
                var version=new DocumentVersion{DocumentId=document.Id,Number=number,SourceKind=input.Source.Kind,PolicyVersionId=input.Source.PolicyVersionId,
                    QuoteRevisionId=input.Source.QuoteRevisionId,QuoteTermsVersionId=input.Source.QuoteTermsVersionId,ServicingTermsVersionId=input.Source.TermsVersionId,
                    TermsHash=selected.TermsHash,TemplateVersionId=input.TemplateVersionId,SourceHash=source.SourceHash,TemplateHash=source.TemplateHash,
                    OriginalName=source.Reference+"-"+input.Kind+".pdf",Reason=input.Reason,CreatedBy=actor.UserId,CreatedAt=now};
                var work=new OutboxWork{Kind="document-generation",SubjectRecordId=version.Id,OperationKey="document/"+version.Id.ToString("N"),
                    CreatedBy=actor.UserId,CreatedAt=now,UpdatedAt=now,NextAttemptAt=now,Payload=GenerationPayload(version)};
                version.WorkId=work.Id;
                // Work precedes the guarded version insert; both commit together.
                db.Add(work);await db.SaveChangesAsync(ct);
                db.Add(version);await db.SaveChangesAsync(ct);
                return PendingOutcome(version,input.Kind);
            },token);
    }

    internal static string GenerationPayload(DocumentVersion version)=>JsonSerializer.Serialize(new{
        format="document-generation-1",documentVersionId=version.Id,documentId=version.DocumentId,
        sourceKind=version.SourceKind,sourceVersionId=version.PolicyVersionId??version.QuoteRevisionId??version.ServicingTermsVersionId,
        quoteTermsVersionId=version.QuoteTermsVersionId,templateVersionId=version.TemplateVersionId,
        sourceHash=version.SourceHash,termsHash=version.TermsHash,templateHash=version.TemplateHash
    },Json);
}
