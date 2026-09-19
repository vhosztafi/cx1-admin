using System.Text.Json;
using BackOffice.Application.Agencies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public static class CommercialCaptureSeed
{
    public const string MarkerScope = "commercial-capture-initialized";

    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Commercial capture seed requires held initialization.");
        if (await db.Set<SettingVersion>().AnyAsync(x => x.Scope == MarkerScope, token)) return;
        var product = await db.Set<Product>().SingleAsync(x => x.Code == CommercialCaptureRules.ProductCode, token);
        var original = await db.Set<ProductVersion>().SingleAsync(x => x.ProductId == product.Id && x.Version == 1, token);
        if (await db.Set<ProductVersion>().AnyAsync(x => x.ProductId == product.Id && x.Version == 2, token))
            throw new InvalidOperationException("Commercial product slot is occupied; explicit reconciliation is required.");
        var from = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var grantor = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example", token);
        var version = new ProductVersion { ProductId = product.Id, ProviderId = original.ProviderId, Version = 2, State = "published",
            EffectiveFrom = from, EffectiveTo = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), JsonSchemaVersion = "1.0",
            QuestionSetVersion = CommercialCaptureRules.QuestionVersion, CreatedBy = grantor.Id,
            Definition = JsonSerializer.Serialize(new { demo = true, kind = "commercial-combined-capture", schemaVersion = "1.0",
                productCode = CommercialCaptureRules.ProductCode, captureFormat = CommercialCaptureRules.Format,
                questionSetVersion = CommercialCaptureRules.QuestionVersion, referenceVersion = CommercialCaptureRules.ReferenceVersion,
                captureAvailable = true, ratingAvailable = false }) };
        db.Add(version);

        var capture = await db.Set<SettingVersion>().Where(x => x.Scope == "quote-capture").OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
        var distribution = await db.Set<SettingVersion>().Where(x => x.Scope == "agency-distribution").OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
        var captured = capture is null ? null : QuoteCaptureConfiguration.Parse(capture.Values);
        var distributed = distribution is null ? null : AgencyDistributionRules.Parse(distribution.Values);
        // Publication adds a catalogue entry, never approved agency access. A prior
        // global withdrawal, malformed configuration or CC distribution revocation
        // is authoritative. The marker prevents startup from re-granting later.
        if (captured is { Count: > 0 } && distributed?.Contains(original.Id) == true && original.State != "retired")
        {
            var newCapture = captured.Values.Append(new QuoteCaptureVersion(version.Id, "1.0", CommercialCaptureRules.QuestionVersion, CommercialCaptureRules.ReferenceVersion)).ToArray();
            var captureJson = JsonSerializer.Serialize(new { demo = true, kind = "quote-capture", products = newCapture.Select(x => new {
                productVersionId = x.ProductVersionId, schemaVersion = x.SchemaVersion, questionSetVersion = x.QuestionSetVersion, referenceVersion = x.ReferenceVersion }) });
            var distributionJson = JsonSerializer.Serialize(new { demo = true, kind = "agency-distribution", productVersionIds = distributed.Append(version.Id).Order() });
            if (QuoteCaptureConfiguration.Parse(captureJson) is null || AgencyDistributionRules.Parse(distributionJson) is null)
                throw new InvalidOperationException("Additive commercial capture configuration is invalid.");
            db.Add(new SettingVersion { Scope = capture!.Scope, Version = capture.Version + 1, EffectiveFrom = capture.EffectiveFrom > from ? capture.EffectiveFrom : from,
                Values = captureJson, CreatedBy = grantor.Id });
            db.Add(new SettingVersion { Scope = distribution!.Scope, Version = distribution.Version + 1, EffectiveFrom = distribution.EffectiveFrom > from ? distribution.EffectiveFrom : from,
                Values = distributionJson, CreatedBy = grantor.Id });
        }
        db.Add(new SettingVersion { Scope = MarkerScope, Version = 1, EffectiveFrom = from, CreatedBy = grantor.Id,
            Values = JsonSerializer.Serialize(new { demo = true, kind = MarkerScope, productVersionId = version.Id,
                questionSetVersion = CommercialCaptureRules.QuestionVersion, referenceVersion = CommercialCaptureRules.ReferenceVersion }) });
        await db.SaveChangesAsync(token);
    }
}
