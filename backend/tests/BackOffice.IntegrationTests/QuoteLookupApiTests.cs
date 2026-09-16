using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
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
    public async Task RealSqlLookupApiScopesResultsValidatesInputsAndSelectsWithCurrentAuthority()
    {
        await WithDatabase(async (db, password) =>
        {
            var fixture = await CreateFixture(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await QuoteCaptureDemoSeed.SeedAsync(db); await QuoteLookupDemoSeed.SeedAsync(db);
                await QuoteLookupDemoSeed.SeedAsync(db); await transaction.CommitAsync();
            }
            Assert.Equal(6, await db.Set<SettingVersion>().CountAsync(x => x.Scope.StartsWith("quote-lookup/")));
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString())
                .UseSetting("Cover:QuoteLookupWorkerEnabled", "false")
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "lookup-api-test-keys", db.Database.GetDbConnection().Database)))
                .ConfigureServices(services =>
                {
                    services.AddScoped(provider => new QuoteService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new QuoteTime()));
                    services.AddScoped(provider => new QuoteLookupService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new QuoteTime()));
                }));
            using var client = host.CreateClient();
            var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "underwriter@cover.example", password }) })
            {
                login.Headers.Add("X-CSRF-Token", csrf);
                using var response = await client.SendAsync(login); response.EnsureSuccessStatusCode();
            }
            csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            async Task<HttpResponseMessage> Write(string path, object body, string? version = null, bool useCsrf = true, string? key = null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body is string raw
                    ? new StringContent(raw, Encoding.UTF8, "application/json") : JsonContent.Create(body) };
                request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
                if (useCsrf) request.Headers.Add("X-CSRF-Token", csrf);
                if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", version);
                return await client.SendAsync(request);
            }
            async Task Problem(HttpResponseMessage response, int status, string code)
            {
                using (response)
                {
                    Assert.Equal(status, (int)response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
                    Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
                }
            }
            using var created = await Write("/api/v1/quotes", new { relationshipId = fixture.Relationship, productVersionId = fixture.ProductVersion,
                proposal = new { schemaVersion = "1.0", productCode = "motor-trade-road-risks", insured = new { address = new { postcode = "AB1 2CD" } } } });
            created.EnsureSuccessStatusCode();
            var quoteId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var route = $"/api/v1/quotes/{quoteId:D}"; var etag = created.Headers.ETag!.ToString();
            var quote = await client.GetFromJsonAsync<JsonElement>(route); var revisionId = quote.GetProperty("revisionId").GetGuid();
            var input = new { revisionId, kind = "address", scope = "insured", scenario = "multiple" };
            await Problem(await Write(route + "/lookups", input, etag, false), 403, "csrf-invalid");
            await Problem(await Write(route + "/lookups", input), 428, "version-required");
            await Problem(await Write(route + "/lookups", new { revisionId, kind = "address", scope = "insured", scenario = "multiple", query = "forged" }, etag), 400, "unknown-field");
            await Problem(await Write(route + "/lookups", "{\"revisionId\":null}", etag), 422, "null-field");
            using var requested = await Write(route + "/lookups", input, etag, key: "lookup-http-request-01");
            Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode); Assert.Equal(etag, requested.Headers.ETag!.ToString());
            var lookupId = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            Assert.Equal(route + "/lookups/" + lookupId, requested.Headers.Location!.ToString());
            var pending = await client.GetFromJsonAsync<JsonElement>(requested.Headers.Location);
            Assert.Equal("pending", pending.GetProperty("state").GetString()); Assert.Empty(pending.GetProperty("candidates").EnumerateArray());
            Assert.False(pending.TryGetProperty("query", out _)); Assert.False(pending.TryGetProperty("resultJson", out _));
            var factory = host.Services.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(); var clock = new QuoteTime();
            var leases = new SqlJobLeases(factory, clock); var worker = new QuoteLookupWorker(factory, clock);
            var workId = await db.Set<QuoteLookup>().Where(x => x.Id == lookupId).Select(x => x.WorkId).SingleAsync();
            var lease = (await leases.ClaimWorkAsync(QuoteLookupService.WorkKind, workId))!;
            Assert.True(await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease)));
            using var loaded = await client.GetAsync(requested.Headers.Location); loaded.EnsureSuccessStatusCode();
            Assert.True(loaded.Headers.CacheControl!.NoStore);
            var completed = await loaded.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("succeeded", completed.GetProperty("state").GetString());
            Assert.Equal(2, completed.GetProperty("candidates").GetArrayLength()); Assert.Equal(1, completed.GetProperty("attempts").GetInt32());
            var list = await client.GetFromJsonAsync<JsonElement>(route + "/lookups"); Assert.Single(list.GetProperty("items").EnumerateArray());
            using var anonymous = host.CreateClient(); using var denied = await anonymous.GetAsync(requested.Headers.Location);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            await Problem(await client.GetAsync(route + "/lookups/" + Guid.NewGuid()), 404, "lookup-not-found");
            using var generic = await client.GetAsync("/api/v1/jobs/" + workId); Assert.False(generic.IsSuccessStatusCode);
            var selection = new { lookupId, revisionId, inputFingerprint = completed.GetProperty("inputFingerprint").GetString(), candidateId = completed.GetProperty("candidates")[0].GetProperty("id").GetGuid() };
            using var selected = await Write(route + "/lookup-selections", selection, etag, key: "lookup-http-selection-01");
            Assert.Equal(HttpStatusCode.OK, selected.StatusCode); Assert.NotEqual(etag, selected.Headers.ETag!.ToString());
            using var replay = await Write(route + "/lookup-selections", selection, etag, key: "lookup-http-selection-01");
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(selected.Headers.ETag!.ToString(), replay.Headers.ETag!.ToString());
            await Problem(await Write(route + "/lookup-selections", selection, etag), 412, "stale-lookup-input");
            var updated = await client.GetFromJsonAsync<JsonElement>(route);
            Assert.Equal(2, updated.GetProperty("revisionNumber").GetInt32());
            Assert.Equal("Fictional Demo Street", updated.GetProperty("proposal").GetProperty("insured").GetProperty("address").GetProperty("street").GetString());
            var selectedView = await client.GetFromJsonAsync<JsonElement>(requested.Headers.Location);
            Assert.Equal(updated.GetProperty("revisionId").GetGuid(), selectedView.GetProperty("selectedRevisionId").GetGuid());
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={fixture.Agency}");
            using var revoked = await Write(route + "/lookup-selections", selection, etag, key: "lookup-http-selection-01");
            Assert.False(revoked.IsSuccessStatusCode);
        });
    }
}
