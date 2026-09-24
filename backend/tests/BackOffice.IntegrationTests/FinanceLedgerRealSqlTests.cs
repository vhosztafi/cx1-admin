using System.Globalization;
using BackOffice.Application;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task<ActorContext> FinanceLedgerActor(BackOfficeDbContext db)
    {
        var user = await db.Set<StaffUser>().AsNoTracking()
            .SingleAsync(x => x.Email == "finance@cover.example");
        var roles = await (from link in db.Set<UserRole>().AsNoTracking()
            join role in db.Set<Role>().AsNoTracking() on link.RoleId equals role.Id
            where link.UserId == user.Id select role.Code).ToArrayAsync();
        return new ActorContext(user.Id, user.TeamId, user.AgencyId,
            roles.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RealSqlFinanceLedgerNewFirstIssuePinsHeldOpenPeriodBeforeJournalSeal()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            var issuedAt = f.Clock.GetUtcNow();
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(
                f.Underwriter, f.QuoteId, setup.Version, setup.Input,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());

            db.ChangeTracker.Clear();
            var journal = await db.Set<Journal>().AsNoTracking().SingleAsync();
            var obligation = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync();
            var period = await db.Set<AccountingPeriod>().AsNoTracking()
                .SingleAsync(x => x.Id == journal.AccountingPeriodId);
            var londonDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
                issuedAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
            Assert.Equal("first-issue", journal.Purpose);
            Assert.NotNull(journal.PostedAt);
            Assert.Equal("open", period.State);
            Assert.Equal(londonDate < period.StartsOn ? period.StartsOn : londonDate, journal.PostingDate);
            Assert.True(period.StartsOn <= journal.PostingDate && journal.PostingDate < period.EndsOn);
            Assert.Equal(obligation.TransactionId, journal.TransactionId);
            Assert.Equal(0m, await db.Set<JournalLine>().Where(x => x.JournalId == journal.Id)
                .SumAsync(x => x.Debit - x.Credit));
            Assert.Equal(obligation.InvoiceDue, await db.Set<JournalLine>()
                .Where(x => x.JournalId == journal.Id &&
                    (x.AccountCode == "agency-receivable" || x.AccountCode == "relationship-receivable"))
                .SumAsync(x => x.Debit - x.Credit));
            var ledger = new FinanceLedgerService(f.Factory);
            var actor = await FinanceLedgerActor(db);
            var page = await ledger.ListAsync(actor, obligation.AgencyId);
            var movement = Assert.Single(page.Items);
            Assert.Equal("insurance", movement.SourceKind);
            Assert.Equal(journal.PostingDate, movement.PostingDate);
            Assert.Equal(obligation.InvoiceDue.ToString("0.00", CultureInfo.InvariantCulture), movement.DebtorDelta);
            Assert.Equal(1, page.Total);
            var transaction = await ledger.TransactionAsync(actor, obligation.TransactionId);
            Assert.Equal(journal.Id, transaction.JournalId);
            Assert.Equal(journal.AccountingPeriodId, transaction.AccountingPeriodId);
            Assert.Equal(obligation.AgencyTermsVersionId, transaction.AgencyTermsVersionId);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                ledger.ListAsync(f.Underwriter, obligation.AgencyId))).Status);
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [Fact]
    public async Task RealSqlFinanceLedgerLegacyFirstIssueRetainsNullPeriodAndOriginalPostingInstant()
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await StoredPolicy(db, password);
            var posting = await StoredIssuePosting(db, f, false);
            var postedAt = f.Source.Clock.GetUtcNow();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Journal SET PostedAt={postedAt} WHERE Id={posting.Journal.Id}");
            db.ChangeTracker.Clear();

            var legacy = await db.Set<Journal>().AsNoTracking()
                .SingleAsync(x => x.Id == posting.Journal.Id);
            Assert.Equal("first-issue", legacy.Purpose);
            Assert.Null(legacy.AccountingPeriodId);
            Assert.Null(legacy.PostingDate);
            Assert.Equal(postedAt, legacy.PostedAt);
            Assert.Equal(posting.Obligation.InvoiceDue, await db.Set<JournalLine>()
                .Where(x => x.JournalId == legacy.Id &&
                    (x.AccountCode == "agency-receivable" || x.AccountCode == "relationship-receivable"))
                .SumAsync(x => x.Debit - x.Credit));
            var ledger = new FinanceLedgerService(f.Source.Factory);
            var actor = await FinanceLedgerActor(db);
            var page = await ledger.ListAsync(actor, posting.Obligation.AgencyId);
            var movement = Assert.Single(page.Items);
            Assert.Equal("insurance", movement.SourceKind);
            Assert.Equal(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(postedAt,
                TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime), movement.PostingDate);
            Assert.Null(movement.AccountingPeriodId);
            Assert.Equal(posting.Obligation.InvoiceDue.ToString("0.00", CultureInfo.InvariantCulture), movement.DebtorDelta);
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [Fact]
    public async Task RealSqlFinanceLedgerClosedPeriodsBlockNewIssueWithoutLeavingPostingOrReceipt()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            var key = Guid.NewGuid().ToString("N");
            await db.Database.ExecuteSqlRawAsync("UPDATE AccountingPeriod SET State=N'closed'");

            var issue = new QuoteIssueService(f.Factory, f.Clock);
            var error = await Assert.ThrowsAsync<QuoteOperationException>(() => issue.IssueAsync(
                f.Underwriter, f.QuoteId, setup.Version, setup.Input, key, Guid.NewGuid()));
            Assert.Equal("accounting-period-unavailable", error.Code);
            db.ChangeTracker.Clear();
            Assert.Empty(await db.Set<Policy>().AsNoTracking().ToArrayAsync());
            Assert.Empty(await db.Set<Journal>().AsNoTracking().ToArrayAsync());
            Assert.False(await db.Set<IdempotencyRecord>().AnyAsync(x => x.Key == key));

            await db.Database.ExecuteSqlRawAsync("UPDATE AccountingPeriod SET State=N'open'");
            await issue.IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input,
                key, Guid.NewGuid());
            db.ChangeTracker.Clear();
            Assert.Single(await db.Set<Policy>().AsNoTracking().ToArrayAsync());
            Assert.NotNull((await db.Set<Journal>().AsNoTracking().SingleAsync()).AccountingPeriodId);
        });
    }

    [Fact]
    public async Task RealSqlFinanceLedgerConcurrentPeriodCloseWinsBeforeIssuePosting()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            var key = Guid.NewGuid().ToString("N");
            await using var closing = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE AccountingPeriod SET State=N'closed'");

            var issueTask = new QuoteIssueService(f.Factory, f.Clock).IssueAsync(
                f.Underwriter, f.QuoteId, setup.Version, setup.Input, key, Guid.NewGuid());
            await Task.Delay(500);
            Assert.False(issueTask.IsCompleted);
            await closing.CommitAsync();

            var error = await Assert.ThrowsAsync<QuoteOperationException>(() => issueTask);
            Assert.Equal("accounting-period-unavailable", error.Code);
            db.ChangeTracker.Clear();
            Assert.Empty(await db.Set<Policy>().AsNoTracking().ToArrayAsync());
            Assert.Empty(await db.Set<Journal>().AsNoTracking().ToArrayAsync());
            Assert.False(await db.Set<IdempotencyRecord>().AnyAsync(x => x.Key == key));
        });
    }

    [Fact]
    public async Task RealSqlFinanceLedgerCashPostingRejectsForeignPolicyAndClosedPeriod()
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await StoredPolicy(db, password);
            var period = await db.Set<AccountingPeriod>().AsNoTracking().OrderBy(x => x.StartsOn).FirstAsync();
            var otherAgencyId = await db.Set<Agency>().AsNoTracking()
                .Where(x => x.Id != f.Policy.AgencyId).Select(x => x.Id).FirstAsync();
            var now = f.Source.Clock.GetUtcNow();

            FinancePosting Posting(Guid agencyId) => new()
            {
                SourceKind = "correction", SourceId = Guid.NewGuid(), AgencyId = agencyId,
                RelationshipId = f.Policy.RelationshipId, PolicyId = f.Policy.Id,
                DebtorKind = "agency", AccountingPeriodId = period.Id,
                PostingDate = period.StartsOn, EffectiveAt = now, PostedAt = now,
                DebtorDelta = 1m, ProviderDelta = 1m,
                Reason = "Fictional guarded finance correction", CreatedAt = now,
                CreatedBy = f.Source.Underwriter.UserId
            };

            db.Add(Posting(otherAgencyId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            Assert.False(await db.Set<FinancePosting>().AnyAsync());

            await using (var valid = await db.Database.BeginTransactionAsync())
            {
                var saved = Posting(f.Policy.AgencyId);
                db.Add(saved);
                await db.SaveChangesAsync();
                Assert.Single(await db.Set<FinancePosting>().AsNoTracking().ToArrayAsync());
                await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE FinancePosting SET DebtorDelta=2 WHERE Id={saved.Id}"));
                await valid.RollbackAsync();
            }
            db.ChangeTracker.Clear();
            Assert.False(await db.Set<FinancePosting>().AnyAsync());

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE AccountingPeriod SET State=N'closed' WHERE Id={period.Id}");
            db.Add(Posting(f.Policy.AgencyId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            Assert.False(await db.Set<FinancePosting>().AnyAsync());
        });
    }
}
