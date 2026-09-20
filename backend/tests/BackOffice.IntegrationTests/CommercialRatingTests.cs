using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Underwriting;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Platform;
using BackOffice.Application.Quotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("stale-configuration")]
    [InlineData("revoke-requester")]
    public async Task RealSqlCommercialRatingDurableJobReplaysAndRechecksCurrentAuthority(string scenario)
    {
        await WithDatabase(async (db, password) =>
        {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true, includeCommercialCapture: true);
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await CommercialUnderwritingSeed.SeedAsync(db); await transaction.CommitAsync();
            }
            var f = await Fixture(db, 3, "commercial-combined");
            var relationship = await db.Set<Quote>().Where(x => x.Id == f.Quote).Select(x => x.RelationshipId).SingleAsync();
            var factory = new RatingFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options);
            var clock = new RatingClock(); var quotes = new QuoteService(factory, clock); var service = new QuoteRatingService(factory, clock);
            using var stream = typeof(UnderwritingRuntimeTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!;
            using var fixture = JsonDocument.Parse(stream); var proposal = JsonNode.Parse(fixture.RootElement.GetRawText())!;
            proposal["termIntent"]!["localStartDate"] = "2026-09-17";
            var created = await quotes.CreateAsync(f.Actor, relationship, f.ProductVersion, proposal.ToJsonString(), Guid.NewGuid().ToString(), Guid.NewGuid());
            var before = await quotes.GetAsync(f.Actor, created.ResourceId);
            var key = Guid.NewGuid().ToString();
            var stale = await Assert.ThrowsAsync<QuoteOperationException>(() => service.RateAsync(f.Actor, created.ResourceId, Guid.NewGuid(), before.Quote.RowVersion, "Stale revision", Guid.NewGuid().ToString(), Guid.NewGuid()));
            Assert.Equal(412, stale.Status);
            Assert.Empty(await db.Set<UnderwritingCycle>().ToArrayAsync());
            var request = await service.RateAsync(f.Actor, created.ResourceId, before.Revision.Id, before.Quote.RowVersion, "Fictional commercial price", key, Guid.NewGuid());
            Assert.Equal(202, request.Status);
            Assert.True((await service.RateAsync(f.Actor, created.ResourceId, before.Revision.Id, before.Quote.RowVersion, "Fictional commercial price", key, Guid.NewGuid())).Replayed);
            var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync();
            var stored = JsonSerializer.Deserialize<StoredRatingInput>(cycle.InputJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.True(stored.IsCommercial); Assert.Null(stored.Input); Assert.NotNull(stored.Commercial);
            var leases = new SqlJobLeases(factory, clock); var worker = new QuoteRatingWorker(factory, clock);
            var lease = (await leases.ClaimWorkAsync(QuoteRatingService.WorkKind, cycle.WorkId))!;
            var outcome = await worker.ExecuteProviderAsync(lease);
            var replay = await new QuoteRatingWorker(factory, clock).ExecuteProviderAsync(lease);
            Assert.Equal(outcome.OperationId, replay.OperationId); Assert.Equal(outcome.CompletedAt, replay.CompletedAt);
            Assert.NotNull(outcome.Rating); Assert.Equal(75m, outcome.Rating.Fee);
            if (scenario == "stale-configuration")
            {
                var current = await db.Set<SettingVersion>().Where(x => x.Scope == "underwriting-runtime").OrderByDescending(x => x.Version).FirstAsync();
                db.Add(new SettingVersion { Scope = current.Scope, Version = current.Version + 1, EffectiveFrom = Now, Values = current.Values });
                await db.SaveChangesAsync();
            }
            if (scenario == "revoke-requester")
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Actor.UserId}");
            Assert.True(await worker.ApplyAsync(lease, replay)); Assert.False(await worker.ApplyAsync(lease, replay));
            db.ChangeTracker.Clear();
            var applied = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync();
            var result = await db.Set<QuoteRatingResult>().SingleAsync();
            Assert.Equal(outcome.Rating.AnnualPremium, result.AnnualPremium);
            Assert.Equal(outcome.Rating.GrossPayable, result.GrossPayable);
            Assert.Single(await db.Set<DemoProviderOperation>().Where(x => x.Kind == QuoteRatingService.WorkKind).ToArrayAsync());
            Assert.Equal(scenario == "success" ? "rated" : "failed", applied.State);
            Assert.Equal(scenario == "success", applied.CurrentRatingId.HasValue);
            if (scenario == "success")
            {
                var model = new QuoteUnderwritingReadModel(factory, clock);
                var view = await model.RatingAsync(f.Actor, result.Id);
                Assert.True((bool)view["applicable"]); Assert.Equal(result.GrossPayable.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), view["grossPayable"]);
                var assessment = await model.AssessmentAsync(f.Actor, created.ResourceId);
                var capabilities = JsonSerializer.SerializeToElement(assessment["capabilities"]);
                Assert.False(capabilities.GetProperty("canIssue").GetBoolean()); Assert.True(capabilities.GetProperty("canSubmit").GetBoolean());
                var saved = await quotes.GetAsync(f.Actor, created.ResourceId);
                await CheckUnderwritingApi(db, password, created.ResourceId, before.Revision.Id, result.Id, saved.Quote.RowVersion, true, result.AnnualPremium.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (scenario == "revoke-requester")
                Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.RateAsync(f.Actor, created.ResourceId, before.Revision.Id, before.Quote.RowVersion, "Fictional commercial price", key, Guid.NewGuid()))).Status);
        });
    }

    [Fact]
    public async Task RealSqlCommercialRatingEligibilityRequiresV3AndRejectsCrossProductConfiguration()
    {
        await WithDatabase(async (db, password) =>
        {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true, includeCommercialCapture: true);
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await CommercialUnderwritingSeed.SeedAsync(db); await transaction.CommitAsync();
            }
            var fixture = await Fixture(db, 3, "commercial-combined");
            var captureOnly = await Fixture(db, 2, "commercial-combined");
            async Task<EligibleQuoteRating> Resolve(RatingFixture subject)
            {
                db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync();
                var owned = await QuoteUnderwritingScope.HoldAsync(db, subject.Actor, subject.Quote, "quote-rate");
                var result = await QuoteRatingEligibility.ResolveAsync(db, owned, subject.ProductVersion, subject.Terms, Term, Now);
                await transaction.CommitAsync(); return result;
            }
            var current = await Resolve(fixture);
            Assert.Equal("commercial-demo-rate-1", current.RatingVersion.Version);
            Assert.Equal(1250, current.CommissionBasisPoints);
            Assert.Equal("underwriting-product-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() => Resolve(captureOnly))).Code);
            var runtime = JsonNode.Parse(current.RuntimeVersion.Values)!;
            var other = runtime["products"]!.AsArray().First(x => x!["productVersionId"]!.GetValue<Guid>() != fixture.ProductVersion)!;
            var choice = runtime["products"]!.AsArray().Single(x => x!["productVersionId"]!.GetValue<Guid>() == fixture.ProductVersion)!;
            choice["ratingRuleVersionId"] = other["ratingRuleVersionId"]!.DeepClone();
            db.Add(new SettingVersion { Scope = "underwriting-runtime", Version = current.RuntimeVersion.Version + 1, EffectiveFrom = Now, Values = runtime.ToJsonString() });
            await db.SaveChangesAsync();
            Assert.Equal("underwriting-product-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() => Resolve(fixture))).Code);
        });
    }

    [Fact]
    public async Task RealSqlCommercialRatingPublicationPreservesV2AndNeverRegrantsWithdrawnRuntime()
    {
        await WithDatabase(async (db, password) =>
        {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true, includeCommercialCapture: true);
            var product = await db.Set<Product>().SingleAsync(x => x.Code == "commercial-combined");
            var v2 = await db.Set<ProductVersion>().AsNoTracking().SingleAsync(x => x.ProductId == product.Id && x.Version == 2);
            var before = await db.Set<SettingVersion>().AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Values);
            var grants = await db.Set<UserAuthorityGrant>().CountAsync();
            var credentials = await db.Set<UserCredential>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.PasswordHash).ToArrayAsync();
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true, includeCommercialCapture: true, includeCommercialUnderwriting: true);
            db.ChangeTracker.Clear();
            var v3 = await db.Set<ProductVersion>().SingleAsync(x => x.ProductId == product.Id && x.Version == 3);
            using var definition = JsonDocument.Parse(v3.Definition);
            Assert.True(definition.RootElement.GetProperty("ratingAvailable").GetBoolean());
            var changed = v3.Definition.Replace("commercial-demo", "modified-demo", StringComparison.Ordinal);
            var immutable = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ProductVersion SET Definition={changed} WHERE Id={v3.Id}"));
            Assert.Equal(51401, immutable.Number);
            foreach (var kind in new[] { "commercial-combined-underwriting", "unknown" })
            {
                var duplicate = JsonNode.Parse(v3.Definition)!; duplicate["kind"] = kind;
                db.Add(new ProductVersion { ProductId = v3.ProductId, ProviderId = v3.ProviderId, Version = 4,
                    State = "published", EffectiveFrom = v3.EffectiveFrom, EffectiveTo = v3.EffectiveTo,
                    JsonSchemaVersion = v3.JsonSchemaVersion, QuestionSetVersion = v3.QuestionSetVersion, Definition = duplicate.ToJsonString() });
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                Assert.Equal(51001, Assert.IsType<SqlException>(error.InnerException).Number);
                db.ChangeTracker.Clear();
            }
            Assert.Equal(v2.Definition, (await db.Set<ProductVersion>().SingleAsync(x => x.Id == v2.Id)).Definition);
            foreach (var pair in before) Assert.Equal(pair.Value, (await db.Set<SettingVersion>().SingleAsync(x => x.Id == pair.Key)).Values);
            Assert.Equal(grants, await db.Set<UserAuthorityGrant>().CountAsync());
            Assert.Empty(await db.Set<AgencyTermsVersion>().ToArrayAsync());
            Assert.Equal(credentials, await db.Set<UserCredential>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.PasswordHash).ToArrayAsync());
            var runtime = await db.Set<SettingVersion>().Where(x => x.Scope == "underwriting-runtime").OrderByDescending(x => x.Version).FirstAsync();
            Assert.Equal(3, UnderwritingRuntimeConfiguration.Parse(runtime.Values)!.Products.Count);
            Assert.True(UnderwritingRuntimeConfiguration.Parse(runtime.Values)!.Products.ContainsKey(v3.Id));
            var rating = await db.Set<RatingRuleVersion>().SingleAsync(x => x.ProductId == product.Id);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RatingRuleVersion SET State=N'retired' WHERE Id={rating.Id}");
            var disabled = JsonNode.Parse(runtime.Values)!; disabled["products"] = new JsonArray();
            db.Add(new SettingVersion { Scope = runtime.Scope, Version = runtime.Version + 1, EffectiveFrom = Now, Values = disabled.ToJsonString() });
            await db.SaveChangesAsync();
            var count = await db.Set<SettingVersion>().CountAsync();
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await CommercialUnderwritingSeed.SeedAsync(db);
                await transaction.CommitAsync();
            }
            db.ChangeTracker.Clear();
            Assert.Equal(count, await db.Set<SettingVersion>().CountAsync());
            Assert.Equal("retired", (await db.Set<RatingRuleVersion>().SingleAsync(x => x.Id == rating.Id)).State);
            Assert.Empty(UnderwritingRuntimeConfiguration.Parse((await db.Set<SettingVersion>().Where(x => x.Scope == runtime.Scope).OrderByDescending(x => x.Version).FirstAsync()).Values)!.Products);
        });
    }
}
