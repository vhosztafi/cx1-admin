using System.Text.Json;
using BackOffice.Application.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public static class QuoteCaptureDemoSeed
{
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Capture configuration seed requires the demo initialization transaction.");
        // Any existing version, including a revocation or invalid operator setting,
        // takes precedence. Initialization must never repair it by enabling capture.
        if (await db.Set<SettingVersion>().AnyAsync(x => x.Scope == "quote-capture", token)) return;
        var versions = await (from version in db.Set<ProductVersion>()
            join product in db.Set<Product>() on version.ProductId equals product.Id
            join provider in db.Set<CapacityProvider>() on version.ProviderId equals provider.Id
            where version.Version == 1 && version.JsonSchemaVersion == "1.0" && provider.Code == "demo-capacity" &&
                (product.Code == "motor-trade-road-risks" || product.Code == "motor-trade-combined")
            orderby product.Code
            select new { version.Id, product.Code }).ToListAsync(token);
        if (versions.Count != 2 || versions.Select(x => x.Code).Distinct().Count() != 2)
            throw new InvalidOperationException("The two named foundation demo product versions are required for capture initialization.");
        var values = JsonSerializer.Serialize(new { demo = true, kind = "quote-capture", products = versions.Select(x => new {
            productVersionId = x.Id, schemaVersion = "1.0", questionSetVersion = QuoteCatalogueIdentity.Version, referenceVersion = QuoteCatalogueIdentity.Version }) });
        if (QuoteCaptureConfiguration.Parse(values) is null) throw new InvalidOperationException("Bundled capture configuration is invalid.");
        db.Add(new SettingVersion { Scope = "quote-capture", Version = 1, EffectiveFrom = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), Values = values });
        await db.SaveChangesAsync(token);
    }
}
