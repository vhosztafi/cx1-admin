using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public static class DocumentTemplateSeed
{
    public static readonly DateTimeOffset EffectiveFrom = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);

    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Document template publication requires held initialization.");
        // Ensure older dates keep their original template availability even
        // when operational seeds are requested before underwriting seeds.
        await PolicyTemplateSeed.SeedAsync(db, token);
        foreach (var product in await db.Set<Product>().Where(x => x.Code == "motor-trade-road-risks" || x.Code == "motor-trade-combined" || x.Code == "commercial-combined").ToArrayAsync(token))
        foreach (var kind in new[] { "policy-schedule", "policy-certificate", "policy-statement", "endorsement", "cancellation-notice" })
        {
            var version = kind.StartsWith("policy-", StringComparison.Ordinal) ? 2 : 1;
            var code = "demo-" + kind;
            if (await db.Set<TemplateVersion>().AnyAsync(x => x.ProductId == product.Id && x.Code == code && x.Version == version, token)) continue;
            var title = kind switch
            {
                "policy-schedule" => "Policy schedule", "policy-statement" => "Statement of fact",
                "policy-certificate" => product.Code == "commercial-combined" ? "Employers' liability certificate" : "Motor insurance certificate",
                "endorsement" => "Selected endorsements", _ => "Cancellation notice"
            };
            db.Add(new TemplateVersion { ProductId = product.Id, Code = code, Version = version, Kind = kind, State = "published",
                EffectiveFrom = EffectiveFrom, EffectiveTo = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ContentJson = JsonSerializer.Serialize(new { format = "document-template-1", productCode = product.Code,
                    kind = DocumentRenderContract.CanonicalKind(kind), title = product.Name + " — " + title,
                    notice = "Fictional demonstration only. No insurance is provided. This document reproduces the identified retained source; subsequent changes require a separate document version." }) });
        }
        await db.SaveChangesAsync(token);
    }
}
