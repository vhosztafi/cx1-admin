using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    // Older posting tests deliberately force closed/reopened periods to isolate their
    // source guards. Keep the 10-11 close trigger enabled for all behavior assertions.
    private static Task SetLegacyPeriodStateRaw(BackOfficeDbContext db, string sql)
        => WithFixtureTrigger(db, period: true,
            () => db.Database.ExecuteSqlRawAsync(sql));

    private static Task SetLegacyPeriodState(BackOfficeDbContext db, FormattableString sql)
        => WithFixtureTrigger(db, period: true,
            () => db.Database.ExecuteSqlInterpolatedAsync(sql));

    // The 10-02 posting test predates source-bound corrections and isolates its
    // independent agency/period/immutability guards. 10-11 tests the source guard.
    private static Task InsertLegacyCorrection(BackOfficeDbContext db)
        => WithFixtureTrigger(db, period: false,
            () => db.SaveChangesAsync());

    private static async Task WithFixtureTrigger(BackOfficeDbContext db, bool period,
        Func<Task> action)
    {
        if (period)
            await db.Database.ExecuteSqlRawAsync(
                "DISABLE TRIGGER TR_AccountingPeriod_FinanceClose ON AccountingPeriod");
        else
            await db.Database.ExecuteSqlRawAsync(
                "DISABLE TRIGGER TR_FinancePosting_CorrectionSource ON FinancePosting");
        try { await action(); }
        finally
        {
            if (period)
                await db.Database.ExecuteSqlRawAsync(
                    "ENABLE TRIGGER TR_AccountingPeriod_FinanceClose ON AccountingPeriod");
            else
                await db.Database.ExecuteSqlRawAsync(
                    "ENABLE TRIGGER TR_FinancePosting_CorrectionSource ON FinancePosting");
        }
        var enabled = period
            ? await db.Database.SqlQueryRaw<bool>(
                "SELECT CONVERT(bit, CASE WHEN is_disabled=0 THEN 1 ELSE 0 END) AS Value FROM sys.triggers WHERE name=N'TR_AccountingPeriod_FinanceClose'").SingleAsync()
            : await db.Database.SqlQueryRaw<bool>(
                "SELECT CONVERT(bit, CASE WHEN is_disabled=0 THEN 1 ELSE 0 END) AS Value FROM sys.triggers WHERE name=N'TR_FinancePosting_CorrectionSource'").SingleAsync();
        Assert.True(enabled);
    }
}
