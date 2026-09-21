using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class PolicyDocumentRenderService
{
    public async Task<RenderedPolicyDocument> RenderSelectedVersion(ActorContext actor, Guid versionId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var version = await db.Set<DocumentVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==versionId,token) ?? throw Missing();
        var document = await db.Set<OperationalDocument>().AsNoTracking().SingleAsync(x=>x.Id==version.DocumentId,token);
        var held = await OperationalScope.HoldSubjects(db, actor, [document.SubjectId], "document-generate", token);
        var selection = new DocumentGenerateInput(document.Kind,new(version.SourceKind,version.PolicyVersionId,version.QuoteRevisionId,version.ServicingTermsVersionId,version.QuoteTermsVersionId),
            version.TemplateVersionId ?? throw Missing(),document.Visibility,version.Reason,document.RelationshipId,document.Id);
        var input = await LoadSelectedPolicy(db, held.Subjects.Single(), selection, token);
        if(input.SourceHash!=version.SourceHash || input.TemplateHash!=version.TemplateHash || input.QuoteTerms?.Hash!=version.TermsHash)
            throw new DocumentRenderException("document-version-source-mismatch");
        var result = renderer.Render(input);
        await transaction.CommitAsync(token);
        return result;
    }

    // The caller holds current identity and typed parent locks through use.
    // Historical versions are explicit; neither source nor template is selected by latest.
    internal static async Task<DocumentRenderInput> LoadSelectedPolicy(BackOfficeDbContext db, OperationalSubject subject,
        DocumentGenerateInput selection, CancellationToken token)
    {
        DocumentRules.Validate(selection);
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Document sources require a held transaction.");
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
        return input;
    }
}
