using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlAttachedMatchUsesQuoteVersionPreservesOwnershipAndFencesReplay()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var tx = await db.Database.BeginTransactionAsync()) { await QuoteCaptureDemoSeed.SeedAsync(db); await MatchDemoSeed.SeedAsync(db); await tx.CommitAsync(); }
            var actorId = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var actor = new ActorContext(actorId, null, null, new HashSet<string> { "underwriter" });
            var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options;
            var factory = new QuoteFactory(options); var clock = new QuoteTime(); var service = new QuoteService(factory, clock);
            var created = await service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "attached-quote", Guid.NewGuid());
            var original = await service.GetAsync(actor, created.ResourceId);
            var rule = await db.Set<SettingVersion>().SingleAsync(x => x.Id == MatchDemoSeed.RuleId);
            var intake = new MatchSubmission { Reference = "MI-ATTACHED-QUOTE", AgencyId = fixture.Agency, QuoteId = created.ResourceId,
                LinkedClientId = fixture.Client, LinkedRelationshipId = fixture.Relationship,
                IdentitySnapshot = JsonSerializer.Serialize(new ClientWrite("Fictional independent trader", "sole-trader", new("1 Fictional Road", "Sheffield", "S1 1AA", "GB")), new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
            var review = new MatchReview { SubmissionId = intake.Id, CandidateClientId = fixture.Client, CandidateRelationshipId = fixture.Relationship,
                RuleVersionId = rule.Id, RuleSnapshot = rule.Values, Signals = JsonSerializer.Serialize(new[] { new MatchSignal("legal-name", "Fictional identity comparison", "Fictional independent trader", "Fictional quote client", "strong", "different") }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
            db.AddRange(intake, review); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var commands = new SqlCommandBoundary(factory, clock);
            async Task<CommandOutcome> Decide(string outcome, string key, byte[]? quoteVersion)
            {
                await using var read = factory.CreateDbContext();
                var version = await read.Set<MatchReview>().AsNoTracking().Where(x => x.Id == review.Id).Select(x => x.RowVersion).SingleAsync();
                var fields = new ValidatedMatchDecision(outcome, "Fictional identity decision", null,
                    quoteVersion is null ? null : "\"" + Convert.ToBase64String(quoteVersion) + "\"");
                return await commands.ExecuteAuthorizedAsync(new(actorId, "/test/attached-match", key, Guid.NewGuid()), fields, "match." + outcome,
                    (scope, ct) => MatchService.AuthorizeHeldAsync(scope, actor, review.Id, fields, ct), async (scope, ct) =>
                    {
                        var saved = await MatchService.DecideAsync(scope, actor, review.Id, version, fields, clock.GetUtcNow(), ct, quoteVersion);
                        return new(saved.Id, 200, JsonSerializer.Serialize(new { id = saved.Id }));
                    });
            }
            Assert.Equal(428, (await Assert.ThrowsAsync<MatchOperationException>(() => Decide("separate", "missing-quote-version", null))).Status);
            Assert.Equal(412, (await Assert.ThrowsAsync<MatchOperationException>(() => Decide("separate", "stale-quote-version", new byte[8]))).Status);
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_AttachedMatchRollback ON MatchDecision AFTER INSERT AS BEGIN THROW 51084, 'Injected match rollback.', 1; END;");
            try { await Assert.ThrowsAsync<DbUpdateException>(() => Decide("separate", "rolled-back", original.Quote.RowVersion)); }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_AttachedMatchRollback"); }
            Assert.Equal(fixture.Client, (await service.GetAsync(actor, created.ResourceId)).Quote.ClientId);
            Assert.Equal(1, await db.Set<QuoteRevision>().CountAsync());
            var results = await Task.WhenAll(Decide("separate", "separate-once", original.Quote.RowVersion), Decide("separate", "separate-once", original.Quote.RowVersion));
            Assert.Single(results, x => x.Replayed);
            var separated = await service.GetAsync(actor, created.ResourceId);
            Assert.NotEqual(fixture.Client, separated.Quote.ClientId); Assert.Equal(2, separated.Revision.Number);
            Assert.Equal(separated.Quote.ClientId, separated.Revision.ClientId);
            Assert.Equal(fixture.Client, (await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == original.Revision.Id)).ClientId);
            Assert.Equal(original.Revision.ProposalJson, separated.Revision.ProposalJson);
            var ownershipChanges = await new QuoteLifecycleService(factory, clock).CompareAsync(actor, created.ResourceId, original.Revision.Id, separated.Revision.Id);
            Assert.Equal(new[] { "/clientId", "/relationshipId" }, ownershipChanges.Select(x=>x.Path).Order().ToArray());
            await Decide("reopen", "reopen-once", separated.Quote.RowVersion);
            var reopened = await service.GetAsync(actor, created.ResourceId);
            Assert.NotEqual(separated.Quote.RowVersion, reopened.Quote.RowVersion);
            var lifecycle = new QuoteLifecycleService(factory, clock);
            async Task<int> Close()
            {
                try { return (await lifecycle.WithdrawAsync(actor, created.ResourceId, reopened.Quote.RowVersion, "Fictional closure", "race-close", Guid.NewGuid())).Status; }
                catch (QuoteOperationException error) { return error.Status; }
            }
            async Task<int> Link()
            {
                try { return (await Decide("link", "race-link", reopened.Quote.RowVersion)).Status; }
                catch (MatchOperationException error) { return error.Status; }
            }
            var raced = await Task.WhenAll(Close(), Link());
            Assert.Single(raced, x => x == 200); Assert.Single(raced, x => x is 409 or 412);
            var afterRace = await service.GetAsync(actor, created.ResourceId);
            if (afterRace.Quote.State == "draft")
                await lifecycle.WithdrawAsync(actor, created.ResourceId, afterRace.Quote.RowVersion, "Fictional closure", "close-after-race", Guid.NewGuid());
            var closed = await service.GetAsync(actor, created.ResourceId);
            Assert.Equal(409, (await Assert.ThrowsAsync<MatchOperationException>(() => Decide("link", "closed-link", closed.Quote.RowVersion))).Status);
            Assert.True((await Decide("reopen", "reopen-once", separated.Quote.RowVersion)).Replayed);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={fixture.Agency}");
            Assert.Equal(409, (await Assert.ThrowsAsync<MatchOperationException>(() => Decide("reopen", "reopen-once", separated.Quote.RowVersion))).Status);
            Assert.Equal(raced[1] == 200 ? 3 : 2, await db.Set<MatchDecision>().CountAsync(x => x.MatchId == review.Id));
            Assert.Equal(raced[1] == 200 ? 3 : 2, await db.Set<QuoteRevision>().CountAsync());
        });
    }
}
