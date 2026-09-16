using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
    public async Task RealSqlQuoteApiCapturesReloadsReplaysAndEnforcesCurrentAuthority()
    {
        await WithDatabase(async (db, password) =>
        {
            var fixture = await CreateFixture(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await QuoteCaptureDemoSeed.SeedAsync(db); await transaction.CommitAsync();
            }
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString())
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "quote-api-test-keys", db.Database.GetDbConnection().Database)))
                .ConfigureServices(services => services.AddScoped(provider => new QuoteService(
                    provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new QuoteTime()))));
            async Task<(HttpClient Client, string Csrf)> SignIn(string email)
            {
                var client = host.CreateClient();
                var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
                using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password }) };
                login.Headers.Add("X-CSRF-Token", csrf);
                using var response = await client.SendAsync(login); response.EnsureSuccessStatusCode();
                return (client, (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!);
            }
            using var anonymous = host.CreateClient();
            using var anonymousRead = await anonymous.GetAsync($"/api/v1/quotes/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
            var signedIn = await SignIn("underwriter@cover.example"); using var client = signedIn.Client;
            async Task<HttpResponseMessage> Write(HttpMethod method, string path, object body, string key, string? version = null, bool csrf = true)
            {
                using var request = new HttpRequestMessage(method, path)
                {
                    Content = body is string raw ? new StringContent(raw, Encoding.UTF8, "application/json") : JsonContent.Create(body)
                };
                request.Headers.Add("Idempotency-Key", key);
                if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", version);
                if (csrf) request.Headers.Add("X-CSRF-Token", signedIn.Csrf);
                return await client.SendAsync(request);
            }
            async Task Problem(HttpResponseMessage response, int status, string code)
            {
                using (response)
                {
                    Assert.Equal(status, (int)response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
                    Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
                    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                    Assert.Equal(code, body.GetProperty("code").GetString()); Assert.True(body.TryGetProperty("traceId", out _));
                }
            }
            var createInput = new { relationshipId = fixture.Relationship, productVersionId = fixture.ProductVersion };
            await Problem(await Write(HttpMethod.Post, "/api/v1/quotes", createInput, "api-no-csrf-000001", csrf: false), 403, "csrf-invalid");
            using var created = await Write(HttpMethod.Post, "/api/v1/quotes", createInput, "api-create-000001");
            Assert.Equal(HttpStatusCode.Created, created.StatusCode); Assert.True(created.Headers.CacheControl!.NoStore);
            var identity = await created.Content.ReadFromJsonAsync<JsonElement>(); Assert.Single(identity.EnumerateObject());
            var quoteId = identity.GetProperty("id").GetGuid(); var route = $"/api/v1/quotes/{quoteId:D}";
            Assert.Equal(route, created.Headers.Location!.ToString()); var firstTag = created.Headers.ETag!.ToString();
            using var replay = await Write(HttpMethod.Post, "/api/v1/quotes", createInput, "api-create-000001");
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Equal(firstTag, replay.Headers.ETag!.ToString());
            Assert.Equal(identity.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
            using var loaded = await client.GetAsync(route); loaded.EnsureSuccessStatusCode();
            Assert.Equal(firstTag, loaded.Headers.ETag!.ToString()); Assert.True(loaded.Headers.CacheControl!.NoStore);
            var view = await loaded.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(quoteId, view.GetProperty("id").GetGuid()); Assert.Equal(1, view.GetProperty("revisionNumber").GetInt32());
            Assert.Equal("Fictional quote client", view.GetProperty("clientName").GetString());
            Assert.Equal("Fictional quote storage", view.GetProperty("agencyName").GetString());
            Assert.True(view.GetProperty("capabilities").GetProperty("canSave").GetBoolean());
            foreach (var name in new[] { "canClone", "canWithdraw" }) Assert.False(view.GetProperty("capabilities").GetProperty(name).GetBoolean());
            Assert.False(view.GetProperty("readiness").GetProperty("ready").GetBoolean());
            var captureVersions = view.GetProperty("captureVersions");
            Assert.Equal("1.0", captureVersions.GetProperty("schemaVersion").GetString());
            Assert.Equal(QuoteCatalogueIdentity.Version, captureVersions.GetProperty("questionSetVersion").GetString());
            Assert.Equal(QuoteCatalogueIdentity.Version, captureVersions.GetProperty("referenceDataVersion").GetString());
            Assert.False(view.TryGetProperty("rowVersion", out _)); Assert.False(view.TryGetProperty("revision", out _));
            var proposal = view.GetProperty("proposal").Clone();
            using var noOp = await Write(HttpMethod.Put, route + "/proposal", new { proposal }, "api-no-op-0000001", firstTag);
            Assert.Equal(HttpStatusCode.OK, noOp.StatusCode); Assert.Equal(firstTag, noOp.Headers.ETag!.ToString());
            var assessmentDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
            var changed = new { schemaVersion = "1.0", productCode = "motor-trade-road-risks", termIntent = new { localStartDate = assessmentDate.AddYears(1).ToString("yyyy-MM-dd") },
                risk = new { drivers = new[] { new { id = Guid.NewGuid(), losses = new[] {
                    new { id = Guid.NewGuid(), occurredOn = assessmentDate.AddDays(-1).ToString("yyyy-MM-dd") }, new { id = Guid.NewGuid(), occurredOn = assessmentDate.AddDays(1).ToString("yyyy-MM-dd") }
                } } } } };
            using var saved = await Write(HttpMethod.Put, route + "/proposal", new { proposal = changed }, "api-save-00000001", firstTag);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode); var savedTag = saved.Headers.ETag!.ToString(); Assert.NotEqual(firstTag, savedTag);
            using var savedReplay = await Write(HttpMethod.Put, route + "/proposal", new { proposal = changed }, "api-save-00000001", firstTag);
            Assert.Equal(HttpStatusCode.OK, savedReplay.StatusCode); Assert.Equal(savedTag, savedReplay.Headers.ETag!.ToString());
            await Problem(await Write(HttpMethod.Put, route + "/proposal", new { proposal }, "api-save-00000001", firstTag), 409, "idempotency-conflict");
            await Problem(await Write(HttpMethod.Put, route + "/proposal", new { proposal }, "api-stale-0000001", firstTag), 412, "stale-quote");
            await Problem(await Write(HttpMethod.Put, route + "/proposal", new { proposal }, "api-no-etag-00001"), 428, "version-required");
            await Problem(await Write(HttpMethod.Put, route + "/proposal", new { proposal }, "api-weak-etag-001", "W/" + savedTag), 400, "invalid-version");
            await Problem(await client.GetAsync(route + "?unknown=true"), 400, "invalid-query");
            await Problem(await client.GetAsync($"/api/v1/quotes/{Guid.NewGuid()}"), 404, "quote-context-not-found");
            using var invalidShape = await Write(HttpMethod.Put, route + "/proposal", new { proposal = new { schemaVersion = "1.0", productCode = "commercial-combined" } }, "api-shape-0000001", savedTag);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidShape.StatusCode);
            var invalidBody = await invalidShape.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("quote-input-invalid", invalidBody.GetProperty("code").GetString());
            Assert.All(invalidBody.GetProperty("errors").EnumerateArray(), error => Assert.False(string.IsNullOrEmpty(error.GetProperty("message").GetString())));
            await Problem(await Write(HttpMethod.Post, "/api/v1/quotes", new { relationshipId = fixture.Relationship, productVersionId = fixture.ProductVersion, matchSubmissionId = Guid.NewGuid() }, "api-match-0000001"), 409, "quote-match-association-unavailable");
            using var malformed = await Write(HttpMethod.Put, route + "/proposal", "{\"proposal\":{},\"proposal\":{}}", "api-duplicate-001", savedTag);
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            await Problem(await Write(HttpMethod.Post, "/api/v1/quotes", new string(' ', 1024 * 1024 + 1), "api-oversized-001"), 413, "quote-request-too-large");
            using var wrongMedia = new HttpRequestMessage(HttpMethod.Post, "/api/v1/quotes") { Content = new StringContent("{}", Encoding.UTF8, "text/plain") };
            wrongMedia.Headers.Add("X-CSRF-Token", signedIn.Csrf); wrongMedia.Headers.Add("Idempotency-Key", "api-media-0000001");
            await Problem(await client.SendAsync(wrongMedia), 415, "json-required");
            using var readiness = await client.GetAsync(route + "/readiness"); readiness.EnsureSuccessStatusCode();
            var assessment = await readiness.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(quoteId, assessment.GetProperty("quoteId").GetGuid()); Assert.False(assessment.GetProperty("ready").GetBoolean());
            Assert.Contains(assessment.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "quote-assessment-unavailable");
            Assert.Contains(assessment.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "required-capture-field" && x.GetProperty("path").GetString() == "/insured/firstName");
            Assert.Contains(assessment.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "activity-split-share-required" && x.GetProperty("path").GetString() == "/risk/business/declaredActivitySplit/sales");
            Assert.Contains(assessment.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "business-description-required");
            Assert.Contains(assessment.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "proposer-name-required" && x.GetProperty("path").GetString() == "/insured/proposerNames");
            Assert.Contains(assessment.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "business-activity-required" && x.GetProperty("path").GetString() == "/risk/business/activities");
            foreach (var question in new[] { "MTS-03-Q04", "MTS-03-Q06" })
                Assert.Contains(assessment.GetProperty("issues").EnumerateArray(), x => x.GetProperty("path").GetString() == "/risk/business/responses/answers" &&
                    x.TryGetProperty("questionId", out var identity) && identity.GetString() == question);
            Assert.False(assessment.GetProperty("issues").EnumerateArray().Single(x => x.GetProperty("code").GetString() == "quote-assessment-unavailable").TryGetProperty("questionId", out _));
            Assert.Contains(assessment.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "history-declaration-required" &&
                x.GetProperty("relatedPath").GetString() == "/risk/drivers/0/losses/0");
            Assert.Contains(assessment.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "history-date-after-assessment" &&
                x.GetProperty("path").GetString() == "/risk/drivers/0/losses/1/occurredOn");
            Assert.Equal(2, await db.Set<QuoteRevision>().CountAsync()); Assert.Equal(3, await db.Set<IdempotencyRecord>().CountAsync());
            foreach (var email in new[] { "agency-admin@cover.example", "system-admin@cover.example" })
            {
                var denied = await SignIn(email); using var deniedClient = denied.Client;
                using var deniedRead = await deniedClient.GetAsync(route); Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
                using var deniedWrite = new HttpRequestMessage(HttpMethod.Post, "/api/v1/quotes") { Content = JsonContent.Create(createInput) };
                deniedWrite.Headers.Add("X-CSRF-Token", denied.Csrf); deniedWrite.Headers.Add("Idempotency-Key", "api-denied-000001");
                using var deniedResult = await deniedClient.SendAsync(deniedWrite); Assert.Equal(HttpStatusCode.Forbidden, deniedResult.StatusCode);
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={fixture.Agency}");
            await Problem(await Write(HttpMethod.Post, "/api/v1/quotes", createInput, "api-create-000001"), 409, "agency-unavailable");
            var historical = await client.GetFromJsonAsync<JsonElement>(route);
            Assert.Equal(2, historical.GetProperty("revisionNumber").GetInt32()); Assert.False(historical.GetProperty("capabilities").GetProperty("canSave").GetBoolean());
            Assert.Equal(captureVersions.GetRawText(), historical.GetProperty("captureVersions").GetRawText());
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET CaptureClosedAt={DateTimeOffset.UtcNow},CaptureClosedReason=N'Fictional progression' WHERE Id={quoteId}");
            var closed = await client.GetAsync(route); var closedTag = closed.Headers.ETag!.ToString(); closed.Dispose();
            await Problem(await Write(HttpMethod.Put, route + "/proposal", new { proposal }, "api-closed-00001", closedTag), 409, "quote-capture-closed");
        });
    }
}
