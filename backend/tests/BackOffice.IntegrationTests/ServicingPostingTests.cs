using System.Data;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingPosting(BackOfficeDbContext db, DecisionFixture f, ServicingCycle cycle, ServicingAcceptance acceptance)
    {
        var prior = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.Id == cycle.BaseVersionId);
        var originalLines = JsonSerializer.Serialize(await db.Set<JournalLine>().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
        var policy = await db.Set<Policy>().AsNoTracking().SingleAsync(x => x.Id == cycle.PolicyId);
        var original = await db.Set<PolicyTransaction>().AsNoTracking().SingleAsync(x => x.PolicyId == policy.Id);
        // Retained initial MID work prevents an unsafe downgrade; preserve the issued graph.
        await VerifyRetainedTemplateDowngradeProtection(db, "20260918115755_ServicingAccountingPeriods", requiresTemplateGuard: false);
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        Assert.False(db.Database.HasPendingModelChanges());
        var rating = await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x => x.Id == cycle.CurrentRatingId);
        var outcome = JsonSerializer.Deserialize<ServicingRatingOutcome>(rating.ResultJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var now = f.Clock.GetUtcNow();
        var grant = await (from g in db.Set<UserAuthorityGrant>() join a in db.Set<AuthorityVersion>() on g.AuthorityVersionId equals a.Id
            where g.UserId==f.Underwriter.UserId && g.RevokedAt==null && a.ProductVersionId==cycle.ProductVersionId && a.BinderVersionId==cycle.BinderVersionId
            select g).FirstAsync();
        var decision = new ServicingIssueDecision { DraftId=cycle.DraftId,PolicyId=policy.Id,BaseTermId=cycle.BaseTermId,BaseVersionId=cycle.BaseVersionId,
            RevisionId=cycle.RevisionId,CycleId=cycle.Id,RatingId=rating.Id,TermsVersionId=acceptance.TermsVersionId,AcceptanceId=acceptance.Id,
            ActorId=f.Underwriter.UserId,GrantId=grant.Id,AuthorityVersionId=grant.AuthorityVersionId,InputHash=cycle.InputHash,
            TermsHash=acceptance.TermsHash,AssuranceHash=acceptance.AssuranceHash,EffectiveAt=outcome.Rating!.Slices[0].EffectiveAt,
            Reason="Fictional guarded signed posting",CreatedAt=now,CreatedBy=f.Underwriter.UserId };
        db.Add(decision);await db.SaveChangesAsync();
        var transaction = new PolicyTransaction { PolicyId = policy.Id, TermId = cycle.BaseTermId, SourceQuoteId = policy.SourceQuoteId,
            Kind = "adjustment", Sequence = 2, ServicingDraftId = cycle.DraftId, ServicingRevisionId = cycle.RevisionId, ServicingCycleId = cycle.Id,
            ServicingRatingId = rating.Id, ServicingAcceptanceId = acceptance.Id, ServicingIssueDecisionId=decision.Id, EffectiveAt = outcome.Rating!.Slices[0].EffectiveAt,
            ProcessedAt = now, CreatedAt = now, CreatedBy = f.Underwriter.UserId, Reason = "Fictional guarded signed posting", OperationKey = "servicing-issue/" + cycle.DraftId.ToString("N") };
        // Neither mixed quote/servicing provenance nor a nullable source can bypass source validation.
        transaction.RatingId = original.RatingId; db.Add(transaction);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear(); transaction.RatingId = null;
        transaction.ServicingAcceptanceId = null; db.Add(transaction);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear(); transaction.ServicingAcceptanceId = acceptance.Id;
        transaction.ServicingRatingId = Guid.NewGuid(); db.Add(transaction);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear(); transaction.ServicingRatingId = rating.Id;
        db.Add(transaction); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var periods = await db.Set<AccountingPeriod>().AsNoTracking().OrderBy(x => x.StartsOn).ToArrayAsync();
        await SetLegacyPeriodStateRaw(db, "UPDATE AccountingPeriod SET State=N'closed'");
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal("accounting-period-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() => ServicingPostingService.WriteAsync(db, transaction.Id))).Code);
            await tx.RollbackAsync();
        }
        Assert.False(await db.Set<IssueFinancialObligation>().AnyAsync(x => x.TransactionId == transaction.Id));
        await SetLegacyPeriodState(db, $"UPDATE AccountingPeriod SET State=N'open' WHERE Id={periods[1].Id}");
        IssueFinancialObligation obligation; IssueFinancialComponent[] components; Journal journal; JournalLine[] lines;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var receipt = await ServicingPostingService.WriteAsync(db, transaction.Id);
            Assert.Equal(periods[1].StartsOn, receipt.PostingDate);
            obligation = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync(x => x.Id == receipt.ObligationId);
            journal = await db.Set<Journal>().AsNoTracking().SingleAsync(x => x.Id == receipt.JournalId);
            components = await db.Set<IssueFinancialComponent>().AsNoTracking().Where(x => x.TransactionId == transaction.Id).ToArrayAsync();
            lines = await db.Set<JournalLine>().AsNoTracking().Where(x => x.TransactionId == transaction.Id).ToArrayAsync();
            Assert.Equal(8, components.Length); Assert.Equal(rating.Premium, obligation.Premium);
            Assert.Equal(0, lines.Sum(x => x.Debit - x.Credit));
            Assert.Equal(2, components.Where(x => x.Code == "premium").Select(x => x.CoverageStartsAt).Distinct().Count());
            if (await db.Set<Product>().Where(x => x.Id == policy.ProductId).Select(x => x.Code).SingleAsync() == "motor-trade-combined")
            {
                Assert.Contains(components, x => x.Code == "premium" && x.Amount > 0);
                Assert.Contains(components, x => x.Code == "premium" && x.Amount < 0);
                foreach (var negative in components.Where(x => x.Code == "premium" && x.Amount < 0))
                    Assert.Contains(lines, x => x.SourceComponentId == negative.Id && x.AccountCode == "insurer-payable" && x.Debit == -negative.Amount);
            }
            Assert.All(lines, x => Assert.True((x.Debit > 0 && x.Credit == 0) || (x.Credit > 0 && x.Debit == 0)));
            await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear(); journal.PostedAt = null;
        db.Add(obligation); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var component = components.First(x => x.Code == "premium"); var amount = component.Amount;
        component.Amount += 0.01m; db.Add(component); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear(); component.Amount = amount;
        var starts = component.CoverageStartsAt; component.CoverageStartsAt = starts.AddDays(1); db.Add(component);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear(); component.CoverageStartsAt = starts;
        component.OriginalComponentId = await db.Set<IssueFinancialComponent>().Where(x => x.TransactionId == original.Id && x.Code == "premium").Select(x => x.Id).SingleAsync();
        db.Add(component); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear(); component.OriginalComponentId = null;
        db.AddRange(components); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var selectedPeriod = journal.AccountingPeriodId; journal.AccountingPeriodId = periods[0].Id;
        db.Add(journal); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear(); journal.AccountingPeriodId = selectedPeriod;
        db.Add(journal); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var line = lines.First(x => x.PartyId is not null); var owner = line.PartyId;
        line.PartyId = Guid.NewGuid(); db.Add(line); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear(); line.PartyId = owner;
        (line.Debit, line.Credit) = (line.Credit, line.Debit); db.Add(line);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear(); (line.Debit, line.Credit) = (line.Credit, line.Debit);
        db.AddRange(lines.SkipLast(1)); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(51192, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Journal SET PostedAt={now} WHERE Id={journal.Id}"))).Number);
        db.Add(lines.Last()); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await SetLegacyPeriodState(db, $"UPDATE AccountingPeriod SET State=N'closed' WHERE Id={journal.AccountingPeriodId}");
        Assert.Equal(51512, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Journal SET PostedAt={now} WHERE Id={journal.Id}"))).Number);
        await SetLegacyPeriodState(db, $"UPDATE AccountingPeriod SET State=N'open' WHERE Id={journal.AccountingPeriodId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Journal SET PostedAt={now} WHERE Id={journal.Id}");
        component.Id = Guid.NewGuid(); component.Ordinal = 3; db.Add(component);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        line.Id = Guid.NewGuid(); db.Add(line); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Journal SET PostedAt=NULL WHERE Id={journal.Id}"));
        Assert.Equal(originalLines, JsonSerializer.Serialize(await db.Set<JournalLine>().AsNoTracking().Where(x => x.TransactionId == original.Id).OrderBy(x => x.Id).ToArrayAsync()));
        var retained = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.Id == prior.Id);
        Assert.Equal(prior.SnapshotJson, retained.SnapshotJson); Assert.Equal(prior.ContentHash, retained.ContentHash);
    }

    [Fact]
    public async Task RealSqlServicingPostingTests_PeriodFallbackMissingAndImmutableBoundaries()
    {
        await WithDatabase(async (db, _) =>
        {
            var periods = await db.Set<AccountingPeriod>().OrderBy(x => x.StartsOn).ToArrayAsync();
            Assert.Equal(3, periods.Length);
            var now = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
            await Assert.ThrowsAsync<InvalidOperationException>(() => AccountingPeriods.HoldAsync(db, now));
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                var selected = await AccountingPeriods.HoldAsync(db, now);
                Assert.Equal(periods[0].Id, selected.PeriodId);
                Assert.Equal(new DateOnly(2026, 9, 18), selected.PostingDate);
                await tx.CommitAsync();
            }
            await SetLegacyPeriodState(db, $"UPDATE AccountingPeriod SET State=N'closed' WHERE Id={periods[0].Id}");
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                var selected = await AccountingPeriods.HoldAsync(db, now);
                Assert.Equal(periods[1].Id, selected.PeriodId);
                Assert.Equal(new DateOnly(2027, 1, 1), selected.PostingDate);
                // An idempotent missing-only seed cannot reopen a closed period.
                await AccountingPeriods.SeedAsync(db, now);
                await tx.CommitAsync();
            }
            Assert.Equal("closed", await db.Set<AccountingPeriod>().AsNoTracking().Where(x => x.Id == periods[0].Id).Select(x => x.State).SingleAsync());
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AccountingPeriod SET StartsOn={new DateOnly(2025,1,1)} WHERE Id={periods[0].Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE AccountingPeriod WHERE Id={periods[0].Id}"));
            await SetLegacyPeriodStateRaw(db, "UPDATE AccountingPeriod SET State=N'closed'");
            await using var missing = await db.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<QuoteOperationException>(() => AccountingPeriods.HoldAsync(db, now));
            Assert.Equal("accounting-period-unavailable", error.Code);
        });
    }

    [Fact]
    public async Task RealSqlServicingPostingTests_PeriodOverlapAndCloseContention()
    {
        await WithDatabase(async (db, _) =>
        {
            var now = new DateTimeOffset(2026,9,18,12,0,0,TimeSpan.Zero);
            db.Add(new AccountingPeriod { StartsOn = new(2026,12,1), EndsOn = new(2027,2,1) });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var held = await AccountingPeriods.HoldAsync(db, now);
            await using var other = new SqlConnection(db.Database.GetConnectionString());
            await other.OpenAsync();
            await using var command = other.CreateCommand();
            command.CommandText = "SET LOCK_TIMEOUT 300; UPDATE AccountingPeriod SET CloseReason=CloseReason WHERE Id=@id";
            command.Parameters.AddWithValue("@id", held.PeriodId);
            Assert.Equal(1222, (await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync())).Number);
            await tx.CommitAsync();
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
            await SetLegacyPeriodState(db, $"UPDATE AccountingPeriod SET State=N'closed' WHERE Id={held.PeriodId}");
            await using var next = await db.Database.BeginTransactionAsync();
            Assert.Equal(new DateOnly(2027,1,1), (await AccountingPeriods.HoldAsync(db, now)).PostingDate);
        });
    }
}
