using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public static class QuoteLookupDemoSeed
{
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Lookup configuration requires the demo seed transaction.");
        foreach (var scenario in new[] { "success", "no-match", "multiple", "reject", "fail-once", "timeout-after-success" })
        {
            var scope = "quote-lookup/" + scenario;
            // Preserve every operator version, including deliberate disablement.
            if (await db.Set<SettingVersion>().AnyAsync(x => x.Scope == scope, token)) continue;
            db.Add(new SettingVersion { Scope = scope, Version = 1,
                EffectiveFrom = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                Values = JsonSerializer.Serialize(new { demo = true, kind = "quote-lookup", scenario }) });
        }
        await db.SaveChangesAsync(token);
    }
}
