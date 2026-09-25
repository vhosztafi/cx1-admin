using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlFinanceEarningBackfillPinsPostedPremiumAndRejectsChangedSchedule()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var fixture = setup.Source;
            Assert.Empty(await db.Set<FinanceEarningSlice>().AsNoTracking().ToArrayAsync());
            await new QuoteIssueService(fixture.Factory, fixture.Clock).IssueAsync(fixture.Underwriter,
                fixture.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var component = await db.Set<IssueFinancialComponent>().AsNoTracking()
                .SingleAsync(x => x.Code == "premium");
            var obligation = await db.Set<IssueFinancialObligation>().AsNoTracking()
                .SingleAsync(x => x.Id == component.ObligationId);
            var journal = await db.Set<Journal>().AsNoTracking()
                .SingleAsync(x => x.ObligationId == obligation.Id);
            Assert.NotNull(journal.PostedAt);
            var actor = await FinanceLedgerActor(db);
            var service = new FinanceEarningService(fixture.Factory);
            var immediate = await db.Set<FinanceEarningSlice>().AsNoTracking()
                .Where(x => x.SourceComponentId == component.Id).ToArrayAsync();
            Assert.NotEmpty(immediate);
            Assert.Equal(FinanceLedgerMath.Pence(component.Amount), immediate.Sum(x => x.EarnedPence));
            Assert.Equal(0, await service.BackfillAgencyAsync(actor, obligation.AgencyId));
            var period = await db.Set<AccountingPeriod>().AsNoTracking().SingleAsync(x =>
                x.StartsOn <= DateOnly.FromDateTime(component.CoverageStartsAt.UtcDateTime) &&
                x.EndsOn > DateOnly.FromDateTime(component.CoverageStartsAt.UtcDateTime));
            var read = new FinanceEarningService(fixture.Factory,
                new StatementClock(journal.PostedAt!.Value.AddSeconds(1)));
            var review = await read.ReviewAsync(actor, obligation.AgencyId, period.Id);
            Assert.Equal("GBP", review.Currency);
            Assert.Equal("posted-premium-component/monthly-utc-overlap-v1", review.Basis);
            Assert.Equal(journal.PostedAt.Value.AddSeconds(1), review.AsOf);
            Assert.True(review.ComponentCount > 0);
            Assert.Equal(review.EarnedPremium,
                FinanceLedgerMath.Money(review.Items.Sum(x => decimal.Parse(x.PeriodEarnedPremium,
                    System.Globalization.CultureInfo.InvariantCulture))));
            Assert.All(review.Items, x => Assert.Equal(component.Id, x.ComponentId));
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                read.ReviewAsync(actor, Guid.NewGuid(), period.Id))).Status);
            // This isolated database simulates a legacy posted source before the additive migration.
            await db.Set<FinanceEarningSlice>().Where(x => x.SourceComponentId == component.Id)
                .ExecuteDeleteAsync();
            var created = await service.BackfillAgencyAsync(actor, obligation.AgencyId);
            Assert.True(created > 0);
            db.ChangeTracker.Clear();
            var slices = await db.Set<FinanceEarningSlice>().AsNoTracking()
                .Where(x => x.SourceComponentId == component.Id).OrderBy(x => x.MonthStart).ToArrayAsync();
            Assert.Equal(created, slices.Length);
            Assert.Equal(FinanceLedgerMath.Pence(component.Amount), slices.Sum(x => x.EarnedPence));
            Assert.All(slices, slice =>
            {
                Assert.Equal(obligation.AgencyId, slice.AgencyId);
                Assert.Equal(obligation.PolicyId, slice.PolicyId);
                Assert.Equal(journal.PostedAt, slice.SourcePostedAt);
                Assert.Equal(1, slice.MonthStart.Day);
                Assert.Equal(FinanceEarningMath.SourceHash(component, journal.PostedAt!.Value), slice.SourceHash);
            });
            Assert.Equal(0, await service.BackfillAgencyAsync(actor, obligation.AgencyId));
            Assert.Equal(created, await db.Set<FinanceEarningSlice>().CountAsync());
            Assert.Equal(0, await db.Set<FinanceEarningSlice>().CountAsync(x =>
                x.SourceComponentId != component.Id));
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE FinanceEarningSlice SET SourceHash=0x0000000000000000000000000000000000000000000000000000000000000000 WHERE Id={slices[0].Id}");
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                read.ReviewAsync(actor, obligation.AgencyId, period.Id))).Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.BackfillAgencyAsync(actor, obligation.AgencyId));
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={actor.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                read.ReviewAsync(actor, obligation.AgencyId, period.Id))).Status);
        });
    }
}
