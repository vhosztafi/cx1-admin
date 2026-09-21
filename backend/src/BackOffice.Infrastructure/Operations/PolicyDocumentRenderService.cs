using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

// Internal composition for the document generation owner. Rendering is not
// publication: no request is marked ready and no DocumentVersion is invented.
public sealed class PolicyDocumentRenderService(IDbContextFactory<BackOfficeDbContext> factory, IPolicyDocumentRenderer renderer)
{
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
