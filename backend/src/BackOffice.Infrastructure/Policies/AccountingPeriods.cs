using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record HeldAccountingPeriod(Guid PeriodId, DateOnly PostingDate);

public static class AccountingPeriods
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    // The lock is retained by the caller's issue transaction through journal sealing.
    public static async Task<HeldAccountingPeriod> HoldAsync(BackOfficeDbContext db, DateTimeOffset processingAt, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Accounting selection requires a held issue transaction.");
        if (processingAt.Offset != TimeSpan.Zero) throw new ArgumentException("Processing time must be UTC.", nameof(processingAt));
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(processingAt, London).DateTime);
        var period = await db.Set<AccountingPeriod>().FromSqlInterpolated($"SELECT TOP(1) * FROM AccountingPeriod WITH(UPDLOCK,HOLDLOCK) WHERE State=N'open' AND EndsOn>{day} ORDER BY StartsOn").AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(409, "accounting-period-unavailable");
        return new(period.Id, day < period.StartsOn ? period.StartsOn : day);
    }

    public static async Task SeedAsync(BackOfficeDbContext db, DateTimeOffset processingAt, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Period seed requires a held initialization transaction.");
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'CoverMGA.AccountingPeriodSeed',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51510,'Accounting seed lock unavailable.',1;", token);
        var year = TimeZoneInfo.ConvertTime(processingAt, London).Year;
        for (var offset = 0; offset < 3; offset++)
        {
            var start = new DateOnly(year + offset, 1, 1); var end = start.AddYears(1);
            // Existing shorter or closed periods are deliberate configuration. Never replace them.
            if (!await db.Set<AccountingPeriod>().AnyAsync(x => x.StartsOn < end && start < x.EndsOn, token))
            {
                db.Add(new AccountingPeriod { StartsOn = start, EndsOn = end, CreatedAt = processingAt });
                await db.SaveChangesAsync(token);
            }
        }
    }
}
