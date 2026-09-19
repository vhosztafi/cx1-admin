using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlCommercialCaptureHttpUsesClosedProposalCurrentScopeAndExactReceipts()
    {
        await WithDatabase(async (db, password) =>
        {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeCommercialCapture: true);
            var fixture = await CreateFixture(db, "-CC-HTTP", CommercialCaptureRules.ProductCode, 2);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString())
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "commercial-capture-test-keys", db.Database.GetDbConnection().Database)))
                .ConfigureServices(services => {
                    services.AddSingleton<TimeProvider>(new QuoteTime());
                    services.AddScoped(provider => new QuoteService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new QuoteTime()));
                }));
            using var client = host.CreateClient();
            var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "underwriter@cover.example", password }) };
            login.Headers.Add("X-CSRF-Token", csrf); using var loggedIn = await client.SendAsync(login); loggedIn.EnsureSuccessStatusCode();
            csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            async Task<HttpResponseMessage> Write(HttpMethod method, string path, object input, string key, string? tag = null)
            {
                using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(input) };
                request.Headers.Add("X-CSRF-Token", csrf); request.Headers.Add("Idempotency-Key", key);
                if (tag is not null) request.Headers.Add("If-Match", tag);
                return await client.SendAsync(request);
            }
            var create = new { relationshipId = fixture.Relationship, productVersionId = fixture.ProductVersion };
            using var created = await Write(HttpMethod.Post, "/api/v1/quotes", create, "commercial-http-create-1");
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var path = $"/api/v1/quotes/{id:D}"; var firstTag = created.Headers.ETag!.ToString();
            using var initial = await client.GetAsync(path); initial.EnsureSuccessStatusCode(); Assert.True(initial.Headers.CacheControl!.NoStore);
            var view = await initial.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(CommercialCaptureRules.Format, view.GetProperty("proposal").GetProperty("format").GetString());
            Assert.False(view.GetProperty("capabilities").GetProperty("canAttachEvidence").GetBoolean());
            Assert.Contains(view.GetProperty("readiness").GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "commercial-question-required");
            var proposal = JsonSerializer.Deserialize<JsonElement>("""{"schemaVersion":"1.0","format":"commercial-combined-capture-1","productCode":"commercial-combined","termIntent":{"kind":"annual","localStartDate":"2026-09-16","localStartTime":"00:00","timeZone":"Europe/London","utcOffsetMinutes":60},"risk":{"materialFacts":"Saved CC HTTP facts"}}""");
            var save = new { proposal, reason = "Fictional HTTP capture" };
            using var saved = await Write(HttpMethod.Put, path + "/proposal", save, "commercial-http-save-1", firstTag);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            using var replay = await Write(HttpMethod.Put, path + "/proposal", save, "commercial-http-save-1", firstTag);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(saved.Headers.ETag, replay.Headers.ETag);
            using var stale = await Write(HttpMethod.Put, path + "/proposal", save, "commercial-http-stale-1", firstTag);
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
            using var loaded = await client.GetAsync(path); loaded.EnsureSuccessStatusCode();
            var loadedView = await loaded.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Saved CC HTTP facts", loadedView.GetProperty("proposal").GetProperty("risk").GetProperty("materialFacts").GetString());
            using var forged = await Write(HttpMethod.Post, "/api/v1/quotes", new { create.relationshipId, create.productVersionId, clientId = Guid.NewGuid(), agencyId = Guid.NewGuid() }, "commercial-http-forged-1");
            Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
            using var discovered = await client.GetAsync("/api/v1/quotes?productCode=commercial-combined"); discovered.EnsureSuccessStatusCode();
            var discovery = await discovered.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(discovery.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
            using var underwriting = await client.GetAsync(path + "/underwriting"); underwriting.EnsureSuccessStatusCode();
            var assessment = await underwriting.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(assessment.GetProperty("capabilities").GetProperty("canRate").GetBoolean());
            Assert.Contains(assessment.GetProperty("blockers").EnumerateArray(), x => x.GetProperty("code").GetString() == "underwriting-product-unavailable");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAccount SET IdentityState=N'inactive' WHERE Id={fixture.Client}");
            using var revoked = await Write(HttpMethod.Put, path + "/proposal", save, "commercial-http-save-1", firstTag);
            Assert.Equal(HttpStatusCode.Conflict, revoked.StatusCode);
            Assert.Equal(2, await db.Set<QuoteRevision>().CountAsync(x => x.QuoteId == id));
        });
    }

    [Fact]
    public async Task RealSqlCommercialCaptureInitializationIsAdditiveAndDoesNotGrantAgencyTerms()
    {
        await WithDatabase(async (db, password) =>
        {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true);
            var fixture = await CreateFixture(db);
            var oldVersions = await db.Set<ProductVersion>().AsNoTracking().ToArrayAsync();
            var oldSettings = await db.Set<SettingVersion>().AsNoTracking().ToArrayAsync();
            var oldTerms = await db.Set<AgencyTermsVersion>().AsNoTracking().ToArrayAsync();
            var grants = await db.Set<AgencyProduct>().AsNoTracking().CountAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => CommercialCaptureSeed.SeedAsync(db));
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeCommercialCapture: true);
            var product = await db.Set<Product>().SingleAsync(x => x.Code == CommercialCaptureRules.ProductCode);
            var added = await db.Set<ProductVersion>().AsNoTracking().SingleAsync(x => x.ProductId == product.Id && x.Version == 2);
            Assert.Equal("published", added.State); Assert.Equal(CommercialCaptureRules.QuestionVersion, added.QuestionSetVersion);
            var current = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope == "quote-capture").OrderByDescending(x => x.Version).FirstAsync();
            var configured = QuoteCaptureConfiguration.Parse(current.Values)!;
            Assert.Equal(3, configured.Count); Assert.Equal(CommercialCaptureRules.ReferenceVersion, configured[added.Id].ReferenceVersion);
            Assert.Equal(grants, await db.Set<AgencyProduct>().CountAsync());
            foreach (var prior in oldVersions)
            {
                var retained = await db.Set<ProductVersion>().AsNoTracking().SingleAsync(x => x.Id == prior.Id);
                Assert.Equal(prior.Definition, retained.Definition); Assert.Equal(prior.RowVersion, retained.RowVersion);
            }
            foreach (var prior in oldSettings) Assert.Equal(prior.Values, (await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Id == prior.Id)).Values);
            foreach (var prior in oldTerms) Assert.Equal(prior.Snapshot, (await db.Set<AgencyTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == prior.Id)).Snapshot);
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeCommercialCapture: true);
            Assert.Equal(oldVersions.Length + 1, await db.Set<ProductVersion>().CountAsync());
            Assert.Equal(current.Id, (await db.Set<SettingVersion>().Where(x => x.Scope == "quote-capture").OrderByDescending(x => x.Version).FirstAsync()).Id);
            const string revoked = "{\"demo\":true,\"kind\":\"quote-capture\",\"products\":[]}";
            db.Add(new SettingVersion { Scope = "quote-capture", Version = current.Version + 1, EffectiveFrom = current.EffectiveFrom, Values = revoked });
            await db.SaveChangesAsync();
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeCommercialCapture: true);
            Assert.Equal(revoked, (await db.Set<SettingVersion>().Where(x => x.Scope == "quote-capture").OrderByDescending(x => x.Version).FirstAsync()).Values);
            Assert.Empty(await db.Set<Quote>().ToArrayAsync());
            Assert.Equal(grants, await db.Set<AgencyProduct>().CountAsync());
        });
    }

    [Theory]
    [InlineData("quote-capture", "{\"demo\":true,\"kind\":\"quote-capture\",\"products\":[]}")]
    [InlineData("quote-capture", "{}")]
    [InlineData("agency-distribution", "{\"demo\":true,\"kind\":\"agency-distribution\",\"productVersionIds\":[]}")]
    public async Task RealSqlCommercialCaptureInitializationRespectsExistingOperatorRevocation(string scope, string values)
    {
        await WithDatabase(async (db, password) =>
        {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true);
            var previous = await db.Set<SettingVersion>().Where(x => x.Scope == scope).OrderByDescending(x => x.Version).FirstAsync();
            var revoked = new SettingVersion { Scope = scope, Version = previous.Version + 1, EffectiveFrom = previous.EffectiveFrom, Values = values };
            db.Add(revoked); await db.SaveChangesAsync();
            var captureCount = await db.Set<SettingVersion>().CountAsync(x => x.Scope == "quote-capture");
            var distributionCount = await db.Set<SettingVersion>().CountAsync(x => x.Scope == "agency-distribution");
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeCommercialCapture: true);
            Assert.Equal(captureCount, await db.Set<SettingVersion>().CountAsync(x => x.Scope == "quote-capture"));
            Assert.Equal(distributionCount, await db.Set<SettingVersion>().CountAsync(x => x.Scope == "agency-distribution"));
            Assert.Equal(values, (await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Id == revoked.Id)).Values);
            Assert.Single(await db.Set<SettingVersion>().Where(x => x.Scope == CommercialCaptureSeed.MarkerScope).ToArrayAsync());
        });
    }

    [Fact]
    public async Task RealSqlCommercialCapturePersistsRevisionsRetriesStaleWritesAndRevocation()
    {
        await WithDatabase(async (db, _) =>
        {
            var product = await db.Set<Product>().SingleAsync(x => x.Code == CommercialCaptureRules.ProductCode);
            var original = await db.Set<ProductVersion>().SingleAsync(x => x.ProductId == product.Id && x.Version == 1);
            var version = new ProductVersion { ProductId = product.Id, ProviderId = original.ProviderId, Version = 2, State = "published",
                EffectiveFrom = original.EffectiveFrom, JsonSchemaVersion = "1.0", QuestionSetVersion = CommercialCaptureRules.QuestionVersion,
                Definition = "{\"demo\":true,\"kind\":\"commercial-combined-capture\",\"ratingAvailable\":false}" };
            db.Add(version); await db.SaveChangesAsync();
            var fixture = await CreateFixture(db, "-CC", CommercialCaptureRules.ProductCode, 2);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            db.Add(new SettingVersion { Scope = "quote-capture", Version = 1, EffectiveFrom = original.EffectiveFrom,
                Values = JsonSerializer.Serialize(new { demo = true, kind = "quote-capture", products = new[] { new { productVersionId = version.Id,
                    schemaVersion = "1.0", questionSetVersion = CommercialCaptureRules.QuestionVersion, referenceVersion = CommercialCaptureRules.ReferenceVersion } } }) });
            db.Add(new SettingVersion { Scope = "agency-distribution", Version = 2, EffectiveFrom = original.EffectiveFrom,
                Values = JsonSerializer.Serialize(new { demo = true, kind = "agency-distribution", productVersionIds = new[] { version.Id } }) });
            await db.SaveChangesAsync();
            var user = await db.Set<StaffUser>().SingleAsync(x => x.Email == "underwriter@cover.example");
            var actor = new ActorContext(user.Id, user.TeamId, null, new HashSet<string> { "underwriter" });
            var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options;
            var factory = new QuoteFactory(options); var service = new QuoteService(factory, new QuoteTime());
            var offer = Assert.Single(await new QuoteProducts(factory, new QuoteTime()).ListAsync(actor, fixture.Relationship));
            Assert.True(offer.CaptureEligible); Assert.Equal(CommercialCaptureRules.ProductCode, offer.ProductCode);
            const string createKey = "commercial-create-retry-1";
            var created = await service.CreateAsync(actor, fixture.Relationship, version.Id, null, createKey, Guid.NewGuid());
            var initial = await service.GetAsync(actor, created.ResourceId);
            Assert.True(initial.CanSave); Assert.Equal(CommercialCaptureRules.QuestionVersion, initial.Revision.QuestionSetVersion);
            var replay = await service.CreateAsync(actor, fixture.Relationship, version.Id, null, createKey, Guid.NewGuid());
            Assert.True(replay.Replayed); Assert.Equal(created.ResourceId, replay.ResourceId);
            const string proposal = """{"schemaVersion":"1.0","format":"commercial-combined-capture-1","productCode":"commercial-combined","risk":{"locations":[{"id":"00000000-0000-4000-8000-000000000001","buildings":"12.34"}]}}""";
            var saved = await service.SaveAsync(actor, created.ResourceId, initial.Quote.RowVersion, proposal, "Fictional location capture", "commercial-save-retry-1", Guid.NewGuid());
            var loaded = await service.GetAsync(actor, created.ResourceId);
            Assert.Equal(2, loaded.Revision.Number); Assert.Equal(CommercialCaptureRules.ProductCode, loaded.ProductCode);
            using var json = JsonDocument.Parse(loaded.Revision.ProposalJson);
            Assert.Equal("12.34", json.RootElement.GetProperty("risk").GetProperty("locations")[0].GetProperty("buildings").GetString());
            Assert.Empty(await db.Set<QuoteRegistration>().Where(x => x.QuoteId == created.ResourceId).ToArrayAsync());
            var saveReplay = await service.SaveAsync(actor, created.ResourceId, initial.Quote.RowVersion, proposal, "Fictional location capture", "commercial-save-retry-1", Guid.NewGuid());
            Assert.True(saveReplay.Replayed); Assert.Equal(saved.ResourceId, saveReplay.ResourceId);
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SaveAsync(actor, created.ResourceId, initial.Quote.RowVersion, proposal, "Stale revision", "commercial-stale-save-1", Guid.NewGuid()))).Status);
            const string motor = """{"schemaVersion":"1.0","productCode":"motor-trade-road-risks","risk":{"vehicles":[]}}""";
            await Assert.ThrowsAsync<QuoteValidationException>(() => service.SaveAsync(actor, created.ResourceId, loaded.Quote.RowVersion, motor, "Wrong product", "commercial-motor-save-1", Guid.NewGuid()));
            const string foreign = """{"schemaVersion":"1.0","format":"commercial-combined-capture-1","productCode":"commercial-combined","risk":{"losses":[{"id":"00000000-0000-4000-8000-000000000002","riskItemId":"00000000-0000-4000-8000-000000000099"}]}}""";
            Assert.Contains((await Assert.ThrowsAsync<QuoteValidationException>(() => service.SaveAsync(actor, created.ResourceId, loaded.Quote.RowVersion, foreign, "Foreign location", "commercial-foreign-save-1", Guid.NewGuid()))).Issues, x => x.Code == "loss-location-not-owned");
            Assert.Equal(2, await db.Set<QuoteRevision>().CountAsync(x => x.QuoteId == created.ResourceId));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={user.Id}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.CreateAsync(actor, fixture.Relationship, version.Id, null, createKey, Guid.NewGuid()))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SaveAsync(actor, created.ResourceId, initial.Quote.RowVersion, proposal, "Fictional location capture", "commercial-save-retry-1", Guid.NewGuid()))).Status);
            Assert.Equal(2, await db.Set<QuoteRevision>().CountAsync(x => x.QuoteId == created.ResourceId));
        });
    }
}
