using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public static class QuoteTermsSeed
{
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Terms seed requires held initialization.");
        foreach (var product in await db.Set<Product>().Where(x => x.Code == "motor-trade-road-risks" || x.Code == "motor-trade-combined").ToArrayAsync(token))
            if (!await db.Set<TemplateVersion>().AnyAsync(x => x.Code == "demo-motor-trade-terms" && x.ProductId == product.Id, token))
                db.Add(new TemplateVersion { Code = "demo-motor-trade-terms", ProductId = product.Id, Version = 1,
                    EffectiveFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), EffectiveTo = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    ContentJson = JsonSerializer.Serialize(new { format = "quote-template-1", title = product.Name + " quotation and statement of fact", notice = "Fictional demo quotation. Review the retained risk facts, cover and conditions before accepting." }) });
        if (!await db.Set<SettingVersion>().AnyAsync(x => x.Scope == "quote-delivery", token))
            db.Add(new SettingVersion { Scope = "quote-delivery", Version = 1, EffectiveFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Values = "{\"demo\":true,\"kind\":\"quote-delivery\",\"schemaVersion\":\"1\",\"scenario\":\"success\"}" });
        await db.SaveChangesAsync(token);
    }

    internal static string? Scenario(SettingVersion setting)
    {
        try
        {
            using var doc = JsonDocument.Parse(setting.Values); var root = doc.RootElement;
            var fields = root.EnumerateObject().Select(x => x.Name).ToArray();
            if (setting.Scope != "quote-delivery" || fields.Length != 4 || fields.Distinct().Count() != 4 ||
                root.GetProperty("demo").ValueKind != JsonValueKind.True || root.GetProperty("kind").GetString() != "quote-delivery" || root.GetProperty("schemaVersion").GetString() != "1") return null;
            var scenario = root.GetProperty("scenario").GetString();
            return scenario is "success" or "reject" or "transient-once" or "timeout-after-success" ? scenario : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
    }
}
