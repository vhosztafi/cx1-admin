using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task RealSqlQuoteLifecycleApiProtectsHistoryCursorsCloneAndTerminalWithdrawal()
    {
        await WithDatabase(async (db, password) =>
        {
            var fixture = await CreateFixture(db); await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var tx = await db.Database.BeginTransactionAsync()) { await QuoteCaptureDemoSeed.SeedAsync(db); await tx.CommitAsync(); }
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString())
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "lifecycle-api-test-keys", db.Database.GetDbConnection().Database)))
                .ConfigureServices(services =>
                {
                    services.AddScoped(provider => new QuoteService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new QuoteTime()));
                    services.AddScoped(provider => new QuoteLifecycleService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new QuoteTime()));
                }));
            using var client = host.CreateClient();
            var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "underwriter@cover.example", password }) })
            { login.Headers.Add("X-CSRF-Token", csrf); using var response = await client.SendAsync(login); response.EnsureSuccessStatusCode(); }
            csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            async Task<HttpResponseMessage> Write(string route, object body, string? etag = null, string? key = null, bool put = false, bool withCsrf = true)
            {
                using var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, route) { Content = JsonContent.Create(body) };
                request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString()); if (withCsrf) request.Headers.Add("X-CSRF-Token", csrf);
                if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag); return await client.SendAsync(request);
            }
            using var create = await Write("/api/v1/quotes", new { relationshipId = fixture.Relationship, productVersionId = fixture.ProductVersion });
            create.EnsureSuccessStatusCode(); var quoteId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var route = $"/api/v1/quotes/{quoteId:D}"; var etag = create.Headers.ETag!.ToString();
            var first = await client.GetFromJsonAsync<JsonElement>(route); var firstRevision = first.GetProperty("revisionId").GetGuid();
            var proposal = new { schemaVersion = "1.0", productCode = "motor-trade-road-risks", risk = new { materialFacts = "Fictional facts" }, insured = new { legalName = "Fictional proposer" } };
            using var save = await Write(route + "/proposal", new { proposal, reason = "Fictional history change" }, etag, put: true); save.EnsureSuccessStatusCode(); etag = save.Headers.ETag!.ToString();
            var second = await client.GetFromJsonAsync<JsonElement>(route); var secondRevision = second.GetProperty("revisionId").GetGuid();
            using var historyResponse = await client.GetAsync(route + "/revisions?pageSize=1"); historyResponse.EnsureSuccessStatusCode(); Assert.True(historyResponse.Headers.CacheControl!.NoStore);
            var history = await historyResponse.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(2, history.GetProperty("totalCount").GetInt32());
            Assert.Equal(secondRevision, history.GetProperty("items")[0].GetProperty("id").GetGuid());
            var cursor = history.GetProperty("nextCursor").GetString()!;
            var next = await client.GetFromJsonAsync<JsonElement>(route + "/revisions?pageSize=1&cursor=" + Uri.EscapeDataString(cursor));
            Assert.Equal(firstRevision, next.GetProperty("items")[0].GetProperty("id").GetGuid());
            using var badCursor = await client.GetAsync(route + "/revisions?pageSize=2&cursor=" + Uri.EscapeDataString(cursor)); Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
            var comparisonRoute = route + $"/compare?leftRevisionId={firstRevision}&rightRevisionId={secondRevision}&pageSize=1";
            var comparison = await client.GetFromJsonAsync<JsonElement>(comparisonRoute); Assert.Equal(2, comparison.GetProperty("totalChanges").GetInt32());
            Assert.Single(comparison.GetProperty("changes").EnumerateArray()); var compareCursor = comparison.GetProperty("nextCursor").GetString()!;
            var remaining = await client.GetFromJsonAsync<JsonElement>(comparisonRoute + "&cursor=" + Uri.EscapeDataString(compareCursor)); Assert.False(remaining.TryGetProperty("nextCursor", out _));
            using var swapped = await client.GetAsync(route + $"/compare?leftRevisionId={secondRevision}&rightRevisionId={firstRevision}&pageSize=1&cursor=" + Uri.EscapeDataString(compareCursor));
            Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
            using var foreign = await client.GetAsync(route + "/revisions/" + Guid.NewGuid()); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            var terms = await client.GetFromJsonAsync<JsonElement>(route + $"/clone-terms?sourceRevisionId={firstRevision}&relationshipId={fixture.Relationship}");
            Assert.False(terms.GetProperty("confirmationRequired").GetBoolean());
            var body = new { sourceRevisionId = firstRevision, relationshipId = fixture.Relationship, reason = "Fictional historical clone" };
            using var missingCsrf = await Write(route + "/clone", body, etag, withCsrf: false); Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
            using var missingVersion = await Write(route + "/clone", body); Assert.Equal(428, (int)missingVersion.StatusCode);
            using var forged = await Write(route + "/clone", new { body.sourceRevisionId, body.relationshipId, body.reason, ready = true }, etag); Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
            using var cloned = await Write(route + "/clone", body, etag, "lifecycle-api-clone-01"); Assert.Equal(HttpStatusCode.Created, cloned.StatusCode);
            var cloneId = (await cloned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); Assert.NotEqual(quoteId, cloneId);
            using var cloneRead = await client.GetAsync(cloned.Headers.Location); cloneRead.EnsureSuccessStatusCode();
            using var replay = await Write(route + "/clone", body, etag, "lifecycle-api-clone-01"); Assert.Equal(cloneId, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            using var withdrawn = await Write(route + "/withdraw", new { reason = "Fictional quote withdrawn" }, etag, "lifecycle-api-withdraw"); withdrawn.EnsureSuccessStatusCode();
            var closed = await client.GetFromJsonAsync<JsonElement>(route); Assert.Equal("withdrawn", closed.GetProperty("state").GetString());
            Assert.Equal("Fictional quote withdrawn", closed.GetProperty("captureClosedReason").GetString());
            Assert.Equal(JsonValueKind.String, closed.GetProperty("captureClosedAt").ValueKind);
            foreach (var property in closed.GetProperty("capabilities").EnumerateObject()) Assert.False(property.Value.GetBoolean());
            using var withdrawalReplay = await Write(route + "/withdraw", new { reason = "Fictional quote withdrawn" }, etag, "lifecycle-api-withdraw"); withdrawalReplay.EnsureSuccessStatusCode();
            using var closedSave = await Write(route + "/proposal", new { proposal }, withdrawn.Headers.ETag!.ToString(), put: true); Assert.False(closedSave.IsSuccessStatusCode);
            var retained = await client.GetFromJsonAsync<JsonElement>(route + "/revisions"); Assert.Equal(2, retained.GetProperty("totalCount").GetInt32());
            using var anonymous = host.CreateClient(); using var denied = await anonymous.GetAsync(route + "/revisions"); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            var user = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM UserRole WHERE UserId={user}");
            using var revoked = await Write(route + "/clone", body, etag, "lifecycle-api-clone-01"); Assert.False(revoked.IsSuccessStatusCode);
        });
    }
}
