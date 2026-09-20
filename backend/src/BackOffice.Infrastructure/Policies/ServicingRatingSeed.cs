using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public static class ServicingRatingSeed
{
    public const string Scope = "servicing-rating";
    public const string CommercialScope = "commercial-servicing-rating";
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Servicing seed requires held initialization.");
        // Never replace an operator's published value, even if it is malformed.
        foreach (var (scope, fee) in new[] { (Scope, "15.00"), (CommercialScope, "25.00") })
        {
            if (await db.Set<SettingVersion>().AnyAsync(x => x.Scope == scope, token)) continue;
            var values = JsonSerializer.Serialize(new { demo = true, kind = scope, schemaVersion = "1", currency = "GBP", adjustmentFee = fee, earningBasis = "london-calendar-days" });
            if (ServicingRatingConfiguration.Parse(values, scope) is null) throw new InvalidOperationException("Bundled servicing configuration is invalid.");
            db.Add(new SettingVersion { Scope = scope, Version = 1, EffectiveFrom = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), Values = values });
        }
        await db.SaveChangesAsync(token);
    }
}
