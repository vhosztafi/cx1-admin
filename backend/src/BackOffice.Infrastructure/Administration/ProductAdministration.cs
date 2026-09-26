using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Administration;

public sealed record ProductVersionEdit(Guid ProviderId, DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo, string[] CoverSections, string Reason);
public sealed record ProductVersionView(Guid Id, Guid ProductId, string ProductCode, string ProductName,
    int Version, string State, Guid ProviderId, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
    string SchemaVersion, string QuestionSetVersion, string[] CoverSections, string Etag);
public sealed record ProviderEdit(string Code, string Name, string State, string Reason);

public sealed class ProductAdministration(IDbContextFactory<BackOfficeDbContext> factory,
    SqlCommandBoundary commands, TimeProvider time)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<object> ListAsync(ActorContext actor, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        await AdminAccess.Authorize(db, actor, token);
        var products = await db.Set<Product>().AsNoTracking().OrderBy(x => x.Code).ToArrayAsync(token);
        var versions = await db.Set<ProductVersion>().AsNoTracking().OrderBy(x => x.ProductId).ThenByDescending(x => x.Version).ToArrayAsync(token);
        var providers = await db.Set<CapacityProvider>().AsNoTracking().OrderBy(x => x.Name).ToArrayAsync(token);
        var result = new { products = products.Select(x => new { x.Id, x.Code, x.Name }),
            versions = versions.Select(x => View(x, products.Single(p => p.Id == x.ProductId))).ToArray(),
            providers = providers.Select(x => new { x.Id, x.Code, x.Name, x.State, etag = AdminAccess.Etag(x.RowVersion) }).ToArray() };
        await tx.CommitAsync(token); return result;
    }

    public Task<CommandOutcome> CloneAsync(ActorContext actor, Guid sourceId, string etag,
        ProductVersionEdit input, string key, CancellationToken token = default)
        => Command(actor, $"/api/v1/admin/product-versions/{sourceId}/clone", key,
            new { etag, input }, "administration.product-draft", async (db, ct) =>
            {
                var source = await Version(db, sourceId, ct); AdminAccess.Version(source, etag);
                var product = await db.Set<Product>().SingleAsync(x => x.Id == source.ProductId, ct);
                Validate(input, product.Code);
                await ActiveProvider(db, input.ProviderId, ct);
                var row = new ProductVersion { ProductId = product.Id, ProviderId = input.ProviderId,
                    Version = await db.Set<ProductVersion>().Where(x => x.ProductId == product.Id).MaxAsync(x => x.Version, ct) + 1,
                    State = "draft", EffectiveFrom = input.EffectiveFrom, EffectiveTo = input.EffectiveTo,
                    JsonSchemaVersion = source.JsonSchemaVersion, QuestionSetVersion = source.QuestionSetVersion,
                    Definition = Definition(source.Definition, input.CoverSections), CreatedBy = actor.UserId };
                db.Add(row); await db.SaveChangesAsync(ct);
                var view = View(row, product);
                AdminAccess.Audit(db, actor, row.Id, "administration.product-cloned", input.Reason,
                    new { sourceId }, view, time.GetUtcNow());
                return Outcome(row.Id, view, 201);
            }, token);

    public Task<CommandOutcome> SaveAsync(ActorContext actor, Guid id, string etag,
        ProductVersionEdit input, string key, CancellationToken token = default)
        => Command(actor, $"/api/v1/admin/product-versions/{id}", key, new { etag, input },
            "administration.product-edited", async (db, ct) =>
            {
                var row = await Version(db, id, ct); AdminAccess.Version(row, etag);
                if (row.State != "draft") throw new QuoteOperationException(409, "published-version-immutable");
                await Unreferenced(db, row.Id, ct);
                var product = await db.Set<Product>().SingleAsync(x => x.Id == row.ProductId, ct);
                Validate(input, product.Code); await ActiveProvider(db, input.ProviderId, ct);
                var before = View(row, product);
                row.ProviderId = input.ProviderId; row.EffectiveFrom = input.EffectiveFrom;
                row.EffectiveTo = input.EffectiveTo; row.Definition = Definition(row.Definition, input.CoverSections);
                row.UpdatedAt = time.GetUtcNow(); await db.SaveChangesAsync(ct);
                var view = View(row, product);
                AdminAccess.Audit(db, actor, id, "administration.product-draft-updated", input.Reason, before, view, time.GetUtcNow());
                return Outcome(id, view);
            }, token);

    public Task<CommandOutcome> PublishAsync(ActorContext actor, Guid id, string etag, string reason,
        string key, CancellationToken token = default)
        => Command(actor, $"/api/v1/admin/product-versions/{id}/publish", key, new { etag, reason },
            "administration.product-published", async (db, ct) =>
            {
                AdminAccess.Text(reason, 1000);
                var row = await Version(db, id, ct); AdminAccess.Version(row, etag);
                if (row.State != "draft") throw new QuoteOperationException(409, "published-version-immutable");
                await Unreferenced(db, row.Id, ct);
                await ActiveProvider(db, row.ProviderId, ct);
                if (await db.Set<ProductVersion>().AnyAsync(x => x.ProductId == row.ProductId && x.Id != id &&
                    x.State == "published" && (row.EffectiveTo == null || x.EffectiveFrom < row.EffectiveTo) &&
                    (x.EffectiveTo == null || x.EffectiveTo > row.EffectiveFrom), ct))
                    throw new QuoteOperationException(409, "product-period-overlap");
                var product = await db.Set<Product>().SingleAsync(x => x.Id == row.ProductId, ct);
                var before = View(row, product);
                // Publication extends the capture/distribution catalogue only. Agency grants remain independent.
                var capture = await db.Set<SettingVersion>().Where(x => x.Scope == "quote-capture")
                    .OrderByDescending(x => x.Version).FirstAsync(ct);
                var distribution = await db.Set<SettingVersion>().Where(x => x.Scope == "agency-distribution")
                    .OrderByDescending(x => x.Version).FirstAsync(ct);
                var captured = QuoteCaptureConfiguration.Parse(capture.Values) ?? throw new QuoteOperationException(409, "capture-settings-invalid");
                var distributed = AgencyDistributionRules.Parse(distribution.Values) ?? throw new QuoteOperationException(409, "distribution-settings-invalid");
                var reference = product.Code == CommercialCaptureRules.ProductCode ? CommercialCaptureRules.ReferenceVersion : QuoteCatalogueIdentity.Version;
                var questions = product.Code == CommercialCaptureRules.ProductCode ? CommercialCaptureRules.QuestionVersion : QuoteCatalogueIdentity.Version;
                var pins = captured.Values.Append(new QuoteCaptureVersion(id, row.JsonSchemaVersion, questions, reference)).ToArray();
                var values = JsonSerializer.Serialize(new { demo = true, kind = "quote-capture", products = pins.Select(x => new {
                    productVersionId = x.ProductVersionId, schemaVersion = x.SchemaVersion, questionSetVersion = x.QuestionSetVersion, referenceVersion = x.ReferenceVersion }) });
                var distributionValues = JsonSerializer.Serialize(new { demo = true, kind = "agency-distribution", productVersionIds = distributed.Append(id).Distinct() });
                if (QuoteCaptureConfiguration.Parse(values) is null || AgencyDistributionRules.Parse(distributionValues) is null)
                    throw new QuoteOperationException(409, "product-catalogue-capacity");
                var effective = time.GetUtcNow();
                if (capture.EffectiveFrom > effective || distribution.EffectiveFrom > effective)
                    throw new QuoteOperationException(409, "future-catalogue-version-pending");
                db.AddRange(new SettingVersion { Scope = capture.Scope, Version = capture.Version + 1,
                    EffectiveFrom = effective, Values = values, CreatedBy = actor.UserId },
                    new SettingVersion { Scope = distribution.Scope, Version = distribution.Version + 1,
                    EffectiveFrom = effective, Values = distributionValues, CreatedBy = actor.UserId });
                row.QuestionSetVersion = questions; row.State = "published"; row.UpdatedAt = effective;
                await db.SaveChangesAsync(ct); var view = View(row, product);
                AdminAccess.Audit(db, actor, id, "administration.product-publication", reason, before, view, effective);
                return Outcome(id, view);
            }, token);

    public Task<CommandOutcome> ProviderAsync(ActorContext actor, Guid? id, string etag,
        ProviderEdit input, string key, CancellationToken token = default)
        => Command(actor, $"/api/v1/admin/providers/{id?.ToString() ?? "new"}", key, new { etag, input },
            "administration.provider-updated", async (db, ct) =>
            {
                var code = AdminAccess.Text(input.Code, 50); var name = AdminAccess.Text(input.Name);
                if (input.State is not ("active" or "inactive") || code.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))
                    throw new QuoteOperationException(400, "provider-input-invalid");
                CapacityProvider row;
                if (id is Guid existing)
                {
                    row = await db.Set<CapacityProvider>().SingleOrDefaultAsync(x => x.Id == existing, ct)
                        ?? throw new QuoteOperationException(404, "provider-not-found");
                    AdminAccess.Version(row, etag);
                    if (row.Code != code) throw new QuoteOperationException(409, "provider-code-immutable");
                }
                else { row = new CapacityProvider { Code = code, CreatedBy = actor.UserId }; db.Add(row); }
                if (await db.Set<CapacityProvider>().AnyAsync(x => x.Code == code && x.Id != row.Id, ct))
                    throw new QuoteOperationException(409, "provider-code-exists");
                var before = new { row.Name, row.State }; row.Name = name; row.State = input.State; row.UpdatedAt = time.GetUtcNow();
                await db.SaveChangesAsync(ct);
                var view = new { row.Id, row.Code, row.Name, row.State, etag = AdminAccess.Etag(row.RowVersion) };
                AdminAccess.Audit(db, actor, row.Id, "administration.provider-change", input.Reason, before, view, time.GetUtcNow());
                return Outcome(row.Id, view, id is null ? 201 : 200);
            }, token);

    private Task<CommandOutcome> Command<T>(ActorContext actor, string route, string key, T input,
        string kind, Func<BackOfficeDbContext, CancellationToken, Task<CommandOutcome>> write, CancellationToken token)
        => commands.ExecuteAuthorizedAsync(new(actor.UserId, route, key, Guid.NewGuid()), input, kind,
            (db, ct) => AdminAccess.Authorize(db, actor, ct), write, token, IsolationLevel.Serializable);
    private static async Task<ProductVersion> Version(BackOfficeDbContext db, Guid id, CancellationToken ct)
        => await db.Set<ProductVersion>().FromSqlInterpolated($"SELECT * FROM ProductVersion WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}")
            .SingleOrDefaultAsync(ct) ?? throw new QuoteOperationException(404, "product-version-not-found");
    private static async Task ActiveProvider(BackOfficeDbContext db, Guid id, CancellationToken ct)
    { if (!await db.Set<CapacityProvider>().AnyAsync(x => x.Id == id && x.State == "active", ct)) throw new QuoteOperationException(400, "provider-unavailable"); }
    private static async Task Unreferenced(BackOfficeDbContext db, Guid id, CancellationToken ct)
    { if (await db.Set<QuoteRevision>().AnyAsync(x => x.ProductVersionId == id, ct) ||
          await db.Set<AgencyProduct>().AnyAsync(x => x.ProductVersionId == id, ct))
        throw new QuoteOperationException(409, "referenced-version-immutable"); }
    private static void Validate(ProductVersionEdit input, string product)
    {
        AdminAccess.Text(input.Reason, 1000);
        if (input.EffectiveFrom.Offset != TimeSpan.Zero || input.EffectiveTo?.Offset != TimeSpan.Zero && input.EffectiveTo != null ||
            input.EffectiveTo <= input.EffectiveFrom || !ProductCoverRules.Valid(product, input.CoverSections))
            throw new QuoteOperationException(400, "product-version-input-invalid");
    }
    private static string Definition(string source, string[] covers)
    { var node = JsonNode.Parse(source)!.AsObject(); node["coverSections"] = JsonSerializer.SerializeToNode(covers); return node.ToJsonString(); }
    private static ProductVersionView View(ProductVersion row, Product product) => new(row.Id, row.ProductId,
        product.Code, product.Name, row.Version, row.State, row.ProviderId, row.EffectiveFrom, row.EffectiveTo,
        row.JsonSchemaVersion, row.QuestionSetVersion, ProductCoverRules.Read(row.Definition, product.Code), AdminAccess.Etag(row.RowVersion));
    private static CommandOutcome Outcome(Guid id, object view, int status = 200)
        => new(id, status, JsonSerializer.Serialize(view, Json));
}
