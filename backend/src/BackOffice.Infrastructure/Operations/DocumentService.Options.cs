using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed record DocumentGenerationChoice(string Kind,string Label,Guid TemplateVersionId,string TemplateLabel,DocumentSourceInput Source);
public sealed record DocumentGenerationOptions(string ProductCode,string SourceKind,Guid SourceVersionId,string SourceLabel,DateTimeOffset SourceDate,
    IReadOnlyList<DocumentGenerationChoice> Items,Guid? NextTemplateId);

public sealed partial class DocumentService
{
    // The picker validates the exact same owned source as the eventual command.
    // Pagination is over candidate templates, so an empty page can still have a
    // continuation when none of its templates apply to the selected cover.
    public async Task<DocumentGenerationOptions> GenerationOptions(ActorContext actor,Guid subjectId,DocumentSourceInput source,
        Guid? afterTemplateId,int size,DateTimeOffset asOf,CancellationToken token)
    {
        ValidatePage(0,size);
        if(source is null)throw new DocumentRuleException("invalid-document-source");
        var sourceId=source.PolicyVersionId??source.QuoteRevisionId??source.TermsVersionId??Guid.Empty;
        DocumentRules.Validate(new DocumentGenerateInput("statement-of-fact",source,sourceId,"internal","Select an applicable document template"));
        await using var db=await factory.CreateDbContextAsync(token);
        await using var transaction=await db.Database.BeginTransactionAsync(token);
        var held=await OperationalScope.HoldSubjects(db,actor,[subjectId],"document-generate",token);
        var subject=held.Subjects.Single();var owned=await DescribeSelection(db,subject,source,token);
        var product=await db.Set<Product>().AsNoTracking().SingleAsync(x=>x.Id==owned.ProductId,token);
        var candidates=db.Set<TemplateVersion>().FromSqlInterpolated($"SELECT * FROM TemplateVersion WITH(HOLDLOCK,ROWLOCK) WHERE ProductId={owned.ProductId}")
            .AsNoTracking().Where(x=>x.State=="published" && x.EffectiveFrom<=asOf && x.EffectiveTo>asOf &&
                (owned.TemplateId==null || x.Id==owned.TemplateId));
        if(afterTemplateId is Guid after)
        {
            if(!await candidates.AnyAsync(x=>x.Id==after,token))throw new OperationalAccessException(400,"invalid-document-template-cursor");
            candidates=candidates.Where(x=>x.Id.CompareTo(after)>0);
        }
        var templates=await candidates.OrderBy(x=>x.Id).Take(size+1).ToArrayAsync(token);
        var choices=new List<DocumentGenerationChoice>();
        foreach(var template in templates.Take(size))
        foreach(var kind in CandidateKinds(source,template.Kind))
        {
            try
            {
                var input=new DocumentGenerateInput(kind,source,template.Id,"internal","Select an applicable document template");
                var selected=await PolicyDocumentRenderService.LoadSelected(db,subject,input,token);
                var contract=DocumentRenderContract.Create(selected.Input);
                choices.Add(new(kind,DocumentKindLabel(kind),template.Id,contract.Title+" · template v"+template.Version,source));
            }
            catch(DocumentRenderException){ /* Unsupported kind, cover or retained template: never offer it. */ }
        }
        await transaction.CommitAsync(token);
        return new(product.Code,source.Kind,sourceId,owned.Label,owned.Date,choices,templates.Length>size?templates[size-1].Id:null);
    }

    private sealed record SelectionDescription(Guid ProductId,string Label,DateTimeOffset Date,Guid? TemplateId=null);
    private static async Task<SelectionDescription> DescribeSelection(BackOfficeDbContext db,OperationalSubject subject,DocumentSourceInput source,CancellationToken token)
    {
        if(source.Kind=="policy-version" && subject.PolicyId is Guid policyId)
        {
            var version=await db.Set<PolicyVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==source.PolicyVersionId && x.PolicyId==policyId,token) ?? throw MissingDocument();
            var policy=await db.Set<Policy>().AsNoTracking().SingleAsync(x=>x.Id==policyId,token);
            var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x=>x.Id==version.TermId && x.PolicyId==policyId,token);
            return new(policy.ProductId,$"Term {term.Number} · policy version {version.Sequence}",version.EffectiveAt);
        }
        if(source.Kind=="quote-revision" && subject.QuoteId is Guid quoteId)
        {
            var quote=await db.Set<Quote>().AsNoTracking().SingleAsync(x=>x.Id==quoteId,token);
            var revision=await db.Set<QuoteRevision>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==source.QuoteRevisionId && x.QuoteId==quoteId &&
                x.ClientId==quote.ClientId && x.RelationshipId==quote.RelationshipId && x.AgencyId==quote.AgencyId && x.ProductId==quote.ProductId,token) ?? throw MissingDocument();
            Guid? templateId=null;
            if(source.QuoteTermsVersionId is Guid termsId)
            {
                var terms=await db.Set<QuoteTermsVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==termsId && x.QuoteId==quoteId,token) ?? throw MissingDocument();
                if(!await db.Set<UnderwritingCycle>().AnyAsync(x=>x.Id==terms.CycleId && x.QuoteId==quoteId && x.QuoteRevisionId==revision.Id,token))throw MissingDocument();
                templateId=terms.TemplateVersionId;
            }
            return new(quote.ProductId,$"Quote revision {revision.Number}",revision.SavedAt,templateId);
        }
        if(source.Kind=="servicing-terms")
        {
            var terms=await db.Set<ServicingTermsVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==source.TermsVersionId,token) ?? throw MissingDocument();
            var draft=await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x=>x.Id==terms.DraftId,token);
            if(subject.ServicingDraftId!=draft.Id && subject.PolicyId!=draft.PolicyId)throw MissingDocument();
            var policy=await db.Set<Policy>().AsNoTracking().SingleAsync(x=>x.Id==draft.PolicyId,token);
            return new(policy.ProductId,$"{(draft.Kind=="renewal"?"Renewal":"Adjustment")} terms {terms.Sequence}",terms.PreparedAt,terms.TemplateVersionId);
        }
        throw MissingDocument();
    }

    private static string[] CandidateKinds(DocumentSourceInput source,string templateKind)=>source.Kind switch
    {
        "policy-version"=>templateKind is "policy-schedule" or "policy-certificate" or "policy-statement" or "endorsement" or "cancellation-notice"
            ?[DocumentRenderContract.CanonicalKind(templateKind)]:[],
        "quote-revision"=>templateKind=="quote-terms" ?source.QuoteTermsVersionId is null?["statement-of-fact"]:["quotation","statement-of-fact"]
            :templateKind is "policy-statement" or "statement-of-fact"?["statement-of-fact"]:[],
        "servicing-terms"=>templateKind=="servicing-terms"?["quotation","statement-of-fact"]:templateKind=="renewal-invitation"?["renewal-invitation","statement-of-fact"]:[],
        _=>[]
    };
    private static string DocumentKindLabel(string kind)=>kind switch
    {
        "policy-schedule"=>"Policy schedule","policy-certificate"=>"Certificate","statement-of-fact"=>"Statement of fact",
        "quotation"=>"Quotation","endorsement"=>"Endorsements","renewal-invitation"=>"Renewal invitation","cancellation-notice"=>"Cancellation notice",_=>kind
    };
}
