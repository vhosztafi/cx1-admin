using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlCapacityDeadlineSeedAppendsExactDefaultUpgradeAndPreservesOperatorSettings()
    {
        await WithDatabase(async (db, _) =>
        {
            var legacy = new SettingVersion { Scope = "capacity-escalation/approve-stock-150000", Version = 1, EffectiveFrom = Now,
                Values = JsonSerializer.Serialize(new { demo = true, kind = "capacity-escalation", schemaVersion = "1", scenario = "approve-stock-150000", responseDueHours = 48 }) };
            var custom = new SettingVersion { Scope = "capacity-escalation/query-proof", Version = 1, EffectiveFrom = Now,
                Values = JsonSerializer.Serialize(new { demo = true, kind = "capacity-escalation", schemaVersion = "1", scenario = "query-proof", responseDueHours = 72 }) };
            db.AddRange(legacy, custom); await db.SaveChangesAsync();
            await using (var tx = await db.Database.BeginTransactionAsync()) { await CapacitySeed.SeedAsync(db); await tx.CommitAsync(); }
            var upgrade = await db.Set<SettingVersion>().SingleAsync(x => x.Scope == legacy.Scope && x.Version == 2);
            using var values = JsonDocument.Parse(upgrade.Values);
            Assert.Equal(2, values.RootElement.GetProperty("responseDueWorkingDays").GetInt32());
            Assert.Equal(legacy.Values, (await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Id == legacy.Id)).Values);
            Assert.Single(await db.Set<SettingVersion>().Where(x => x.Scope == custom.Scope).ToArrayAsync());
            Assert.Equal(custom.Values, (await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Id == custom.Id)).Values);
            var retained = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope.StartsWith("capacity-escalation/"))
                .OrderBy(x => x.Id).Select(x => new { x.Id, x.Scope, x.Version, x.EffectiveFrom, x.Values }).ToArrayAsync();
            await using (var tx = await db.Database.BeginTransactionAsync()) { await CapacitySeed.SeedAsync(db); await tx.CommitAsync(); }
            var repeated = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope.StartsWith("capacity-escalation/"))
                .OrderBy(x => x.Id).Select(x => new { x.Id, x.Scope, x.Version, x.EffectiveFrom, x.Values }).ToArrayAsync();
            Assert.Equal(retained, repeated);
        });
    }
}
