using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlLookupVehicleProvenanceSurvivesUnrelatedEditsAndInvalidatesChangedTargets()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var transaction = await db.Database.BeginTransactionAsync())
            { await QuoteCaptureDemoSeed.SeedAsync(db); await QuoteLookupDemoSeed.SeedAsync(db); await transaction.CommitAsync(); }
            var user = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var actor = new ActorContext(user, null, null, new HashSet<string> { "underwriter" });
            var factory = new QuoteFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options);
            var clock = new QuoteTime(); var quotes = new QuoteService(factory, clock); var lookups = new QuoteLookupService(factory, clock);
            var worker = new QuoteLookupWorker(factory, clock); var leases = new SqlJobLeases(factory, clock); var vehicleId = Guid.NewGuid();
            var proposal = JsonSerializer.Serialize(new { schemaVersion = "1.0", productCode = "motor-trade-road-risks",
                insured = new { legalName = "Fictional provenance test" }, risk = new { vehicles = new[] { new { id = vehicleId, registration = "AB12CDE" } } } });
            var created = await quotes.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, proposal, "provenance-create", Guid.NewGuid());
            var saved = await quotes.GetAsync(actor, created.ResourceId); Assert.Empty(saved.VehicleCaptureModes);
            async Task Decide(string scenario, bool manual)
            {
                var requested = await lookups.RequestAsync(actor, created.ResourceId, saved.Quote.RowVersion, saved.Revision.Id,
                    new("vehicle", "vehicle", vehicleId), scenario, Guid.NewGuid().ToString(), Guid.NewGuid());
                var lookup = await lookups.GetAsync(actor, created.ResourceId, requested.ResourceId);
                var lease = (await leases.ClaimWorkAsync(QuoteLookupService.WorkKind, lookup.WorkId))!;
                var outcome = await worker.ExecuteProviderAsync(lease); await worker.ApplyAsync(lease, outcome);
                await lookups.SelectAsync(actor, created.ResourceId, lookup.Id, saved.Quote.RowVersion, saved.Revision.Id,
                    lookup.InputFingerprint, manual ? null : outcome.Candidates[0].Id, manual ? "Vehicle details checked manually" : null,
                    Guid.NewGuid().ToString(), Guid.NewGuid());
                saved = await quotes.GetAsync(actor, created.ResourceId);
            }
            async Task Edit(Action<JsonNode> edit)
            {
                var draft = JsonNode.Parse(saved.Revision.ProposalJson)!; edit(draft);
                await quotes.SaveAsync(actor, created.ResourceId, saved.Quote.RowVersion, draft.ToJsonString(), null, Guid.NewGuid().ToString(), Guid.NewGuid());
                saved = await quotes.GetAsync(actor, created.ResourceId);
            }
            await Decide("no-match", true); Assert.Equal("manual", saved.VehicleCaptureModes[vehicleId]);
            await Edit(draft => draft["insured"]!["tradingName"] = "Unrelated name change");
            Assert.Equal("manual", saved.VehicleCaptureModes[vehicleId]);
            await Edit(draft => draft["risk"]!["vehicles"]![0]!["make"] = "Changed declaration");
            Assert.Empty(saved.VehicleCaptureModes);
            await Decide("success", false); Assert.Equal("lookup", saved.VehicleCaptureModes[vehicleId]);
            Assert.Contains("Demo Motors", saved.Revision.ProposalJson);
            await Edit(draft => draft["risk"]!["vehicles"] = new JsonArray()); Assert.Empty(saved.VehicleCaptureModes);
            Assert.Equal(2, await db.Set<QuoteLookupSelection>().CountAsync());
        });
    }
}
