using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using BackOffice.Infrastructure.Quotes;
using System.Text.Json;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class PolicyDocumentRenderService
{
    internal sealed record SelectedSource(DocumentRenderInput Input, string? TermsHash);
    public async Task<RenderedPolicyDocument> RenderSelectedVersion(ActorContext actor, Guid versionId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var version = await db.Set<DocumentVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==versionId,token) ?? throw Missing();
        var document = await db.Set<OperationalDocument>().AsNoTracking().SingleAsync(x=>x.Id==version.DocumentId,token);
        var held = await OperationalScope.HoldSubjects(db, actor, [document.SubjectId], "document-generate", token);
        if(version.CancellationConsequenceId is Guid consequenceId)
        {
            var original=await LoadCancellation(db,consequenceId,held.Subjects.Single().PolicyId ?? throw Missing(),actor.UserId,token);
            if(original.Version.Id!=version.PolicyVersionId)throw Invalid();
        }
        var selection = new DocumentGenerateInput(document.Kind,new(version.SourceKind,version.PolicyVersionId,version.QuoteRevisionId,version.ServicingTermsVersionId,version.QuoteTermsVersionId),
            version.TemplateVersionId ?? throw Missing(),document.Visibility,version.Reason,document.RelationshipId,document.Id);
        var selected = await LoadSelected(db, held.Subjects.Single(), selection, token);
        var input = selected.Input;
        if(input.SourceHash!=version.SourceHash || input.TemplateHash!=version.TemplateHash || selected.TermsHash!=version.TermsHash)
            throw new DocumentRenderException("document-version-source-mismatch");
        var result = renderer.Render(input);
        await transaction.CommitAsync(token);
        return result;
    }

    // The caller holds current identity and typed parent locks through use.
    // Historical versions are explicit; neither source nor template is selected by latest.
    internal static async Task<SelectedSource> LoadSelected(BackOfficeDbContext db, OperationalSubject subject,
        DocumentGenerateInput selection, CancellationToken token)
    {
        DocumentRules.Validate(selection);
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Document sources require a held transaction.");
        if (selection.Source.Kind == "quote-revision") return await LoadSelectedQuote(db, subject, selection, token);
        if (selection.Source.Kind == "servicing-terms") return await LoadSelectedServicing(db, subject, selection, token);
        if (selection.Source.Kind != "policy-version" || subject.PolicyId is not Guid policyId) throw Missing();
        var policy = await db.Set<Policy>().AsNoTracking().SingleAsync(x => x.Id == policyId, token);
        if (selection.RelationshipId is Guid relationship && relationship != policy.RelationshipId) throw Missing();
        var version = await db.Set<PolicyVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == selection.Source.PolicyVersionId && x.PolicyId == policyId, token) ?? throw Missing();
        var template = await db.Set<TemplateVersion>().FromSqlInterpolated($"SELECT * FROM TemplateVersion WITH(HOLDLOCK,ROWLOCK) WHERE Id={selection.TemplateVersionId}")
            .AsNoTracking().SingleOrDefaultAsync(token) ?? throw Missing();
        if (template.ProductId != policy.ProductId) throw Missing();
        var product = await db.Set<Product>().AsNoTracking().SingleAsync(x => x.Id == policy.ProductId, token);
        var input = new DocumentRenderInput(version.Id, "policy-version", version.SnapshotJson, Convert.ToHexStringLower(version.ContentHash),
            template.Id, template.ContentJson, Hash(template.ContentJson), product.Code, selection.Kind, policy.Reference, product.Code, template.Kind);
        _ = DocumentRenderContract.Create(input);
        return new(input,null);
    }

    private static async Task<SelectedSource> LoadSelectedQuote(BackOfficeDbContext db, OperationalSubject subject,
        DocumentGenerateInput selection, CancellationToken token)
    {
        if(subject.QuoteId is not Guid quoteId) throw Missing();
        var quote=await db.Set<Quote>().AsNoTracking().SingleAsync(x=>x.Id==quoteId,token);
        if(selection.RelationshipId is Guid relationship && relationship!=quote.RelationshipId) throw Missing();
        var revision=await db.Set<QuoteRevision>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==selection.Source.QuoteRevisionId && x.QuoteId==quoteId &&
            x.ClientId==quote.ClientId && x.RelationshipId==quote.RelationshipId && x.AgencyId==quote.AgencyId && x.ProductId==quote.ProductId,token) ?? throw Missing();
        var template=await db.Set<TemplateVersion>().FromSqlInterpolated($"SELECT * FROM TemplateVersion WITH(HOLDLOCK,ROWLOCK) WHERE Id={selection.TemplateVersionId}")
            .AsNoTracking().SingleOrDefaultAsync(token) ?? throw Missing();
        if(template.ProductId!=revision.ProductId) throw Missing();
        var product=await db.Set<Product>().AsNoTracking().SingleAsync(x=>x.Id==revision.ProductId,token);
        var input=new DocumentRenderInput(revision.Id,"quote-revision",revision.ProposalJson,Convert.ToHexStringLower(revision.ContentHash),
            template.Id,template.ContentJson,Hash(template.ContentJson),product.Code,selection.Kind,quote.Reference,product.Code,template.Kind,QuoteService.Pins(revision));
        string? termsHash=null;
        if(selection.Source.QuoteTermsVersionId is Guid termsId)
        {
            var terms=await db.Set<QuoteTermsVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==termsId && x.QuoteId==quoteId && x.TemplateVersionId==template.Id,token) ?? throw Missing();
            var cycle=await db.Set<UnderwritingCycle>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==terms.CycleId && x.QuoteId==quoteId && x.QuoteRevisionId==revision.Id,token) ?? throw Missing();
            var rating=await db.Set<QuoteRatingResult>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==terms.RatingId && x.CycleId==cycle.Id && x.QuoteId==quoteId,token) ?? throw Missing();
            var priced=input with {Kind="quotation",QuoteTerms=new(terms.Id,terms.TermsJson,terms.TermsHash)};
            var root=DocumentRenderContract.Create(priced).QuotationTerms!.Value;
            if(root.GetProperty("quoteId").GetGuid()!=quoteId || root.GetProperty("cycleId").GetGuid()!=cycle.Id ||
                root.GetProperty("ratingId").GetGuid()!=rating.Id || root.GetProperty("clientId").GetGuid()!=revision.ClientId ||
                root.GetProperty("relationshipId").GetGuid()!=revision.RelationshipId) throw Invalid();
            using var result=JsonDocument.Parse(rating.ResultJson);
            if(!JsonElement.DeepEquals(root.GetProperty("rating"),result.RootElement)) throw Invalid();
            termsHash=terms.TermsHash;
            if(selection.Kind=="quotation") input=priced;
        }
        _=DocumentRenderContract.Create(input);
        return new(input,termsHash);
    }

    private static async Task<SelectedSource> LoadSelectedServicing(BackOfficeDbContext db, OperationalSubject subject,
        DocumentGenerateInput selection, CancellationToken token)
    {
        var terms=await db.Set<ServicingTermsVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==selection.Source.TermsVersionId,token) ?? throw Missing();
        var draft=await db.Set<ServicingDraft>().FromSqlInterpolated($"SELECT * FROM ServicingDraft WITH(HOLDLOCK,ROWLOCK) WHERE Id={terms.DraftId}")
            .AsNoTracking().SingleAsync(token);
        if(subject.ServicingDraftId!=draft.Id && subject.PolicyId!=draft.PolicyId || selection.TemplateVersionId!=terms.TemplateVersionId) throw Missing();
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync(x=>x.Id==draft.PolicyId,token);
        if(selection.RelationshipId is Guid relationship && relationship!=policy.RelationshipId) throw Missing();
        return new(await LoadServicingTerms(db,draft.Id,terms.Id,selection.Kind,token),null);
    }
}
