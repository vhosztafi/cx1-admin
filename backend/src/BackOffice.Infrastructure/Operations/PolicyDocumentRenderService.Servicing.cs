using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class PolicyDocumentRenderService
{
    public async Task<RenderedPolicyDocument> RenderServicingTerms(ActorContext actor, Guid draftId, Guid termsId, string kind, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await OperationalScope.HoldParents(db, actor, [new("servicing-draft", draftId)], "document-read", token);
        var input = await LoadServicingTerms(db, draftId, termsId, kind, token);
        var result = renderer.Render(input);
        await transaction.CommitAsync(token); return result;
    }

    private static async Task<DocumentRenderInput> LoadServicingTerms(BackOfficeDbContext db, Guid draftId, Guid termsId, string kind, CancellationToken token)
    {
        var draft = await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x => x.Id == draftId, token);
        var policy = await db.Set<Policy>().AsNoTracking().SingleAsync(x => x.Id == draft.PolicyId, token);
        var terms = await db.Set<ServicingTermsVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == termsId && x.DraftId == draftId, token) ?? throw Missing();
        var cycle = await db.Set<ServicingCycle>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == terms.CycleId && x.DraftId == draftId &&
            x.PolicyId == policy.Id && x.ProductId == policy.ProductId && x.BaseVersionId == terms.BaseVersionId && x.RevisionId == terms.RevisionId, token) ?? throw Missing();
        if (!await db.Set<PolicyVersion>().AnyAsync(x => x.Id == terms.BaseVersionId && x.PolicyId == policy.Id && x.TermId == cycle.BaseTermId, token) ||
            !await db.Set<ServicingRevision>().AnyAsync(x => x.Id == terms.RevisionId && x.DraftId == draftId, token)) throw Missing();
        var rating = await db.Set<ServicingRatingResult>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == terms.RatingId && x.CycleId == cycle.Id && x.RevisionId == terms.RevisionId, token) ?? throw Missing();
        var template = await db.Set<TemplateVersion>().FromSqlInterpolated($"SELECT * FROM TemplateVersion WITH(HOLDLOCK,ROWLOCK) WHERE Id={terms.TemplateVersionId}")
            .AsNoTracking().SingleOrDefaultAsync(token) ?? throw Missing();
        if (template.ProductId != policy.ProductId) throw Missing();
        var product = await db.Set<Product>().AsNoTracking().SingleAsync(x => x.Id == policy.ProductId, token);
        var input = new DocumentRenderInput(terms.Id, "servicing-terms", terms.TermsJson, terms.TermsHash, template.Id, template.ContentJson, Hash(template.ContentJson),
            product.Code, kind, policy.Reference, product.Code, template.Kind);
        var root = DocumentRenderContract.Create(input).Source;
        if (root.GetProperty("draftId").GetGuid() != draft.Id || root.GetProperty("cycleId").GetGuid() != cycle.Id ||
            root.GetProperty("revisionId").GetGuid() != terms.RevisionId || root.GetProperty("baseVersionId").GetGuid() != terms.BaseVersionId ||
            root.GetProperty("baseTermId").GetGuid() != cycle.BaseTermId || root.GetProperty("policyId").GetGuid() != policy.Id ||
            root.GetProperty("ratingId").GetGuid() != rating.Id || root.GetProperty("inputHash").GetString() != Convert.ToHexStringLower(cycle.InputHash) ||
            root.GetProperty("ratingHash").GetString() != Convert.ToHexStringLower(rating.ResultHash) ||
            root.GetProperty("format").GetString() != (draft.Kind == "renewal" ? "renewal-contract-1" : "servicing-contract-1")) throw Invalid();
        using var ratingInput = JsonDocument.Parse(cycle.InputJson); using var ratingOutput = JsonDocument.Parse(rating.ResultJson);
        if (!JsonElement.DeepEquals(root.GetProperty("ratingInput"), ratingInput.RootElement) || !JsonElement.DeepEquals(root.GetProperty("rating"), ratingOutput.RootElement)) throw Invalid();
        return input;
    }
}
