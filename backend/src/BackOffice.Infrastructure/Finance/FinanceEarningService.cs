using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Finance;

public sealed record MonthlyEarning(DateOnly MonthStart, long EarnedPence);
public sealed record EarningBackfillReport(int InspectedComponents, int PreexistingSlices, int InsertedSlices);
public sealed record EarnedPremiumMonth(Guid ComponentId, Guid ObligationId, Guid TransactionId,
    Guid PolicyId, DateOnly MonthStart, string SavedMonthPremium, string PeriodEarnedPremium,
    DateTimeOffset SourcePostedAt);
public sealed record EarnedPremiumReview(Guid AgencyId, Guid PeriodId, DateOnly From, DateOnly To,
    string PeriodState, DateTimeOffset AsOf, string Basis, string Currency,
    string EarnedPremium, int ComponentCount, IReadOnlyList<EarnedPremiumMonth> Items);

public static class FinanceEarningMath
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public static IReadOnlyList<MonthlyEarning> Allocate(long premiumPence,
        DateTimeOffset coverageStartsAt, DateTimeOffset coverageEndsAt)
    {
        if (coverageStartsAt.Offset != TimeSpan.Zero || coverageEndsAt.Offset != TimeSpan.Zero ||
            coverageStartsAt >= coverageEndsAt)
            throw new InvalidOperationException("A posted premium needs a positive UTC coverage interval.");
        var totalTicks = checked(coverageEndsAt.UtcDateTime.Ticks - coverageStartsAt.UtcDateTime.Ticks);
        var local = TimeZoneInfo.ConvertTime(coverageStartsAt, London);
        var month = new DateOnly(local.Year, local.Month, 1);
        var slices = new List<MonthlyEarning>();
        long allocated = 0;
        while (true)
        {
            if (slices.Count >= 1200) throw new InvalidOperationException("Coverage exceeds 100 calendar years.");
            var next = month.AddMonths(1);
            var boundary = new DateTime(next.Year, next.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var boundaryUtc = TimeZoneInfo.ConvertTimeToUtc(boundary, London);
            var end = boundaryUtc < coverageEndsAt.UtcDateTime ? boundaryUtc : coverageEndsAt.UtcDateTime;
            var begin = slices.Count == 0 ? coverageStartsAt.UtcDateTime :
                TimeZoneInfo.ConvertTimeToUtc(new DateTime(month.Year, month.Month, 1, 0, 0, 0,
                    DateTimeKind.Unspecified), London);
            var overlap = checked(end.Ticks - begin.Ticks);
            if (overlap <= 0) throw new InvalidOperationException("Invalid calendar-month overlap.");
            var amount = end == coverageEndsAt.UtcDateTime
                ? checked(premiumPence - allocated)
                : checked((long)(checked((Int128)premiumPence * overlap) / totalTicks));
            allocated = checked(allocated + amount);
            slices.Add(new MonthlyEarning(month, amount));
            if (end == coverageEndsAt.UtcDateTime) break;
            month = next;
        }
        if (allocated != premiumPence) throw new InvalidOperationException("Earning schedule lost a penny.");
        return slices;
    }

    public static byte[] SourceHash(IssueFinancialComponent component, DateTimeOffset postedAt)
    {
        var pence = FinanceLedgerMath.Pence(component.Amount);
        var source = string.Join('|', "earning-v1", component.Id.ToString("N"),
            component.ObligationId.ToString("N"), component.TransactionId.ToString("N"),
            pence.ToString(CultureInfo.InvariantCulture),
            component.CoverageStartsAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            component.CoverageEndsAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
            postedAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture));
        return SHA256.HashData(Encoding.UTF8.GetBytes(source));
    }
}

public sealed class FinanceEarningService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider? clock = null)
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private static DateTimeOffset LondonMidnight(DateOnly date) => new(TimeZoneInfo.ConvertTimeToUtc(
        new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified), London), TimeSpan.Zero);
    private static string Money(long pence) => FinanceLedgerMath.Money(pence / 100m);

    public async Task<EarnedPremiumReview> ReviewAsync(ActorContext actor, Guid agencyId,
        Guid periodId, CancellationToken token = default)
    {
        if (agencyId == Guid.Empty || periodId == Guid.Empty)
            throw new QuoteOperationException(400, "finance-earning-query-invalid");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await FinanceLedgerService.Authorize(db, actor, agencyId, null, token);
        var period = await db.Set<AccountingPeriod>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodId, token)
            ?? throw new QuoteOperationException(404, "finance-period-not-found");
        var cutoff = period.State == "closed"
            ? period.SourceCutoff ?? throw new QuoteOperationException(409, "finance-earning-cutoff-missing")
            : (clock ?? TimeProvider.System).GetUtcNow();
        var sources = await (from component in db.Set<IssueFinancialComponent>().AsNoTracking()
            join obligation in db.Set<IssueFinancialObligation>().AsNoTracking()
                on component.ObligationId equals obligation.Id
            join journal in db.Set<Journal>().AsNoTracking()
                on obligation.Id equals journal.ObligationId
            where obligation.AgencyId == agencyId && component.TransactionId == obligation.TransactionId &&
                journal.TransactionId == component.TransactionId && component.Code == "premium" &&
                journal.PostedAt != null && journal.PostedAt <= cutoff
            select new { Component = component, Obligation = obligation, PostedAt = journal.PostedAt!.Value })
            .ToArrayAsync(token);
        var ids = sources.Select(x => x.Component.Id).ToArray();
        var saved = await db.Set<FinanceEarningSlice>().AsNoTracking()
            .Where(x => x.AgencyId == agencyId && ids.Contains(x.SourceComponentId))
            .OrderBy(x => x.MonthStart).ToArrayAsync(token);
        var bySource = saved.GroupBy(x => x.SourceComponentId).ToDictionary(x => x.Key, x => x.ToArray());
        var periodStart = LondonMidnight(period.StartsOn);
        var periodEnd = LondonMidnight(period.EndsOn);
        var items = new List<EarnedPremiumMonth>();
        var amount = 0L;
        foreach (var source in sources)
        {
            var component = source.Component;
            var expected = FinanceEarningMath.Allocate(FinanceLedgerMath.Pence(component.Amount),
                component.CoverageStartsAt, component.CoverageEndsAt);
            var hash = FinanceEarningMath.SourceHash(component, source.PostedAt);
            if (!bySource.TryGetValue(component.Id, out var slices) || slices.Length != expected.Count ||
                slices.Where((slice, index) => slice.MonthStart != expected[index].MonthStart ||
                    slice.EarnedPence != expected[index].EarnedPence ||
                    slice.PremiumPence != FinanceLedgerMath.Pence(component.Amount) ||
                    slice.ObligationId != source.Obligation.Id || slice.TransactionId != component.TransactionId ||
                    slice.AgencyId != agencyId || slice.PolicyId != source.Obligation.PolicyId ||
                    slice.CoverageStartsAt != component.CoverageStartsAt ||
                    slice.CoverageEndsAt != component.CoverageEndsAt || slice.SourcePostedAt != source.PostedAt ||
                    slice.AlgorithmVersion != 1 || !slice.SourceHash.SequenceEqual(hash)).Any())
                throw new QuoteOperationException(409, "finance-earning-incomplete");
            foreach (var slice in slices)
            {
                var monthStart = LondonMidnight(slice.MonthStart);
                var monthEnd = LondonMidnight(slice.MonthStart.AddMonths(1));
                var sourceStart = component.CoverageStartsAt > monthStart ? component.CoverageStartsAt : monthStart;
                var sourceEnd = component.CoverageEndsAt < monthEnd ? component.CoverageEndsAt : monthEnd;
                var clipStart = sourceStart > periodStart ? sourceStart : periodStart;
                var clipEnd = sourceEnd < periodEnd ? sourceEnd : periodEnd;
                if (clipStart >= clipEnd) continue;
                var periodPence = clipStart == sourceStart && clipEnd == sourceEnd
                    ? slice.EarnedPence
                    : checked((long)(checked((Int128)slice.EarnedPence *
                        (clipEnd.UtcDateTime.Ticks - clipStart.UtcDateTime.Ticks)) /
                        (sourceEnd.UtcDateTime.Ticks - sourceStart.UtcDateTime.Ticks)));
                amount = checked(amount + periodPence);
                items.Add(new EarnedPremiumMonth(component.Id, source.Obligation.Id, component.TransactionId,
                    source.Obligation.PolicyId, slice.MonthStart, Money(slice.EarnedPence), Money(periodPence),
                    source.PostedAt));
            }
        }
        await tx.CommitAsync(token);
        return new EarnedPremiumReview(agencyId, period.Id, period.StartsOn, period.EndsOn,
            period.State, cutoff, "posted-premium-component/monthly-utc-overlap-v1", "GBP",
            Money(FinanceLedgerMath.SumPence([amount])), items.Select(x => x.ComponentId).Distinct().Count(),
            items.OrderBy(x => x.MonthStart).ThenBy(x => x.ComponentId).ToArray());
    }

    public static async Task<EarningBackfillReport> BackfillDemoAsync(string connection,
        CancellationToken token = default)
    {
        DemoDatabase.ValidateDemoTarget(connection);
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>()
            .UseSqlServer(connection, sql => sql.UseCompatibilityLevel(160)).Options;
        await using var db = new BackOfficeDbContext(options);
        await db.Database.MigrateAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var obligationIds = await (from journal in db.Set<Journal>().AsNoTracking()
            join component in db.Set<IssueFinancialComponent>().AsNoTracking()
                on journal.ObligationId equals component.ObligationId
            where journal.PostedAt != null && component.Code == "premium" &&
                journal.TransactionId == component.TransactionId
            select component.ObligationId).Distinct().ToArrayAsync(token);
        var inspected = await (from journal in db.Set<Journal>().AsNoTracking()
            join component in db.Set<IssueFinancialComponent>().AsNoTracking()
                on journal.ObligationId equals component.ObligationId
            where journal.PostedAt != null && component.Code == "premium" &&
                journal.TransactionId == component.TransactionId
            select component.Id).Distinct().CountAsync(token);
        var preexisting = await db.Set<FinanceEarningSlice>().CountAsync(token);
        var inserted = 0;
        foreach (var obligationId in obligationIds)
            inserted += await MaterializeObligationAsync(db, obligationId, token);
        await tx.CommitAsync(token);
        return new EarningBackfillReport(inspected, preexisting, inserted);
    }

    public async Task<int> BackfillAgencyAsync(ActorContext actor, Guid agencyId,
        CancellationToken token = default)
    {
        if (agencyId == Guid.Empty) throw new InvalidOperationException("Agency is required.");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await FinanceLedgerService.Authorize(db, actor, agencyId, null, token, true);
        var obligationIds = await (from journal in db.Set<Journal>().AsNoTracking()
            join obligation in db.Set<IssueFinancialObligation>().AsNoTracking()
                on journal.ObligationId equals obligation.Id
            where obligation.AgencyId == agencyId && journal.PostedAt != null
            select obligation.Id).Distinct().ToArrayAsync(token);
        var added = 0;
        foreach (var obligationId in obligationIds)
            added += await MaterializeObligationAsync(db, obligationId, token);
        await tx.CommitAsync(token);
        return added;
    }

    // Called inside the insurance posting transaction after Journal.PostedAt is saved.
    public static async Task<int> MaterializeObligationAsync(BackOfficeDbContext db, Guid obligationId,
        CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Earning materialization requires the posting transaction.");
        var obligation = await db.Set<IssueFinancialObligation>().AsNoTracking()
            .SingleAsync(x => x.Id == obligationId, token);
        var journal = await db.Set<Journal>().AsNoTracking()
            .SingleAsync(x => x.ObligationId == obligationId && x.TransactionId == obligation.TransactionId, token);
        if (journal.PostedAt is not DateTimeOffset postedAt) return 0;
        var components = await db.Set<IssueFinancialComponent>().AsNoTracking()
            .Where(x => x.ObligationId == obligationId && x.TransactionId == obligation.TransactionId && x.Code == "premium")
            .ToArrayAsync(token);
        var added = 0;
        foreach (var component in components)
        {
            var premium = FinanceLedgerMath.Pence(component.Amount);
            var schedule = FinanceEarningMath.Allocate(premium, component.CoverageStartsAt, component.CoverageEndsAt);
            var hash = FinanceEarningMath.SourceHash(component, postedAt);
            var existing = await db.Set<FinanceEarningSlice>().AsNoTracking()
                .Where(x => x.SourceComponentId == component.Id).OrderBy(x => x.MonthStart).ToArrayAsync(token);
            if (existing.Length > 0)
            {
                if (existing.Length != schedule.Count || existing.Where((slice, index) =>
                    slice.MonthStart != schedule[index].MonthStart || slice.EarnedPence != schedule[index].EarnedPence ||
                    slice.PremiumPence != premium || slice.AgencyId != obligation.AgencyId ||
                    slice.PolicyId != obligation.PolicyId || slice.ObligationId != obligation.Id ||
                    slice.TransactionId != obligation.TransactionId || slice.CoverageStartsAt != component.CoverageStartsAt ||
                    slice.CoverageEndsAt != component.CoverageEndsAt || slice.SourcePostedAt != postedAt ||
                    slice.AlgorithmVersion != 1 || !slice.SourceHash.SequenceEqual(hash)).Any())
                    throw new InvalidOperationException("A pinned premium earning source changed.");
                continue;
            }
            foreach (var month in schedule)
                db.Set<FinanceEarningSlice>().Add(new FinanceEarningSlice
                {
                    SourceComponentId = component.Id, ObligationId = obligation.Id,
                    TransactionId = obligation.TransactionId, AgencyId = obligation.AgencyId,
                    PolicyId = obligation.PolicyId, PremiumPence = premium,
                    CoverageStartsAt = component.CoverageStartsAt, CoverageEndsAt = component.CoverageEndsAt,
                    MonthStart = month.MonthStart, EarnedPence = month.EarnedPence,
                    AlgorithmVersion = 1, SourceHash = hash, SourcePostedAt = postedAt
                });
            added += schedule.Count;
        }
        if (added > 0) await db.SaveChangesAsync(token);
        return added;
    }
}
