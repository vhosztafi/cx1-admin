using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

// Internal composition for the document generation owner. Rendering is not
// publication: no request is marked ready and no DocumentVersion is invented.
public sealed partial class PolicyDocumentRenderService(IDbContextFactory<BackOfficeDbContext> factory, IPolicyDocumentRenderer renderer)
{
    public async Task<RenderedPolicyDocument> RenderQuoteTerms(ActorContext actor, Guid quoteId, Guid termsId, string kind, CancellationToken token = default)
    {
        if (kind is not ("quotation" or "statement-of-fact")) throw Invalid();
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await OperationalScope.HoldParents(db, actor, [new("quote", quoteId)], "document-read", token);
        var quote = await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == quoteId, token);
        var terms = await db.Set<QuoteTermsVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == termsId && x.QuoteId == quoteId, token) ?? throw Missing();
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == terms.CycleId && x.QuoteId == quoteId, token) ?? throw Missing();
        var revision = await db.Set<QuoteRevision>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == cycle.QuoteRevisionId && x.QuoteId == quoteId &&
            x.ClientId == quote.ClientId && x.RelationshipId == quote.RelationshipId && x.AgencyId == quote.AgencyId && x.ProductId == quote.ProductId, token) ?? throw Missing();
        var template = await db.Set<TemplateVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == terms.TemplateVersionId && x.ProductId == revision.ProductId, token) ?? throw Missing();
        var product = await db.Set<Product>().AsNoTracking().SingleAsync(x => x.Id == revision.ProductId, token);
        var input = new DocumentRenderInput(revision.Id, "quote-revision", revision.ProposalJson, Convert.ToHexStringLower(revision.ContentHash),
            template.Id, template.ContentJson, Hash(template.ContentJson), product.Code, "quotation", quote.Reference, product.Code, template.Kind,
            QuoteService.Pins(revision), new(terms.Id, terms.TermsJson, terms.TermsHash));
        // Always validate the chosen retained terms, even for an unpriced
        // statement of fact. An arbitrary terms ID cannot select a revision.
        var validated = DocumentRenderContract.Create(input).QuotationTerms!.Value;
        if (validated.GetProperty("quoteId").GetGuid() != quote.Id || validated.GetProperty("cycleId").GetGuid() != cycle.Id ||
            validated.GetProperty("ratingId").GetGuid() != terms.RatingId || validated.GetProperty("clientId").GetGuid() != revision.ClientId ||
            validated.GetProperty("relationshipId").GetGuid() != revision.RelationshipId) throw Invalid();
        if (kind == "statement-of-fact") input = input with { Kind = kind, QuoteTerms = null };
        var result = renderer.Render(input);
        await transaction.CommitAsync(token); return result;
    }

    public async Task<RenderedPolicyDocument> RenderRetainedRequest(ActorContext actor, Guid requestId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var hint = await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId, token) ?? throw Missing();
        await OperationalScope.HoldParents(db, actor, [new("policy", hint.PolicyId)], "document-read", token);
        var request = await db.Set<PolicyDocumentRequest>().FromSqlInterpolated($"SELECT * FROM PolicyDocumentRequest WITH(HOLDLOCK,ROWLOCK) WHERE Id={requestId}").AsNoTracking().SingleAsync(token);
        if (request.PolicyId != hint.PolicyId) throw Missing();
        var policy = await db.Set<Policy>().AsNoTracking().SingleAsync(x => x.Id == request.PolicyId, token);
        var version = await db.Set<PolicyVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.VersionId && x.PolicyId == policy.Id && x.TermId == request.TermId && x.TransactionId == request.TransactionId, token) ?? throw Missing();
        var template = await db.Set<TemplateVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.TemplateVersionId && x.ProductId == policy.ProductId, token) ?? throw Missing();
        var product = await db.Set<Product>().AsNoTracking().SingleAsync(x => x.Id == policy.ProductId, token);
        var input = new DocumentRenderInput(version.Id, "policy-version", version.SnapshotJson, Convert.ToHexStringLower(version.ContentHash),
            template.Id, template.ContentJson, Hash(template.ContentJson), product.Code, request.Kind, policy.Reference, product.Code, template.Kind);
        _ = DocumentRenderContract.Create(input);
        if (Encoding.UTF8.GetByteCount(request.PayloadJson) > 16 * 1024 * 1024 ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(request.PayloadJson)), request.PayloadHash)) throw Invalid();
        try
        {
            using var payload = JsonDocument.Parse(request.PayloadJson);
            using var source = JsonDocument.Parse(version.SnapshotJson);
            using var content = JsonDocument.Parse(template.ContentJson);
            var root = payload.RootElement;
            if (!Unique(root) || root.GetProperty("format").GetString() != "policy-document-1" ||
                root.GetProperty("requestId").GetGuid() != request.Id || root.GetProperty("policyId").GetGuid() != policy.Id ||
                root.GetProperty("termId").GetGuid() != version.TermId || root.GetProperty("transactionId").GetGuid() != version.TransactionId ||
                root.GetProperty("versionId").GetGuid() != version.Id || root.GetProperty("templateVersionId").GetGuid() != template.Id ||
                root.GetProperty("kind").GetString() != request.Kind || root.GetProperty("contentHash").GetString() != input.SourceHash ||
                root.GetProperty("policyReference").GetString() != policy.Reference ||
                !JsonElement.DeepEquals(root.GetProperty("snapshot"), source.RootElement) || !JsonElement.DeepEquals(root.GetProperty("template"), content.RootElement))
                throw Invalid();
            if (root.TryGetProperty("commercial", out var commercial))
            {
                if (product.Code != "commercial-combined") throw Invalid();
                var end = await db.Set<PolicyTerm>().Where(x => x.Id == version.TermId && x.PolicyId == policy.Id).Select(x => x.EndsAt).SingleAsync(token);
                if (!CommercialDocumentPayload.Valid(commercial, new(policy.Id, version.Id, input.SourceHash, version.SnapshotJson, version.EffectiveAt, end), request.Kind)) throw Invalid();
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw Invalid(); }
        var result = renderer.Render(input);
        await transaction.CommitAsync(token); return result;
    }
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static bool Unique(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count() && value.EnumerateObject().All(x => Unique(x.Value)),
        JsonValueKind.Array => value.EnumerateArray().All(Unique), _ => true
    };
    private static OperationalAccessException Missing() => new(404, "document-source-not-found");
    private static DocumentRenderException Invalid() => new("document-request-provenance-invalid");
}
