using System.Net;
using System.Net.Http.Headers;
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
    public async Task RealSqlQuoteEvidenceApiChecksMultipartScopeDownloadsAndVersionedAttestations()
    {
        await WithDatabase(async (db, password) =>
        {
            var fixture = await CreateFixture(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var transaction = await db.Database.BeginTransactionAsync())
            { await QuoteCaptureDemoSeed.SeedAsync(db); await transaction.CommitAsync(); }
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString())
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "evidence-api-test-keys", db.Database.GetDbConnection().Database)))
                .ConfigureServices(services =>
                {
                    services.AddScoped(provider => new QuoteService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new QuoteTime()));
                    services.AddScoped(provider => new QuoteEvidenceService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new QuoteTime()));
                }));
            using var client = host.CreateClient();
            var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "underwriter@cover.example", password }) })
            { login.Headers.Add("X-CSRF-Token", csrf); using var response = await client.SendAsync(login); response.EnsureSuccessStatusCode(); }
            csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            async Task<HttpResponseMessage> Write(string path, HttpContent content, string? etag = null, string? key = null, bool csrfEnabled = true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
                if (csrfEnabled) request.Headers.Add("X-CSRF-Token", csrf);
                request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
                if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
                return await client.SendAsync(request);
            }
            MultipartFormDataContent File(byte[] bytes, string name = "proof.txt", string media = "text/plain", bool extra = false)
            {
                var form = new MultipartFormDataContent(); var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(media);
                form.Add(file, "file", name); form.Add(new StringContent(name), "fileName"); form.Add(new StringContent(media), "contentType");
                if (extra) form.Add(new StringContent("true"), "verified"); return form;
            }
            async Task Problem(HttpResponseMessage response, int status, string code)
            {
                using (response)
                {
                    Assert.Equal(status, (int)response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
                    Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
                }
            }
            using var created = await Write("/api/v1/quotes", JsonContent.Create(new { relationshipId = fixture.Relationship, productVersionId = fixture.ProductVersion }));
            created.EnsureSuccessStatusCode(); var quoteId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var route = $"/api/v1/quotes/{quoteId:D}"; var quoteEtag = created.Headers.ETag!.ToString();
            var bytes = Encoding.UTF8.GetBytes("Fictional trade evidence for business demo.\r\n");
            await Problem(await Write(route + "/evidence-files", File(bytes), quoteEtag, csrfEnabled: false), 403, "csrf-invalid");
            await Problem(await Write(route + "/evidence-files", File(bytes)), 428, "version-required");
            await Problem(await Write(route + "/evidence-files", File(bytes, extra: true), quoteEtag), 422, "evidence-upload-fields");
            await Problem(await Write(route + "/evidence-files", File(bytes, "../proof.txt"), quoteEtag), 422, "evidence-file-name");
            await Problem(await Write(route + "/evidence-files", File(bytes, "proof.pdf", "application/pdf"), quoteEtag), 422, "evidence-file-type");
            await Problem(await Write(route + "/evidence-files", File(new byte[QuoteEvidenceRules.MaximumFileBytes + 1]), quoteEtag), 413, "evidence-file-size");
            using var uploaded = await Write(route + "/evidence-files", File(bytes), quoteEtag, "evidence-api-upload-01");
            Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode); Assert.Equal(quoteEtag, uploaded.Headers.ETag!.ToString());
            var fileId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var replay = await Write(route + "/evidence-files", File(bytes), quoteEtag, "evidence-api-upload-01");
            Assert.Equal(fileId, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            using var download = await client.GetAsync(uploaded.Headers.Location); download.EnsureSuccessStatusCode();
            Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync()); Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
            Assert.Equal("text/plain", download.Content.Headers.ContentType!.MediaType); Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.True(download.Headers.CacheControl!.NoStore);
            var files = await client.GetFromJsonAsync<JsonElement>(route + "/evidence-files"); Assert.Single(files.GetProperty("items").EnumerateArray());
            Assert.DoesNotContain(Convert.ToBase64String(bytes), files.GetRawText());
            var before = await client.GetFromJsonAsync<JsonElement>(route + "/evidence"); var requirement = before.GetProperty("requirements")[0];
            var revisionId = before.GetProperty("revisionId").GetGuid();
            var missing = await client.GetFromJsonAsync<JsonElement>(route + "/readiness");
            Assert.Contains(missing.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "evidence-missing-motor-trader-proof");
            var body = new { revisionId, fileId, requirementCode = requirement.GetProperty("code").GetString(), inputFingerprint = requirement.GetProperty("inputFingerprint").GetString(), reason = "Reviewed fictional proof" };
            using var attached = await Write(route + "/evidence", JsonContent.Create(body), quoteEtag, "evidence-api-attach-01");
            Assert.Equal(HttpStatusCode.Created, attached.StatusCode); var evidenceEtag = attached.Headers.ETag!.ToString();
            var evidenceId = (await attached.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var item = await client.GetAsync(attached.Headers.Location); item.EnsureSuccessStatusCode(); Assert.Equal(evidenceEtag, item.Headers.ETag!.ToString());
            Assert.Equal("current", (await item.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
            var satisfied = await client.GetFromJsonAsync<JsonElement>(route + "/readiness");
            Assert.False(satisfied.GetProperty("ready").GetBoolean());
            Assert.DoesNotContain(satisfied.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "evidence-missing-motor-trader-proof");
            await Problem(await Write(route + "/evidence", JsonContent.Create(new { revisionId, fileId, verified = true }), quoteEtag), 400, "unknown-field");
            await Problem(await client.GetAsync(route + "/evidence?revisionId=" + Guid.NewGuid()), 404, "evidence-revision-not-found");
            using var withdrawn = await Write(route + $"/evidence/{evidenceId}/withdraw", JsonContent.Create(new { reason = "Replace fictional proof" }), evidenceEtag, "evidence-api-withdraw-01");
            Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
            Assert.Equal("withdrawn", (await client.GetFromJsonAsync<JsonElement>(attached.Headers.Location)).GetProperty("state").GetString());
            var restored = await client.GetFromJsonAsync<JsonElement>(route + "/readiness");
            Assert.Contains(restored.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "evidence-missing-motor-trader-proof");
            using var anonymous = host.CreateClient(); using var forbidden = await anonymous.GetAsync(uploaded.Headers.Location); Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
            var user = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM UserRole WHERE UserId={user}");
            using var revokedDownload = await client.GetAsync(uploaded.Headers.Location); Assert.False(revokedDownload.IsSuccessStatusCode);
            using var revokedReplay = await Write(route + "/evidence", JsonContent.Create(body), quoteEtag, "evidence-api-attach-01"); Assert.False(revokedReplay.IsSuccessStatusCode);
        });
    }
}
