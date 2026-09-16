using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private sealed class RatingFactory(DbContextOptions<BackOfficeDbContext> options) : IDbContextFactory<BackOfficeDbContext>
    { public BackOfficeDbContext CreateDbContext() => new(options); }
    private sealed class RatingClock : TimeProvider { public DateTimeOffset Current { get; set; } = Now; public override DateTimeOffset GetUtcNow() => Current; }

    [Theory]
    [InlineData("success", false, false)]
    [InlineData("fail-once", false, false)]
    [InlineData("timeout-after-success", false, false)]
    [InlineData("success", true, false)]
    [InlineData("success", false, true)]
    [InlineData("reject", false, false)]
    [InlineData("recover-budget", false, false)]
    [InlineData("revoke-requester", false, false)]
    [InlineData("capture-race", false, false)]
    public async Task RealSqlQuoteRatingClosesCaptureAtomicallyRequiresProvenanceAndReauthorizesReplay(string scenario, bool expireLease, bool retireBeforeApply)
    {
        await WithDatabase(async (db, password) =>
        {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true);
            await using (var tx = await db.Database.BeginTransactionAsync()) { await QuoteLookupDemoSeed.SeedAsync(db); await tx.CommitAsync(); }
            var f = await Fixture(db);
            if (scenario is "fail-once" or "timeout-after-success" or "reject")
            {
                var runtime = await db.Set<SettingVersion>().SingleAsync(x => x.Scope == "underwriting-runtime");
                var configuration = JsonNode.Parse(runtime.Values)!;
                configuration["scenarioVersionId"] = await db.Set<SettingVersion>().Where(x => x.Scope == "quote-rating/" + scenario).Select(x => x.Id).SingleAsync();
                db.Add(new SettingVersion { Scope = runtime.Scope, Version = 2, EffectiveFrom = Now, Values = configuration.ToJsonString() }); await db.SaveChangesAsync();
            }
            var factory = new RatingFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options);
            var clock = new RatingClock(); var quotes = new QuoteService(factory, clock); var rating = new QuoteRatingService(factory, clock);
            var relationship = await db.Set<Quote>().Where(x => x.Id == f.Quote).Select(x => x.RelationshipId).SingleAsync();
            using var stream = typeof(UnderwritingSeed).Assembly.GetManifestResourceStream("UnderwritingDemo.Definitions")!;
            using var examples = JsonDocument.Parse(stream); var proposal = examples.RootElement.GetProperty("proposals").GetProperty("motor-trade-road-risks");
            var created = await quotes.CreateAsync(f.Actor, relationship, f.ProductVersion, proposal.GetRawText(), Guid.NewGuid().ToString(), Guid.NewGuid());
            var saved = await quotes.GetAsync(f.Actor, created.ResourceId);
            var unavailable = await Assert.ThrowsAsync<QuoteValidationException>(() => rating.RateAsync(f.Actor, created.ResourceId, saved.Revision.Id, saved.Quote.RowVersion,
                "Fictional rate request", Guid.NewGuid().ToString(), Guid.NewGuid()));
            Assert.Contains(unavailable.Issues, x => x.Code == "vehicle-capture-context-required");
            Assert.Empty(await db.Set<UnderwritingCycle>().ToArrayAsync());
            var lookups = new QuoteLookupService(factory, clock); var worker = new QuoteLookupWorker(factory, clock); var leases = new SqlJobLeases(factory, clock);
            foreach (var vehicle in proposal.GetProperty("risk").GetProperty("vehicles").EnumerateArray())
            {
                var requested = await lookups.RequestAsync(f.Actor, created.ResourceId, saved.Quote.RowVersion, saved.Revision.Id,
                    new("vehicle", "vehicle", vehicle.GetProperty("id").GetGuid()), "no-match", Guid.NewGuid().ToString(), Guid.NewGuid());
                var lookup = await lookups.GetAsync(f.Actor, created.ResourceId, requested.ResourceId);
                var lease = (await leases.ClaimWorkAsync(QuoteLookupService.WorkKind, lookup.WorkId))!;
                Assert.True(await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease)));
                await lookups.SelectAsync(f.Actor, created.ResourceId, lookup.Id, saved.Quote.RowVersion, saved.Revision.Id, lookup.InputFingerprint, null,
                    "Fictional details checked manually", Guid.NewGuid().ToString(), Guid.NewGuid());
                saved = await quotes.GetAsync(f.Actor, created.ResourceId);
            }
            var key = Guid.NewGuid().ToString(); var before = saved;
            CommandOutcome result;
            if (scenario == "capture-race")
            {
                var changed = JsonNode.Parse(before.Revision.ProposalJson)!; changed["insured"]!["tradingName"] = "Concurrent fictional amendment";
                async Task<(CommandOutcome? Result, Exception? Error)> Race(Func<Task<CommandOutcome>> action)
                {
                    try { return (await action(), null); }
                    catch (Exception error) when (error is QuoteOperationException or QuoteInputException) { return (null, error); }
                }
                var contenders = await Task.WhenAll(
                    Race(() => rating.RateAsync(f.Actor, created.ResourceId, before.Revision.Id, before.Quote.RowVersion, "Fictional rate request", key, Guid.NewGuid())),
                    Race(() => quotes.SaveAsync(f.Actor, created.ResourceId, before.Quote.RowVersion, changed.ToJsonString(), "Concurrent change", Guid.NewGuid().ToString(), Guid.NewGuid())));
                Assert.Single(contenders, x => x.Result is not null); Assert.Single(contenders, x => x.Error is not null);
                if (contenders[0].Result is { } won) result = won;
                else
                {
                    Assert.Equal(412, Assert.IsType<QuoteOperationException>(contenders[0].Error).Status);
                    before = await quotes.GetAsync(f.Actor, created.ResourceId);
                    result = await rating.RateAsync(f.Actor, created.ResourceId, before.Revision.Id, before.Quote.RowVersion, "Fictional rate request", key, Guid.NewGuid());
                }
            }
            else result = await rating.RateAsync(f.Actor, created.ResourceId, before.Revision.Id, before.Quote.RowVersion, "Fictional rate request", key, Guid.NewGuid());
            Assert.Equal(202, result.Status);
            var closed = await quotes.GetAsync(f.Actor, created.ResourceId); Assert.Equal("rating-pending", closed.Quote.State); Assert.False(closed.CanSave);
            Assert.Equal(before.Revision.Id, closed.Revision.Id); Assert.Equal(before.Revision.ProposalJson, closed.Revision.ProposalJson);
            Assert.NotEqual(before.Quote.RowVersion, closed.Quote.RowVersion);
            var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(); Assert.Equal(cycle.Id, closed.Quote.CurrentUnderwritingCycleId);
            Assert.Equal(result.ResourceId, cycle.Id);
            Assert.Equal(cycle.Id, (await db.Set<OutboxWork>().SingleAsync(x => x.Id == cycle.WorkId)).SubjectRecordId);
            Assert.True((await rating.RateAsync(f.Actor, created.ResourceId, before.Revision.Id, before.Quote.RowVersion, "Fictional rate request", key, Guid.NewGuid())).Replayed);
            await Assert.ThrowsAsync<QuoteInputException>(() => quotes.SaveAsync(f.Actor, created.ResourceId, closed.Quote.RowVersion, before.Revision.ProposalJson, null, Guid.NewGuid().ToString(), Guid.NewGuid()));
            var ratingWorker = new QuoteRatingWorker(factory, clock);
            var ratingLease = (await leases.ClaimWorkAsync(QuoteRatingService.WorkKind, cycle.WorkId))!;
            if (scenario == "recover-budget")
            {
                for (var attempt = 1; attempt <= 6; attempt++)
                {
                    Assert.True(await leases.FailAsync(ratingLease, JobFailure.ProviderUnavailable));
                    if (attempt < 6)
                    {
                        clock.Current = (await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == cycle.WorkId)).NextAttemptAt.AddSeconds(1);
                        ratingLease = await leases.ClaimWorkAsync(QuoteRatingService.WorkKind, cycle.WorkId) ?? throw new InvalidOperationException("Due rating retry was not claimed.");
                    }
                }
                var jobs = new QuoteRatingJobs(factory, clock);
                var failedWork = (await jobs.ReadAsync(f.Actor, cycle.WorkId, default)).Work; Assert.Equal("failed", failedWork.State);
                Assert.Equal("failed", (await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync()).State);
                Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => jobs.RetryAsync(f.Actor, cycle.WorkId, failedWork.RowVersion,
                    "Recover demo rating", Guid.NewGuid().ToString(), Guid.NewGuid(), default))).Status);
                var administratorRole = await db.Set<Role>().SingleAsync(x => x.Code == "system-admin");
                var link = new UserRole { UserId = f.Actor.UserId, RoleId = administratorRole.Id }; db.Add(link); await db.SaveChangesAsync();
                var recoveryActor = f.Actor with { Roles = new HashSet<string> { "servicing", "system-admin" } };
                Assert.True((await jobs.ReadAsync(recoveryActor, cycle.WorkId, default)).RetryAllowed);
                var retryKey = Guid.NewGuid().ToString();
                var retry = await jobs.RetryAsync(recoveryActor, cycle.WorkId, failedWork.RowVersion, "Recover demo rating", retryKey, Guid.NewGuid(), default);
                Assert.Equal(202, retry.Status);
                Assert.True((await jobs.RetryAsync(recoveryActor, cycle.WorkId, failedWork.RowVersion, "Recover demo rating", retryKey, Guid.NewGuid(), default)).Replayed);
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE Id={link.Id}"); db.ChangeTracker.Clear();
                ratingLease = (await leases.ClaimWorkAsync(QuoteRatingService.WorkKind, cycle.WorkId))!; Assert.Equal(7, ratingLease.Attempt);
            }
            if (scenario is "fail-once" or "timeout-after-success")
            {
                var failure = await Assert.ThrowsAsync<QuoteRatingProviderException>(() => ratingWorker.ExecuteProviderAsync(ratingLease));
                Assert.Equal(scenario == "fail-once" ? JobFailure.ProviderUnavailable : JobFailure.ProviderTimeout, failure.Failure);
                Assert.True(await leases.FailAsync(ratingLease, failure.Failure)); clock.Current = clock.Current.AddMinutes(5);
                ratingLease = (await leases.ClaimWorkAsync(QuoteRatingService.WorkKind, cycle.WorkId))!;
            }
            var outcome = await ratingWorker.ExecuteProviderAsync(ratingLease);
            if (scenario == "reject") Assert.Null(outcome.Rating);
            else { Assert.Equal(600m, outcome.Rating!.AnnualPremium); Assert.Equal(75m, outcome.Rating.BrokerCommission); }
            if (expireLease)
            {
                clock.Current = clock.Current.AddSeconds(31);
                Assert.False(await ratingWorker.ApplyAsync(ratingLease, outcome));
                Assert.Empty(await db.Set<QuoteRatingResult>().ToArrayAsync());
                ratingLease = (await leases.ClaimWorkAsync(QuoteRatingService.WorkKind, cycle.WorkId))!;
            }
            if (retireBeforeApply) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RatingRuleVersion SET State=N'retired' WHERE Id={cycle.RatingRuleVersionId}");
            if (scenario == "revoke-requester") await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Actor.UserId}");
            var recovered = await new QuoteRatingWorker(factory, clock).ExecuteProviderAsync(ratingLease);
            Assert.Equal(outcome.OperationId, recovered.OperationId); Assert.Equal(outcome.CompletedAt, recovered.CompletedAt);
            Assert.True(await ratingWorker.ApplyAsync(ratingLease, recovered)); Assert.False(await ratingWorker.ApplyAsync(ratingLease, recovered));
            if (scenario == "revoke-requester")
            {
                Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => quotes.GetAsync(f.Actor, created.ResourceId))).Status);
                var role = await db.Set<Role>().SingleAsync(x => x.Code == "servicing"); db.Add(new UserRole { UserId = f.Actor.UserId, RoleId = role.Id }); await db.SaveChangesAsync();
            }
            var failed = retireBeforeApply || scenario is "reject" or "revoke-requester";
            var rated = await quotes.GetAsync(f.Actor, created.ResourceId); Assert.Equal(failed ? "rating-pending" : "rated", rated.Quote.State);
            Assert.Single(await db.Set<QuoteRatingResult>().ToArrayAsync());
            var applied = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync();
            Assert.Equal(failed ? "failed" : "rated", applied.State);
            Assert.Equal(!failed, applied.CurrentRatingId is not null);
            Assert.Single(await db.Set<DemoProviderOperation>().Where(x => x.Kind == QuoteRatingService.WorkKind).ToArrayAsync());
            if (scenario == "success" && !expireLease && !retireBeforeApply)
            {
                await CheckUnderwritingApi(db, password, created.ResourceId, rated.Revision.Id, applied.CurrentRatingId!.Value, rated.Quote.RowVersion);
                var lifecycle = new QuoteUnderwritingLifecycle(factory, clock);
                var submitted = await lifecycle.SubmitAsync(f.Actor, created.ResourceId, cycle.Id, rated.Quote.RowVersion, "Review this fictional proposal", Guid.NewGuid().ToString(), Guid.NewGuid());
                var submission = await db.Set<QuoteSubmission>().SingleAsync(); Assert.Equal(submitted.ResourceId, submission.Id); Assert.NotNull(submission.AssignedTeamId);
                rated = await quotes.GetAsync(f.Actor, created.ResourceId);
                var clone = await new QuoteLifecycleService(factory, clock).CloneAsync(f.Actor, created.ResourceId, rated.Quote.RowVersion, rated.Revision.Id, relationship,
                    null, "New fictional draft from rated proposal", Guid.NewGuid().ToString(), Guid.NewGuid());
                var cloned = await quotes.GetAsync(f.Actor, clone.ResourceId); Assert.Equal("draft", cloned.Quote.State);
                Assert.Null(cloned.Quote.CurrentUnderwritingCycleId); Assert.Null(cloned.Quote.CaptureClosedAt);
                await lifecycle.ReturnToDraftAsync(f.Actor, created.ResourceId, cycle.Id, rated.Quote.RowVersion, "Reconsider the fictional proposal", Guid.NewGuid().ToString(), Guid.NewGuid());
                var reopened = await quotes.GetAsync(f.Actor, created.ResourceId); Assert.True(reopened.CanSave); Assert.Null(reopened.Quote.CurrentUnderwritingCycleId);
                Assert.Equal(rated.Revision.Id, reopened.Revision.Id);
                Assert.Equal("superseded", (await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync()).State);
                Assert.Single(await db.Set<QuoteRatingResult>().ToArrayAsync());
            }
            if (!retireBeforeApply) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RatingRuleVersion SET State=N'retired' WHERE Id={cycle.RatingRuleVersionId}");
            Assert.Equal("underwriting-product-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() => rating.RateAsync(f.Actor, created.ResourceId, before.Revision.Id,
                before.Quote.RowVersion, "Fictional rate request", key, Guid.NewGuid()))).Code);
            Assert.Single(await db.Set<UnderwritingCycle>().ToArrayAsync());
        });
    }
}
