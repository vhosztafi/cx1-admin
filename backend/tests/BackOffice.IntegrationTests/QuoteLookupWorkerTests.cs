using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    private sealed class LookupClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddHours(1);
    }

    [Fact]
    public async Task RealSqlLookupProviderRetainsOutcomesAcrossTimeoutRestartAndExpiredLeases()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var transaction = await db.Database.BeginTransactionAsync())
            { await QuoteCaptureDemoSeed.SeedAsync(db); await transaction.CommitAsync(); }
            var clock = new LookupClock();
            var scenarios = new[] { "success", "multiple", "no-match", "reject", "fail-once", "timeout-after-success" };
            foreach (var scenario in scenarios) db.Add(new SettingVersion { Scope = "quote-lookup/" + scenario, Version = 1,
                EffectiveFrom = clock.GetUtcNow(), Values = JsonSerializer.Serialize(new { demo = true, kind = "quote-lookup", scenario }) });
            await db.SaveChangesAsync();
            var user = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var actor = new ActorContext(user, null, null, new HashSet<string> { "underwriter" });
            var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options;
            var factory = new QuoteFactory(options); var quotes = new QuoteService(factory, clock); var service = new QuoteLookupService(factory, clock);
            var leases = new SqlJobLeases(factory, clock); var worker = new QuoteLookupWorker(factory, clock);
            const string proposal = "{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\",\"insured\":{\"address\":{\"postcode\":\"AB1 2CD\"}}}";
            var created = await quotes.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, proposal, "worker-create", Guid.NewGuid());
            var saved = await quotes.GetAsync(actor, created.ResourceId);
            foreach (var scenario in scenarios)
            {
                var request = await service.RequestAsync(actor, created.ResourceId, saved.Quote.RowVersion, saved.Revision.Id,
                    new QuoteLookupTarget("address", "insured"), scenario, "worker-" + scenario, Guid.NewGuid());
                var lookup = await service.GetAsync(actor, created.ResourceId, request.ResourceId);
                var lease = (await leases.ClaimWorkAsync(QuoteLookupService.WorkKind, lookup.WorkId))!; Assert.NotNull(lease);
                if (scenario is "fail-once" or "timeout-after-success")
                {
                    var error = await Assert.ThrowsAsync<QuoteLookupProviderException>(() => worker.ExecuteProviderAsync(lease));
                    Assert.Equal(scenario == "fail-once" ? JobFailure.ProviderUnavailable : JobFailure.ProviderTimeout, error.Failure);
                    Assert.True(await leases.FailAsync(lease, error.Failure));
                    if (scenario == "timeout-after-success") Assert.NotNull(await db.Set<DemoProviderOperation>().Where(x => x.OperationKey == lease.OperationKey).Select(x => x.Result).SingleAsync());
                    clock.Advance(); lease = (await leases.ClaimWorkAsync(QuoteLookupService.WorkKind, lookup.WorkId))!;
                }
                // A new worker recovers the independently committed provider outcome.
                worker = new QuoteLookupWorker(factory, clock); var outcome = await worker.ExecuteProviderAsync(lease);
                Assert.Equal(scenario == "reject" ? "rejected" : scenario == "no-match" ? "no-match" : "succeeded", outcome.State);
                Assert.Equal(scenario == "multiple" ? 2 : scenario is "reject" or "no-match" ? 0 : 1, outcome.Candidates.Length);
                if (scenario == "success")
                {
                    var old = lease; clock.Advance(); lease = (await leases.ClaimWorkAsync(QuoteLookupService.WorkKind, lookup.WorkId))!;
                    Assert.False(await worker.ApplyAsync(old, outcome));
                    Assert.Equal(JsonSerializer.Serialize(outcome), JsonSerializer.Serialize(await worker.ExecuteProviderAsync(lease)));
                    await Assert.ThrowsAsync<QuoteLookupProviderException>(() => worker.ApplyAsync(lease, outcome with { Source = "forged" }));
                }
                Assert.True(await worker.ApplyAsync(lease, outcome)); Assert.False(await worker.ApplyAsync(lease, outcome));
                var completed = await service.GetAsync(actor, created.ResourceId, lookup.Id); Assert.Equal(outcome.State, completed.State);
                var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == lookup.WorkId);
                Assert.DoesNotContain("Fictional Demo Street", work.Result!);
                var responses = await db.Set<AdapterAttempt>().Where(x => x.WorkId == work.Id && x.Response != null).Select(x => x.Response!).ToArrayAsync();
                Assert.All(responses, value => Assert.DoesNotContain("Fictional Demo Street", value));
            }
            Assert.Equal(6, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == QuoteLookupService.WorkKind));
            Assert.Equal(saved.Revision.Id, (await quotes.GetAsync(actor, created.ResourceId)).Revision.Id);
            var successful = await db.Set<QuoteLookup>().AsNoTracking().SingleAsync(x => x.Scenario == "success");
            using var result = JsonDocument.Parse(successful.ResultJson!);
            var candidateId = result.RootElement.GetProperty("candidates")[0].GetProperty("id").GetGuid();
            Assert.Equal(422, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SelectAsync(actor, created.ResourceId,
                successful.Id, saved.Quote.RowVersion, saved.Revision.Id, successful.InputFingerprint, Guid.NewGuid(), null, "invalid-candidate", Guid.NewGuid()))).Status);
            await service.SelectAsync(actor, created.ResourceId, successful.Id, saved.Quote.RowVersion, saved.Revision.Id,
                successful.InputFingerprint, candidateId, null, "select-candidate", Guid.NewGuid());
            var selected = await quotes.GetAsync(actor, created.ResourceId);
            Assert.Equal(2, selected.Revision.Number); Assert.Contains("Fictional Demo Street", selected.Revision.ProposalJson);
            Assert.True((await service.SelectAsync(actor, created.ResourceId, successful.Id, saved.Quote.RowVersion, saved.Revision.Id,
                successful.InputFingerprint, candidateId, null, "select-candidate", Guid.NewGuid())).Replayed);
            var oldNoMatch = await db.Set<QuoteLookup>().AsNoTracking().SingleAsync(x => x.Scenario == "no-match");
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SelectAsync(actor, created.ResourceId,
                oldNoMatch.Id, selected.Quote.RowVersion, selected.Revision.Id, oldNoMatch.InputFingerprint, null, "Manual details", "stale-selection", Guid.NewGuid()))).Status);
            var manualRequest = await service.RequestAsync(actor, created.ResourceId, selected.Quote.RowVersion, selected.Revision.Id,
                new("address", "insured"), "no-match", "manual-request", Guid.NewGuid());
            var manual = await service.GetAsync(actor, created.ResourceId, manualRequest.ResourceId);
            var manualLease = (await leases.ClaimWorkAsync(QuoteLookupService.WorkKind, manual.WorkId))!;
            await worker.ApplyAsync(manualLease, await worker.ExecuteProviderAsync(manualLease));
            var revisionCount = await db.Set<QuoteRevision>().CountAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_LookupSelectionTestFail ON QuoteLookupSelection AFTER INSERT AS BEGIN SET NOCOUNT ON; THROW 51077, 'Injected selection failure.', 1; END;");
            try
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => service.SelectAsync(actor, created.ResourceId, manual.Id,
                    selected.Quote.RowVersion, selected.Revision.Id, manual.InputFingerprint, null, "Address checked manually", "manual-select", Guid.NewGuid()));
                Assert.Equal(revisionCount, await db.Set<QuoteRevision>().CountAsync());
                Assert.Equal(selected.Revision.Id, (await quotes.GetAsync(actor, created.ResourceId)).Revision.Id);
            }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_LookupSelectionTestFail"); }
            await service.SelectAsync(actor, created.ResourceId, manual.Id, selected.Quote.RowVersion, selected.Revision.Id,
                manual.InputFingerprint, null, "Address checked manually", "manual-select", Guid.NewGuid());
            var manualSaved = await quotes.GetAsync(actor, created.ResourceId);
            Assert.Equal(3, manualSaved.Revision.Number); Assert.Equal(selected.Revision.ProposalJson, manualSaved.Revision.ProposalJson);
            Assert.Equal(2, await db.Set<QuoteLookupSelection>().CountAsync());
            // Unknown interruptions consume the bounded lease budget. The final
            // expired lease must atomically publish a durable failed lookup.
            var exhaustedRequest = await service.RequestAsync(actor, created.ResourceId, manualSaved.Quote.RowVersion,
                manualSaved.Revision.Id, new("address", "insured"), "success", "exhausted-request", Guid.NewGuid());
            var exhausted = await service.GetAsync(actor, created.ResourceId, exhaustedRequest.ResourceId);
            var attemptLimit = await db.Set<OutboxWork>().Where(x => x.Id == exhausted.WorkId).Select(x => x.AttemptLimit).SingleAsync();
            JobLease? abandonedLease = null;
            for (var attempt = 0; attempt < attemptLimit; attempt++)
            {
                if (attempt > 0) clock.Advance();
                abandonedLease = (await leases.ClaimWorkAsync(QuoteLookupService.WorkKind, exhausted.WorkId))!;
                Assert.NotNull(abandonedLease);
            }
            var abandonedOutcome = await worker.ExecuteProviderAsync(abandonedLease!);
            clock.Advance();
            Assert.Null(await leases.ClaimWorkAsync(QuoteLookupService.WorkKind, exhausted.WorkId));
            var failed = await service.GetAsync(actor, created.ResourceId, exhausted.Id);
            Assert.Equal("failed", failed.State); Assert.Contains("attempts-exhausted", failed.ResultJson!);
            Assert.False(await worker.ApplyAsync(abandonedLease!, abandonedOutcome));
            // Selection and a normal edit race on the same quote ETag. Exactly
            // one may append a revision; neither can silently replace the other.
            async Task<bool> Race(Func<Task<CommandOutcome>> action)
            {
                try { await action(); return true; }
                catch (QuoteOperationException error) when (error.Status == 412) { return false; }
            }
            var race = await Task.WhenAll(
                Race(() => service.SelectAsync(actor, created.ResourceId, failed.Id, manualSaved.Quote.RowVersion,
                    manualSaved.Revision.Id, failed.InputFingerprint, null, "Provider exhausted; manual check", "race-select", Guid.NewGuid())),
                Race(() => quotes.SaveAsync(actor, created.ResourceId, manualSaved.Quote.RowVersion,
                    manualSaved.Revision.ProposalJson.Replace("AB1 2CD", "CD1 2EF"), null, "race-save", Guid.NewGuid())));
            Assert.Single(race, x => x);
            Assert.Equal(4, (await quotes.GetAsync(actor, created.ResourceId)).Revision.Number);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={fixture.Agency}");
            await Assert.ThrowsAsync<QuoteOperationException>(() => service.SelectAsync(actor, created.ResourceId, manual.Id,
                selected.Quote.RowVersion, selected.Revision.Id, manual.InputFingerprint, null, "Address checked manually", "manual-select", Guid.NewGuid()));

        });
    }
}
