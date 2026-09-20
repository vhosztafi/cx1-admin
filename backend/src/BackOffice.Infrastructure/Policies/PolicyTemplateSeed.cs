using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public static class PolicyTemplateSeed
{
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Policy template seed requires held initialization.");
        foreach (var product in await db.Set<Product>().Where(x => x.Code == "motor-trade-road-risks" || x.Code == "motor-trade-combined" || x.Code == "commercial-combined").ToArrayAsync(token))
            foreach (var kind in PolicyIssueWriter.DocumentKinds)
                if (!await db.Set<TemplateVersion>().AnyAsync(x => x.ProductId == product.Id && x.Code == "demo-" + kind, token))
                    db.Add(new TemplateVersion { ProductId = product.Id, Code = "demo-" + kind, Kind = kind, Version = 1,
                        EffectiveFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), EffectiveTo = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        ContentJson = JsonSerializer.Serialize(new { format = "policy-template-1", title = product.Name + " " + (product.Code == "commercial-combined" && kind == "policy-certificate" ? "Employers' liability certificate" : kind[7..]), notice = "Fictional demonstration cover. Document generation is requested and remains pending." }) });
        await db.SaveChangesAsync(token);
    }
}
