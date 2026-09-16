using System.Text;
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
    [Fact]
    public async Task RealSqlBothProductsBecomeCaptureReadyOnlyWithCurrentStoredProvenance()
    {
        await WithDatabase(async (db, _) =>
        {
            await using (var tx = await db.Database.BeginTransactionAsync())
            { await QuoteCaptureDemoSeed.SeedAsync(db); await QuoteLookupDemoSeed.SeedAsync(db); await MatchDemoSeed.SeedAsync(db); await tx.CommitAsync(); }
            var factory = new QuoteFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options);
            var clock = new QuoteTime(); await new QuoteDemo(factory, clock).SeedAsync();
            var actor = new ActorContext(await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync(), null, null, new HashSet<string> { "underwriter" });
            var quotes = new QuoteService(factory, clock); var lookups = new QuoteLookupService(factory, clock);
            var evidence = new QuoteEvidenceService(factory, clock); var leases = new SqlJobLeases(factory, clock); var worker = new QuoteLookupWorker(factory, clock);
            var products = await new QuoteProducts(factory, clock).ListAsync(actor, QuoteDemo.RelationshipId);
            Assert.Equal(2, products.Count);
            foreach (var product in products)
            {
                using var stream = typeof(QuoteDemo).Assembly.GetManifestResourceStream($"QuoteDemo.quote-capture-{product.ProductCode}.json")!;
                using var fixture = JsonDocument.Parse(stream); var proposal = fixture.RootElement.GetProperty("proposal");
                var created = await quotes.CreateAsync(actor, QuoteDemo.RelationshipId, product.ProductVersionId, proposal.GetRawText(), Guid.NewGuid().ToString(), Guid.NewGuid());
                var saved = await quotes.GetAsync(actor, created.ResourceId);
                QuoteReadinessResult Assess(StoredQuote value)
                {
                    using var json = JsonDocument.Parse(value.Revision.ProposalJson);
                    return QuoteReadiness.Assess(value.Quote.Id, value.Revision.Id, json.RootElement, value.TermAssessment, value.CaptureUnavailableCode,
                        new DateOnly(2026,9,15), value.VehicleCaptureModes, value.CurrentEvidence, value.MatchingCode);
                }
                Assert.False(Assess(saved).Ready);
                foreach (var vehicle in proposal.GetProperty("risk").GetProperty("vehicles").EnumerateArray())
                {
                    var request = await lookups.RequestAsync(actor, created.ResourceId, saved.Quote.RowVersion, saved.Revision.Id,
                        new("vehicle", "vehicle", vehicle.GetProperty("id").GetGuid()), "no-match", Guid.NewGuid().ToString(), Guid.NewGuid());
                    var lookup = await lookups.GetAsync(actor, created.ResourceId, request.ResourceId);
                    var lease = (await leases.ClaimWorkAsync(QuoteLookupService.WorkKind, lookup.WorkId))!;
                    Assert.True(await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease)));
                    await lookups.SelectAsync(actor, created.ResourceId, lookup.Id, saved.Quote.RowVersion, saved.Revision.Id,
                        lookup.InputFingerprint, null, "Fictional details checked manually", Guid.NewGuid().ToString(), Guid.NewGuid());
                    saved = await quotes.GetAsync(actor, created.ResourceId);
                }
                Assert.All(Assess(saved).Issues, issue => Assert.Equal("evidence", issue.Category));
                var bytes = Encoding.UTF8.GetBytes("Fictional readiness evidence only");
                var upload = await evidence.UploadAsync(actor, created.ResourceId, saved.Quote.RowVersion, "proof.txt", "text/plain", bytes, Guid.NewGuid().ToString(), Guid.NewGuid());
                saved = await quotes.GetAsync(actor, created.ResourceId);
                foreach (var requirement in (await evidence.ReadAsync(actor, created.ResourceId)).Requirements)
                {
                    await evidence.AttachAsync(actor, created.ResourceId, saved.Quote.RowVersion, saved.Revision.Id, requirement.Code, requirement.RiskItemId,
                        upload.ResourceId, requirement.InputFingerprint, "Fictional capture evidence reviewed", Guid.NewGuid().ToString(), Guid.NewGuid());
                    saved = await quotes.GetAsync(actor, created.ResourceId);
                }
                var ready = Assess(saved); Assert.True(ready.Ready, string.Join(',', ready.Issues.Select(x => x.Code))); Assert.Empty(ready.Issues);
                Assert.Equal(bytes, (await new QuoteEvidenceService(factory, clock).DownloadAsync(actor, created.ResourceId, upload.ResourceId)).Content);
                // Fresh services recover the actual stored assessment, not an in-memory flag.
                Assert.True(Assess(await new QuoteService(factory, clock).GetAsync(actor, created.ResourceId)).Ready);
                var changed = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(saved.Revision.ProposalJson)!;
                changed["insured"]!["firstName"] = "Changed fictional identity";
                await quotes.SaveAsync(actor, created.ResourceId, saved.Quote.RowVersion, changed.ToJsonString(), null, Guid.NewGuid().ToString(), Guid.NewGuid());
                var stale = Assess(await quotes.GetAsync(actor, created.ResourceId)); Assert.False(stale.Ready); Assert.Contains(stale.Issues, x => x.Category == "evidence");
            }
        });
    }

    [Fact]
    public async Task RealSqlLegacyIdentityRepairRequiresMatchingAssessmentAndNoOpSaveCreatesReview()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db); await CreateFixture(db, "-DUPLICATE");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var tx = await db.Database.BeginTransactionAsync())
            { await QuoteCaptureDemoSeed.SeedAsync(db); await MatchDemoSeed.SeedAsync(db); await tx.CommitAsync(); }
            var actor = new ActorContext(await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync(), null, null, new HashSet<string> { "underwriter" });
            var factory = new QuoteFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options);
            var service = new QuoteService(factory, TimeProvider.System);
            var created = await service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "legacy-create", Guid.NewGuid());
            var saved = await service.GetAsync(actor, created.ResourceId); Assert.Equal("quote-match-identity-incomplete", saved.MatchingCode);
            var client = await db.Set<ClientAccount>().SingleAsync(x => x.Id == fixture.Client);
            client.EntityType = "sole-trader"; client.Address = "{\"line1\":\"1 Fictional Lane\",\"town\":\"Sheffield\",\"postcode\":\"S1 1AA\",\"country\":\"GB\"}";
            await db.SaveChangesAsync();
            saved = await service.GetAsync(actor, created.ResourceId); Assert.Equal("quote-match-assessment-required", saved.MatchingCode);
            await service.SaveAsync(actor, created.ResourceId, saved.Quote.RowVersion, saved.Revision.ProposalJson, null, "legacy-assess", Guid.NewGuid());
            var assessed = await service.GetAsync(actor, created.ResourceId);
            Assert.Equal("quote-match-review-required", assessed.MatchingCode); Assert.NotNull(assessed.MatchReviewId);
            Assert.Equal(saved.Revision.Id, assessed.Revision.Id); Assert.NotEqual(saved.Quote.RowVersion, assessed.Quote.RowVersion);
            Assert.True((await service.SaveAsync(actor, created.ResourceId, saved.Quote.RowVersion, saved.Revision.ProposalJson, null, "legacy-assess", Guid.NewGuid())).Replayed);
            Assert.Single(await db.Set<MatchSubmission>().Where(x => x.QuoteId == created.ResourceId).ToArrayAsync());
        });
    }
}
