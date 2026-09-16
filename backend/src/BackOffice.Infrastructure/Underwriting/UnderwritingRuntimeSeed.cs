using System.Text.Json;
using BackOffice.Application.Agencies;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public static class UnderwritingRuntimeSeed
{
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Runtime seed requires held initialization.");
        if (await db.Set<SettingVersion>().AnyAsync(x => x.Scope == "underwriting-runtime", token)) return;
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var scenarios = new List<SettingVersion>();
        foreach (var scenario in new[] { "success", "reject", "fail-once", "timeout-after-success" })
        {
            var scope = "quote-rating/" + scenario;
            var existing = await db.Set<SettingVersion>().Where(x => x.Scope == scope).OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
            if (existing is not null) { scenarios.Add(existing); continue; }
            var entry = new SettingVersion { Scope = scope, Version = 1, EffectiveFrom = from, Values = JsonSerializer.Serialize(new { demo = true, kind = "quote-rating", scenario }) };
            db.Add(entry); scenarios.Add(entry);
        }
        var team = await db.Set<Team>().SingleAsync(x => x.Name == "Demo operations", token);
        var products = await db.Set<ProductVersion>().Where(x => x.Version == 2 && x.Definition.Contains("motor-trade-underwriting")).ToArrayAsync(token);
        var choices = new List<object>();
        foreach (var version in products)
        {
            var authority = await db.Set<AuthorityVersion>().SingleAsync(x => x.ProductVersionId == version.Id && x.Version == "demo-senior-1", token);
            var rating = await db.Set<RatingRuleVersion>().SingleAsync(x => x.ProductId == version.ProductId, token);
            choices.Add(new { productVersionId = version.Id, ratingRuleVersionId = rating.Id, binderVersionId = authority.BinderVersionId, authorityVersionId = authority.Id });
        }
        if (choices.Count != 2) throw new InvalidOperationException("The two seeded underwriting products are required.");
        var values = JsonSerializer.Serialize(new { demo = true, kind = "underwriting-runtime", schemaVersion = "1",
            scenarioVersionId = scenarios.Single(x => x.Scope == "quote-rating/success").Id, routingTeamId = team.Id, products = choices });
        if (UnderwritingRuntimeConfiguration.Parse(values) is null) throw new InvalidOperationException("Bundled underwriting runtime configuration is invalid.");
        db.Add(new SettingVersion { Scope = "underwriting-runtime", Version = 1, EffectiveFrom = from, Values = values });

        // Add supported versions only where the current configuration still grants
        // their original product. Empty/revoked/malformed operator settings remain
        // authoritative. Existing entries and approved agency terms are retained.
        var capture = await db.Set<SettingVersion>().Where(x => x.Scope == "quote-capture").OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
        var distribution = await db.Set<SettingVersion>().Where(x => x.Scope == "agency-distribution").OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
        var captured = capture is null ? null : QuoteCaptureConfiguration.Parse(capture.Values);
        var distributed = distribution is null ? null : AgencyDistributionRules.Parse(distribution.Values);
        if (captured is not null && distributed is not null)
        {
            var newCapture = captured.Values.ToDictionary(x => x.ProductVersionId); var newDistribution = distributed.ToHashSet();
            foreach (var version in products)
            {
                var original = await db.Set<ProductVersion>().SingleAsync(x => x.ProductId == version.ProductId && x.Version == 1, token);
                if (!captured.ContainsKey(original.Id) || !distributed.Contains(original.Id)) continue;
                newCapture.TryAdd(version.Id, new(version.Id, "1.0", QuoteCatalogueIdentity.Version, QuoteCatalogueIdentity.Version));
                newDistribution.Add(version.Id);
            }
            if (newCapture.Count != captured.Count)
            {
                var json = JsonSerializer.Serialize(new { demo = true, kind = "quote-capture", products = newCapture.Values.Select(x => new {
                    productVersionId = x.ProductVersionId, schemaVersion = x.SchemaVersion, questionSetVersion = x.QuestionSetVersion, referenceVersion = x.ReferenceVersion }) });
                if (QuoteCaptureConfiguration.Parse(json) is null) throw new InvalidOperationException("Additive capture configuration is invalid.");
                db.Add(new SettingVersion { Scope = capture!.Scope, Version = capture.Version + 1, EffectiveFrom = from > capture.EffectiveFrom ? from : capture.EffectiveFrom, Values = json });
            }
            if (newDistribution.Count != distributed.Count)
            {
                var json = JsonSerializer.Serialize(new { demo = true, kind = "agency-distribution", productVersionIds = newDistribution.Order() });
                if (AgencyDistributionRules.Parse(json) is null) throw new InvalidOperationException("Additive distribution configuration is invalid.");
                db.Add(new SettingVersion { Scope = distribution!.Scope, Version = distribution.Version + 1, EffectiveFrom = from > distribution.EffectiveFrom ? from : distribution.EffectiveFrom, Values = json });
            }
        }
        await db.SaveChangesAsync(token);
    }
}
