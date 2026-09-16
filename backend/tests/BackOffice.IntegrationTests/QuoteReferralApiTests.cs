using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlQuoteReferralApiEnforcesTransportRolesScopedHistoryAndActualProofReview()
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await ReadyUnderwriting(db, password, p => p["risk"]!["business"]!["startedOn"] = "2025-01-01");
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:QuoteRatingWorkerEnabled", "false")
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "underwriting-api-test-keys", db.Database.GetDbConnection().Database)))
                .ConfigureServices(services => {
                    services.AddScoped(provider => new UnderwritingEvidenceService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), f.Clock));
                    services.AddScoped(provider => new QuoteReferralService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), f.Clock));
                    services.AddScoped(provider => new QuoteUnderwritingReadModel(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), f.Clock));
                }));
            async Task<(HttpClient Client, string Csrf)> Login(string email)
            {
                var client = host.CreateClient(); var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password }) }; request.Headers.Add("X-CSRF-Token", token);
                using var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
                return (client, (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!);
            }
            var uw = await Login("underwriter@cover.example"); using var client = uw.Client;
            var servicing = await Login("servicing@cover.example"); using var serviceClient = servicing.Client;
            var quoteRoute = $"/api/v1/quotes/{f.QuoteId:D}";
            async Task<JsonElement> Assessment() => await client.GetFromJsonAsync<JsonElement>(quoteRoute + "/underwriting");
            async Task<HttpResponseMessage> Post(string route, object body, string? etag, string? csrf = null, HttpClient? sender = null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = body is string text ? new StringContent(text, Encoding.UTF8, "application/json") : JsonContent.Create(body) };
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
                if (csrf != "omit") request.Headers.Add("X-CSRF-Token", csrf ?? uw.Csrf); return await (sender ?? client).SendAsync(request);
            }
            using var list = await client.GetAsync($"/api/v1/referrals?quoteId={f.QuoteId:D}&pageSize=1"); list.EnsureSuccessStatusCode(); Assert.True(list.Headers.CacheControl!.NoStore);
            var referral = Assert.Single((await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
            var referralId = referral.GetProperty("id").GetGuid(); var tag = (await Assessment()).GetProperty("quoteEtag").GetString()!;
            var query = new { cycleId = f.CycleId, decisions = new[] { new { referralId, etag = referral.GetProperty("etag").GetString(), outcome = "query", reason = "Review trading history",
                question = "Supply your trading history", conditions = new[] { new { code = "provide-trading-history" } } } } };
            using (var missingCsrf = await Post(quoteRoute + "/referral-decisions", query, tag, "omit")) Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
            using (var missingVersion = await Post(quoteRoute + "/referral-decisions", query, null)) Assert.Equal((HttpStatusCode)428, missingVersion.StatusCode);
            using (var unknown = await Post(quoteRoute + "/referral-decisions", new { cycleId = f.CycleId, decisions = query.decisions, overrideAuthority = true }, tag)) Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            using (var denied = await Post(quoteRoute + "/referral-decisions", query, tag, servicing.Csrf, serviceClient)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var success = await Post(quoteRoute + "/referral-decisions", query, tag)) { success.EnsureSuccessStatusCode(); Assert.True(success.Headers.CacheControl!.NoStore); }
            using (var stale = await Post(quoteRoute + "/referral-decisions", query, tag)) Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/referrals/{referralId:D}"); Assert.Equal("queried", detail.GetProperty("state").GetString());
            Assert.Equal("Supply your trading history", Assert.Single(detail.GetProperty("decisions").EnumerateArray()).GetProperty("question").GetString());
            Assert.True(Assert.Single(detail.GetProperty("conditions").EnumerateArray()).TryGetProperty("etag", out _));
            using (var invalidPaging = await client.GetAsync($"/api/v1/referrals?quoteId={f.QuoteId:D}&pageSize=101")) Assert.Equal(HttpStatusCode.BadRequest, invalidPaging.StatusCode);
            foreach (var email in new[] { "system-admin@cover.example", "agency-admin@cover.example" })
            { var denied = await Login(email); using var other = denied.Client; using var response = await other.GetAsync($"/api/v1/referrals/{referralId:D}"); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); }
            var assessment = await Assessment(); var proof = assessment.GetProperty("proofRequirements").EnumerateArray().Single(x => x.GetProperty("code").GetString() == "motor-trader-proof");
            using var form = new MultipartFormDataContent(); form.Add(new StringContent("proof.txt"), "fileName"); form.Add(new StringContent("text/plain"), "contentType");
            var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes("Fictional actual document")); bytes.Headers.ContentType = new("text/plain"); form.Add(bytes, "file", "proof.txt");
            using var upload = new HttpRequestMessage(HttpMethod.Post, quoteRoute + "/underwriting/evidence-files") { Content = form };
            upload.Headers.Add("X-CSRF-Token", servicing.Csrf); upload.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); upload.Headers.TryAddWithoutValidation("If-Match", assessment.GetProperty("quoteEtag").GetString());
            using var uploaded = await serviceClient.SendAsync(upload); Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
            var fileId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var attached = await Post(quoteRoute + "/underwriting/evidence", new { cycleId = f.CycleId, fileId, requirementCode = "motor-trader-proof", inputFingerprint = proof.GetProperty("inputFingerprint").GetString(), reason = "Supplied actual file" },
                (await Assessment()).GetProperty("quoteEtag").GetString(), servicing.Csrf, serviceClient); Assert.Equal(HttpStatusCode.Created, attached.StatusCode);
            var associationId = (await attached.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var evidence = Assert.Single((await client.GetFromJsonAsync<JsonElement>(quoteRoute + "/underwriting/evidence")).GetProperty("items").EnumerateArray());
            Assert.Equal("unreviewed", evidence.GetProperty("reviewState").GetString());
            using var reviewed = await Post(quoteRoute + $"/underwriting/evidence/{associationId:D}/reviews", new { cycleId = f.CycleId, associationEtag = evidence.GetProperty("etag").GetString(), outcome = "accepted", expectedFingerprint = proof.GetProperty("inputFingerprint").GetString(), reason = "Independent content review" }, (await Assessment()).GetProperty("quoteEtag").GetString());
            reviewed.EnsureSuccessStatusCode();
            var events = await client.GetFromJsonAsync<JsonElement>(quoteRoute + $"/underwriting/evidence/{associationId:D}/events"); Assert.Equal("accepted", Assert.Single(events.GetProperty("items").EnumerateArray()).GetProperty("outcome").GetString());
            var final = await Assessment(); Assert.True(final.GetProperty("proofRequirements").EnumerateArray().Single(x => x.GetProperty("code").GetString() == "motor-trader-proof").GetProperty("satisfied").GetBoolean());
            Assert.False(final.GetProperty("capabilities").GetProperty("canIssue").GetBoolean());
        });
    }
}
