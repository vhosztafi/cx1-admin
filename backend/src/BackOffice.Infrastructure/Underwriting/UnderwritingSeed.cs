using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public static class UnderwritingSeed
{
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Underwriting seed requires the held initialization transaction.");
        // An immutable marker makes operator retirement/revocation authoritative.
        // Never recreate a missing/revoked grant as a side effect of startup.
        if (await db.Set<SettingVersion>().AnyAsync(x => x.Scope == "underwriting-demo-initialized", token)) return;
        using var stream = typeof(UnderwritingSeed).Assembly.GetManifestResourceStream("UnderwritingDemo.Definitions")!;
        using var examples = JsonDocument.Parse(stream);
        var definitions = examples.RootElement.GetProperty("configurations").EnumerateArray().ToArray();
        var provider = await db.Set<CapacityProvider>().SingleAsync(x => x.Code == "demo-capacity", token);
        var grantor = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example", token);
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var code in new[] { "motor-trade-road-risks", "motor-trade-combined" })
        {
            var product = await db.Set<Product>().SingleAsync(x => x.Code == code, token);
            if (await db.Set<ProductVersion>().AnyAsync(x => x.ProductId == product.Id && x.Version == 2, token))
                throw new InvalidOperationException("Underwriting demo product slot is already occupied; explicit reconciliation is required.");
            var productVersion = new ProductVersion { ProductId = product.Id, Version = 2, ProviderId = provider.Id,
                State = "published", EffectiveFrom = from, EffectiveTo = to, JsonSchemaVersion = "1.0", QuestionSetVersion = QuoteCatalogueIdentity.Version,
                Definition = JsonSerializer.Serialize(new { demo = true, kind = "motor-trade-underwriting", schemaVersion = "1", productCode = code,
                    referenceVersion = QuoteCatalogueIdentity.Version, requestedSectionsRequired = true, ratingAvailable = true }), CreatedBy = grantor.Id };
            db.Add(productVersion);
            JsonElement Find(string kind) => definitions.Single(x => x.GetProperty("kind").GetString() == kind && x.GetProperty("productCode").GetString() == code);
            var rating = Find("rating");
            if (!UnderwritingConfiguration.Valid(rating, "rating")) throw new InvalidOperationException("Invalid bundled rating definition.");
            db.Add(new RatingRuleVersion { ProductId = product.Id, Version = rating.GetProperty("version").GetString()!,
                EffectiveFrom = from, EffectiveTo = to, DefinitionJson = rating.GetRawText(), CreatedBy = grantor.Id });
            var binderJson = JsonNode.Parse(Find("binder").GetRawText())!; binderJson["providerId"] = provider.Id.ToString("D");
            var binderDefinition = JsonSerializer.SerializeToElement(binderJson);
            if (!UnderwritingConfiguration.Valid(binderDefinition, "binder")) throw new InvalidOperationException("Invalid bundled binder definition.");
            var binder = new BinderVersion { ProductId = product.Id, ProviderId = provider.Id, Version = "demo-binder-1",
                EffectiveFrom = from, EffectiveTo = to, DefinitionJson = binderDefinition.GetRawText(), CreatedBy = grantor.Id };
            db.Add(binder);
            await db.SaveChangesAsync(token);
            foreach (var role in new[] { "underwriter", "senior-underwriter" })
            {
                var version = role == "underwriter" ? "demo-underwriter-1" : "demo-senior-1";
                var definition = definitions.Single(x => x.GetProperty("kind").GetString() == "authority" && x.GetProperty("productCode").GetString() == code && x.GetProperty("version").GetString() == version);
                if (!UnderwritingConfiguration.WithinBinder(definition, binderDefinition)) throw new InvalidOperationException("Bundled authority exceeds its binder.");
                var authority = new AuthorityVersion { ProductId = product.Id, ProductVersionId = productVersion.Id, BinderVersionId = binder.Id,
                    Version = version, EffectiveFrom = from, EffectiveTo = to, DefinitionJson = definition.GetRawText(), CreatedBy = grantor.Id };
                db.Add(authority);
                var user = await db.Set<StaffUser>().SingleAsync(x => x.Email == role + "@cover.example", token);
                db.Add(new UserAuthorityGrant { UserId = user.Id, AuthorityVersionId = authority.Id, GrantedBy = grantor.Id, CreatedBy = grantor.Id,
                    EffectiveFrom = from, EffectiveTo = to, Reason = "Initial fictional underwriting authority; subject to current role and operator revocation." });
            }
            await db.SaveChangesAsync(token);
        }
        db.Add(new SettingVersion { Scope = "underwriting-demo-initialized", Version = 1, EffectiveFrom = from,
            Values = "{\"demo\":true,\"kind\":\"underwriting-initialized\",\"schemaVersion\":\"1\"}", CreatedBy = grantor.Id });
        await db.SaveChangesAsync(token);
        // No approved agency terms or capture/distribution settings are rewritten.
        // Their explicit adoption creates new versions in the progression service.
    }
}
