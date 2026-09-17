using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public static class ServicingRatingSeed
{
    public const string Scope = "servicing-rating";
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Servicing seed requires held initialization.");
        // Never replace an operator's published value, even if it is malformed.
        if (await db.Set<SettingVersion>().AnyAsync(x => x.Scope == Scope, token)) return;
        var values = JsonSerializer.Serialize(new { demo = true, kind = Scope, schemaVersion = "1", currency = "GBP", adjustmentFee = "15.00", earningBasis = "london-calendar-days" });
        if (ServicingRatingConfiguration.Parse(values) is null) throw new InvalidOperationException("Bundled servicing configuration is invalid.");
        db.Add(new SettingVersion { Scope = Scope, Version = 1, EffectiveFrom = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), Values = values });
        await db.SaveChangesAsync(token);
    }
}
