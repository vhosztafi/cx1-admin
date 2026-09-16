using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlQuoteProductsUseCurrentHeldCatalogueAndRelationshipEligibility()
    {
        await WithDatabase(async (db, password) =>
        {
            var fixture = await CreateFixture(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            var userId = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var actor = new ActorContext(userId, null, null, new HashSet<string> { "underwriter" });
            var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options;
            var service = new QuoteProducts(new QuoteFactory(options), new QuoteTime());
            Assert.Equal(503, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ListAsync(actor, fixture.Relationship))).Status);
            await using (var transaction = await db.Database.BeginTransactionAsync())
            { await QuoteCaptureDemoSeed.SeedAsync(db); await transaction.CommitAsync(); }
            var initial = await service.ListAsync(actor, fixture.Relationship);
            Assert.Equal(new[] { "motor-trade-combined", "motor-trade-road-risks" }, initial.Select(x => x.ProductCode));
            var offered = Assert.Single(initial, x => x.CaptureEligible); Assert.Equal(fixture.ProductVersion, offered.ProductVersionId);
            Assert.Null(offered.UnavailableReason); Assert.Equal("v1", offered.VersionLabel);
            Assert.All(initial, x => { Assert.Equal(QuoteCatalogueIdentity.Version, x.QuestionSetVersion); Assert.Equal(QuoteCatalogueIdentity.Version, x.ReferenceDataVersion); });
            Assert.False(string.IsNullOrEmpty(Assert.Single(initial, x => !x.CaptureEligible).UnavailableReason));
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString())
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "quote-product-test-keys", db.Database.GetDbConnection().Database)))
                .ConfigureServices(services => services.AddScoped(provider => new QuoteProducts(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new QuoteTime()))));
            using var client = host.CreateClient();
            var route = $"/api/v1/quote-products?relationshipId={fixture.Relationship:D}";
            using var anonymous = await client.GetAsync(route); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "underwriter@cover.example", password }) };
            login.Headers.Add("X-CSRF-Token", csrf); using var loggedIn = await client.SendAsync(login); loggedIn.EnsureSuccessStatusCode();
            using var response = await client.GetAsync(route); response.EnsureSuccessStatusCode(); Assert.True(response.Headers.CacheControl!.NoStore);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(); var items = body.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(2, items.Length);
            var eligible = Assert.Single(items, x => x.GetProperty("captureEligible").GetBoolean());
            Assert.False(eligible.TryGetProperty("unavailableReason", out var absent));
            Assert.Equal(fixture.ProductVersion, eligible.GetProperty("productVersionId").GetGuid());
            Assert.False(eligible.TryGetProperty("definition", out var definition));
            using var badQuery = await client.GetAsync("/api/v1/quote-products"); Assert.Equal(HttpStatusCode.BadRequest, badQuery.StatusCode);
            using var missing = await client.GetAsync($"/api/v1/quote-products?relationshipId={Guid.NewGuid():D}"); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={fixture.Agency}");
            Assert.All(await service.ListAsync(actor, fixture.Relationship), x => { Assert.False(x.CaptureEligible); Assert.Equal("The agency is not active.", x.UnavailableReason); });
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAgencyRelationship SET State=N'inactive' WHERE Id={fixture.Relationship}");
            Assert.All(await service.ListAsync(actor, fixture.Relationship), x => Assert.False(x.CaptureEligible));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAgencyRelationship SET State=N'active' WHERE Id={fixture.Relationship}");
            var providerId = await db.Set<ProductVersion>().Where(x => x.Id == fixture.ProductVersion).Select(x => x.ProviderId).SingleAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CapacityProvider SET State=N'inactive' WHERE Id={providerId}");
            Assert.False((await service.ListAsync(actor, fixture.Relationship)).Single(x => x.ProductVersionId == fixture.ProductVersion).CaptureEligible);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CapacityProvider SET State=N'active' WHERE Id={providerId}");
            var configured = await db.Set<SettingVersion>().Where(x => x.Scope == "quote-capture").Select(x => x.Values).SingleAsync();
            async Task Publish(string scope, string values, DateTimeOffset? effective = null)
            {
                var version = await db.Set<SettingVersion>().Where(x => x.Scope == scope).MaxAsync(x => x.Version) + 1;
                db.Add(new SettingVersion { Scope = scope, Version = version, Values = values, EffectiveFrom = effective ?? new QuoteTime().GetUtcNow() });
                await db.SaveChangesAsync();
            }
            await Publish("quote-capture", "{}", new QuoteTime().GetUtcNow().AddDays(1));
            Assert.Single(await service.ListAsync(actor, fixture.Relationship), x => x.CaptureEligible); // Future publication is not current.
            await Publish("agency-distribution", "{\"demo\":true,\"kind\":\"agency-distribution\",\"productVersionIds\":[]}");
            var revokedDistribution = await service.ListAsync(actor, fixture.Relationship); Assert.Equal(2, revokedDistribution.Count); Assert.All(revokedDistribution, x => Assert.False(x.CaptureEligible));
            await Publish("quote-capture", "{\"demo\":true,\"kind\":\"quote-capture\",\"products\":[]}");
            Assert.Empty(await service.ListAsync(actor, fixture.Relationship));
            await Publish("quote-capture", "{}");
            using var broken = await client.GetAsync(route); Assert.Equal(HttpStatusCode.ServiceUnavailable, broken.StatusCode);
            Assert.Equal("quote-capture-configuration-unavailable", (await broken.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            await Publish("quote-capture", configured.Replace(fixture.ProductVersion.ToString(), Guid.NewGuid().ToString(), StringComparison.OrdinalIgnoreCase));
            Assert.Equal(503, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ListAsync(actor, fixture.Relationship))).Status);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={userId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ListAsync(actor, fixture.Relationship))).Status);
            Assert.Empty(await db.Set<Quote>().ToListAsync()); Assert.Empty(await db.Set<IdempotencyRecord>().ToListAsync());
        });
    }
}
