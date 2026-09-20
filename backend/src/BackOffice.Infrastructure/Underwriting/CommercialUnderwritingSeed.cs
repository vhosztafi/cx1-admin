using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Agencies;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public static class CommercialUnderwritingSeed
{
    public const string MarkerScope = "commercial-underwriting-initialized";
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Commercial underwriting publication requires held initialization.");
        if (await db.Set<SettingVersion>().AnyAsync(x => x.Scope == MarkerScope, token)) return;
        var product = await db.Set<Product>().SingleAsync(x => x.Code == CommercialCaptureRules.ProductCode, token);
        var original = await db.Set<ProductVersion>().SingleAsync(x => x.ProductId == product.Id && x.Version == 2, token);
        if (await db.Set<ProductVersion>().AnyAsync(x => x.ProductId == product.Id && x.Version == 3, token))
            throw new InvalidOperationException("Commercial underwriting product slot is occupied; explicit reconciliation is required.");
        var grantor = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example", token);
        using var stream = typeof(CommercialUnderwritingSeed).Assembly.GetManifestResourceStream("CommercialUnderwriting.Definitions")!;
        using var definitions = JsonDocument.Parse(stream);
        var ratingJson = definitions.RootElement.GetProperty("rating");
        var binderNode = JsonNode.Parse(definitions.RootElement.GetProperty("binder").GetRawText())!;
        binderNode["providerId"] = original.ProviderId.ToString("D");
        var binderJson = JsonSerializer.SerializeToElement(binderNode);
        var authorityJson = definitions.RootElement.GetProperty("authority");
        if (!CommercialUnderwritingConfiguration.Valid(ratingJson, "rating") || !CommercialUnderwritingConfiguration.WithinBinder(authorityJson, binderJson))
            throw new InvalidOperationException("Invalid bundled Commercial Combined underwriting definitions.");
        var from = ratingJson.GetProperty("effectiveFrom").GetDateTimeOffset();
        var to = ratingJson.GetProperty("effectiveTo").GetDateTimeOffset();
        var version = new ProductVersion { ProductId = product.Id, ProviderId = original.ProviderId, Version = 3, State = "published",
            EffectiveFrom = from, EffectiveTo = to, JsonSchemaVersion = "1.0", QuestionSetVersion = CommercialCaptureRules.QuestionVersion, CreatedBy = grantor.Id,
            Definition = JsonSerializer.Serialize(new { demo = true, kind = "commercial-combined-underwriting", schemaVersion = "1.0", productCode = product.Code,
                captureFormat = CommercialCaptureRules.Format, questionSetVersion = CommercialCaptureRules.QuestionVersion,
                referenceVersion = CommercialCaptureRules.ReferenceVersion, captureAvailable = true, ratingAvailable = true }) };
        var rating = new RatingRuleVersion { ProductId = product.Id, Version = ratingJson.GetProperty("version").GetString()!,
            EffectiveFrom = from, EffectiveTo = to, DefinitionJson = ratingJson.GetRawText(), CreatedBy = grantor.Id };
        var binder = new BinderVersion { ProductId = product.Id, ProviderId = original.ProviderId, Version = binderJson.GetProperty("version").GetString()!,
            EffectiveFrom = from, EffectiveTo = to, DefinitionJson = binderJson.GetRawText(), CreatedBy = grantor.Id };
        db.AddRange(version, rating, binder); await db.SaveChangesAsync(token);
        var authority = new AuthorityVersion { ProductId = product.Id, ProductVersionId = version.Id, BinderVersionId = binder.Id,
            Version = authorityJson.GetProperty("version").GetString()!, EffectiveFrom = from, EffectiveTo = to, DefinitionJson = authorityJson.GetRawText(), CreatedBy = grantor.Id };
        db.Add(authority); await db.SaveChangesAsync(token);

        // Publish definitions without granting agency or staff authority. Adoption of v3
        // and decision grants is explicit; immutable v2 remains capture-only.
        var capture = await Latest(db, "quote-capture", token);
        var distribution = await Latest(db, "agency-distribution", token);
        var runtime = await Latest(db, "underwriting-runtime", token);
        var captured = capture is null ? null : QuoteCaptureConfiguration.Parse(capture.Values);
        var distributed = distribution is null ? null : AgencyDistributionRules.Parse(distribution.Values);
        var running = runtime is null ? null : UnderwritingRuntimeConfiguration.Parse(runtime.Values);
        if (original.State == "published" && captured?.ContainsKey(original.Id) == true && distributed?.Contains(original.Id) == true && running?.Products.Count > 0)
        {
            var newCapture = JsonNode.Parse(capture!.Values)!;
            newCapture["products"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { productVersionId = version.Id, schemaVersion = "1.0",
                questionSetVersion = CommercialCaptureRules.QuestionVersion, referenceVersion = CommercialCaptureRules.ReferenceVersion }));
            var newDistribution = JsonNode.Parse(distribution!.Values)!;
            newDistribution["productVersionIds"]!.AsArray().Add(version.Id.ToString("D"));
            var newRuntime = JsonNode.Parse(runtime!.Values)!;
            newRuntime["products"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { productVersionId = version.Id, ratingRuleVersionId = rating.Id, binderVersionId = binder.Id, authorityVersionId = authority.Id }));
            if (QuoteCaptureConfiguration.Parse(newCapture.ToJsonString()) is null || AgencyDistributionRules.Parse(newDistribution.ToJsonString()) is null ||
                UnderwritingRuntimeConfiguration.Parse(newRuntime.ToJsonString()) is null) throw new InvalidOperationException("Invalid additive Commercial Combined runtime configuration.");
            foreach (var (prior, values) in new[] { (capture, newCapture), (distribution, newDistribution), (runtime, newRuntime) })
                db.Add(new SettingVersion { Scope = prior.Scope, Version = prior.Version + 1, EffectiveFrom = prior.EffectiveFrom > from ? prior.EffectiveFrom : from, Values = values.ToJsonString(), CreatedBy = grantor.Id });
        }
        db.Add(new SettingVersion { Scope = MarkerScope, Version = 1, EffectiveFrom = from, CreatedBy = grantor.Id,
            Values = JsonSerializer.Serialize(new { demo = true, kind = MarkerScope, productVersionId = version.Id, ratingRuleVersionId = rating.Id, binderVersionId = binder.Id, authorityVersionId = authority.Id }) });
        await db.SaveChangesAsync(token);
    }
    private static Task<SettingVersion?> Latest(BackOfficeDbContext db, string scope, CancellationToken token) =>
        db.Set<SettingVersion>().Where(x => x.Scope == scope).OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
}
