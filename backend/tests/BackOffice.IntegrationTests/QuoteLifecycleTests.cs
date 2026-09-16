using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlQuoteLifecycleClonesOwnedHistoryWithExplicitTermsAndAtomicReplay()
    {
        await WithDatabase(async (db, _) =>
        {
            var source = await CreateFixture(db, "-L1"); var target = await CreateFixture(db, "-L2");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={source.Agency} OR Id={target.Agency}");
            await using (var tx = await db.Database.BeginTransactionAsync()) { await QuoteCaptureDemoSeed.SeedAsync(db); await tx.CommitAsync(); }
            var user = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var actor = new ActorContext(user, null, null, new HashSet<string> { "underwriter" });
            var factory = new QuoteFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options);
            var clock = new QuoteTime(); var quotes = new QuoteService(factory, clock); var service = new QuoteLifecycleService(factory, clock);
            var driverId = Guid.NewGuid(); var vehicleId = Guid.NewGuid();
            var proposal = JsonSerializer.Serialize(new { schemaVersion = "1.0", productCode = "motor-trade-road-risks",
                risk = new { drivers = new[] { new { id = driverId, fullName = "Fictional driver" } }, vehicles = new[] { new { id = vehicleId, registration = "DEMO01", ownerDriverId = driverId } } } });
            var created = await quotes.CreateAsync(actor, source.Relationship, source.ProductVersion, proposal, "lifecycle-source-01", Guid.NewGuid());
            var original = await quotes.GetAsync(actor, created.ResourceId);
            await new QuoteEvidenceService(factory, clock).UploadAsync(actor, created.ResourceId, original.Quote.RowVersion, "proof.txt", "text/plain", Encoding.UTF8.GetBytes("Fictional proof"), "lifecycle-source-file", Guid.NewGuid());
            var changed = proposal.Replace("Fictional driver", "Fictional revised driver", StringComparison.Ordinal);
            await quotes.SaveAsync(actor, created.ResourceId, original.Quote.RowVersion, changed, "Corrected name", "lifecycle-save-01", Guid.NewGuid());
            var current = await quotes.GetAsync(actor, created.ResourceId);
            var history = await service.RevisionsAsync(actor, created.ResourceId, 0, 1); Assert.Equal(2, history.TotalCount); Assert.Equal(2, Assert.Single(history.Items).Number);
            Assert.Equal(original.Revision.ProposalJson, (await service.RevisionAsync(actor, created.ResourceId, original.Revision.Id)).Proposal.GetRawText());
            var diff = await service.CompareAsync(actor, created.ResourceId, original.Revision.Id, current.Revision.Id);
            Assert.Equal(driverId, Assert.Single(diff).ItemId); Assert.Equal("/risk/drivers/0/fullName", diff[0].Path);
            var terms = await service.CloneTermsAsync(actor, created.ResourceId, original.Revision.Id, target.Relationship);
            Assert.True(terms.ConfirmationRequired); Assert.Equal(target.Terms, terms.AgencyTermsVersionId);
            Task<BackOffice.Infrastructure.Platform.CommandOutcome> Clone(string key, Guid? confirm = null, Guid? revision = null, byte[]? version = null) =>
                service.CloneAsync(actor, created.ResourceId, version ?? current.Quote.RowVersion, revision ?? original.Revision.Id, target.Relationship,
                    confirm, "Clone historical fictional proposal", key, Guid.NewGuid());
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Clone("clone-no-confirmation"))).Status);
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => Clone("clone-stale-version", target.Terms, version: original.Quote.RowVersion))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => Clone("clone-foreign-revision", target.Terms, Guid.NewGuid()))).Status);
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_LifecycleTestFail ON QuoteActivity AFTER INSERT AS BEGIN SET NOCOUNT ON; THROW 51089, 'Injected lifecycle failure.', 1; END;");
            try { await Assert.ThrowsAsync<DbUpdateException>(() => Clone("clone-atomic-rollback", target.Terms)); }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_LifecycleTestFail"); }
            Assert.Equal(1, await db.Set<Quote>().CountAsync());
            var results = await Task.WhenAll(Clone("clone-atomic-rollback", target.Terms), Clone("clone-atomic-rollback", target.Terms));
            Assert.Equal(results[0].ResourceId, results[1].ResourceId); Assert.Single(results, x => x.Replayed);
            var clone = await quotes.GetAsync(actor, results[0].ResourceId);
            Assert.Equal(original.Revision.Id, clone.Quote.ClonedFromQuoteRevisionId); Assert.Equal(target.Relationship, clone.Quote.RelationshipId);
            Assert.Equal(target.Terms, clone.Revision.AgencyTermsVersionId); Assert.Equal(1, clone.Revision.Number); Assert.Null(clone.Quote.CaptureClosedAt);
            using var cloneJson = JsonDocument.Parse(clone.Revision.ProposalJson); var driver = cloneJson.RootElement.GetProperty("risk").GetProperty("drivers")[0];
            Assert.NotEqual(driverId, driver.GetProperty("id").GetGuid()); Assert.Equal("Fictional driver", driver.GetProperty("fullName").GetString());
            Assert.Equal(driver.GetProperty("id").GetGuid(), cloneJson.RootElement.GetProperty("risk").GetProperty("vehicles")[0].GetProperty("ownerDriverId").GetGuid());
            Assert.Empty(await new QuoteEvidenceService(factory, clock).FilesAsync(actor, clone.Quote.Id));
            Assert.Equal(0, await db.Set<QuoteLookup>().CountAsync(x => x.QuoteId == clone.Quote.Id));
            Assert.Equal(current.Revision.Id, (await quotes.GetAsync(actor, created.ResourceId)).Revision.Id);
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.RevisionAsync(actor, clone.Quote.Id, original.Revision.Id))).Status);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={target.Agency}");
            await Assert.ThrowsAsync<QuoteOperationException>(() => Clone("clone-atomic-rollback", target.Terms));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={target.Agency}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={source.Agency}");
            await Assert.ThrowsAsync<QuoteOperationException>(() => Clone("clone-atomic-rollback", target.Terms));
        });
    }

    [Fact]
    public async Task RealSqlQuoteWithdrawalRollsBackRetainsHistoryAndSerializesAgainstSave()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db); await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var tx = await db.Database.BeginTransactionAsync()) { await QuoteCaptureDemoSeed.SeedAsync(db); await tx.CommitAsync(); }
            var user = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var actor = new ActorContext(user, null, null, new HashSet<string> { "underwriter" });
            var factory = new QuoteFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options);
            var clock = new QuoteTime(); var quotes = new QuoteService(factory, clock); var service = new QuoteLifecycleService(factory, clock);
            var created = await quotes.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "withdraw-source-create", Guid.NewGuid());
            var original = await quotes.GetAsync(actor, created.ResourceId);
            Task<BackOffice.Infrastructure.Platform.CommandOutcome> Withdraw(string key, byte[]? version = null) => service.WithdrawAsync(actor, created.ResourceId,
                version ?? original.Quote.RowVersion, "Fictional quote no longer required", key, Guid.NewGuid());
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_LifecycleTestFail ON QuoteActivity AFTER INSERT AS BEGIN SET NOCOUNT ON; THROW 51089, 'Injected lifecycle failure.', 1; END;");
            try { await Assert.ThrowsAsync<DbUpdateException>(() => Withdraw("withdraw-rollback")); }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_LifecycleTestFail"); }
            Assert.Equal("draft", (await quotes.GetAsync(actor, created.ResourceId)).Quote.State);
            async Task<bool> Attempt(Func<Task> command)
            {
                try { await command(); return true; }
                catch (QuoteOperationException error) when (error.Status == 412) { return false; }
                catch (QuoteInputException error) when (error.Code == "quote-capture-closed") { return false; }
            }
            const string changed = "{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\",\"risk\":{\"materialFacts\":\"Fictional update\"}}";
            var race = await Task.WhenAll(Attempt(async () => { await Withdraw("withdraw-race"); }),
                Attempt(async () => { await quotes.SaveAsync(actor, created.ResourceId, original.Quote.RowVersion, changed, "Fictional revision", "save-race", Guid.NewGuid()); }));
            Assert.Single(race, x => x);
            var saved = await quotes.GetAsync(actor, created.ResourceId);
            var withdrawalKey = race[0] ? "withdraw-race" : "withdraw-after-save";
            var withdrawalVersion = race[0] ? original.Quote.RowVersion : saved.Quote.RowVersion;
            if (!race[0]) await Withdraw(withdrawalKey, withdrawalVersion);
            Assert.True((await Withdraw(withdrawalKey, withdrawalVersion)).Replayed);
            var final = await quotes.GetAsync(actor, created.ResourceId); Assert.Equal("withdrawn", final.Quote.State); Assert.False(final.CanSave);
            Assert.NotNull(final.Quote.CaptureClosedAt); Assert.Equal("Fictional quote no longer required", final.Quote.CaptureClosedReason);
            Assert.Equal(race[0] ? 1 : 2, (await service.RevisionsAsync(actor, created.ResourceId, 0, 100)).TotalCount);
            Assert.Equal(1, await db.Set<QuoteActivity>().CountAsync(x => x.QuoteId == created.ResourceId && x.EventType == "quote.withdrawn"));
            await Assert.ThrowsAsync<QuoteInputException>(() => Withdraw("withdraw-again", final.Quote.RowVersion));
            await Assert.ThrowsAsync<QuoteInputException>(() => service.CloneAsync(actor, created.ResourceId, final.Quote.RowVersion, original.Revision.Id,
                fixture.Relationship, null, "Cannot clone a closed draft", "clone-closed-draft", Guid.NewGuid()));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={fixture.Agency}");
            await Assert.ThrowsAsync<QuoteOperationException>(() => Withdraw(withdrawalKey, withdrawalVersion));
        });
    }
}
