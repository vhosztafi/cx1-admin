using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlServicingDraftTestsApiRequiresCsrfCurrentVersionAndStrictInput()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var issued = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:QuoteRatingWorkerEnabled", "false")
                .UseSetting("Cover:CapacityWorkerEnabled", "false").UseSetting("Cover:QuoteDeliveryWorkerEnabled", "false").UseSetting("Cover:AgencyNotificationWorkerEnabled", "false")
                .UseSetting("Cover:DiagnosticWorkerEnabled", "false").UseSetting("Cover:QuoteLookupWorkerEnabled", "false")
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "servicing-api-keys", db.Database.GetDbConnection().Database))));
            using var client = host.CreateClient();
            var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "underwriter@cover.example", password }) })
            { login.Headers.Add("X-CSRF-Token", csrf); using var response = await client.SendAsync(login); response.EnsureSuccessStatusCode(); }
            csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            var route = $"/api/v1/terms/{issued.TermId}/drafts";
            using var list = await client.GetAsync(route); list.EnsureSuccessStatusCode(); var etag = list.Headers.ETag!.Tag;
            var key = Guid.NewGuid().ToString();
            async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, string? fence = null, string? csrfOverride = null, bool version = true)
            {
                using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
                if (csrfOverride != "missing") request.Headers.Add("X-CSRF-Token", csrfOverride ?? csrf);
                request.Headers.Add("Idempotency-Key", key); if (version) request.Headers.TryAddWithoutValidation("If-Match", etag);
                if (fence is not null) request.Headers.Add("X-Edit-Lease", fence);
                return await client.SendAsync(request);
            }
            var input = new { kind = "adjustment", baseVersionId = issued.Id, commonEffectiveIntent = new { localDate = "2026-10-01", localTime = "00:00", timeZone = "Europe/London" }, reason = "Fictional API servicing request" };
            using (var missing = await Send(HttpMethod.Post, route, input, csrfOverride: "missing")) Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
            using (var invalid = await Send(HttpMethod.Post, route, input, csrfOverride: "invalid")) Assert.Equal(HttpStatusCode.Forbidden, invalid.StatusCode);
            using (var missing = await Send(HttpMethod.Post, route, input, version: false)) Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
            using (var unknown = await Send(HttpMethod.Post, route, new { input.kind, input.baseVersionId, input.commonEffectiveIntent, input.reason, premium = "1.00" })) Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            using var created = await Send(HttpMethod.Post, route, input); var createdText = await created.Content.ReadAsStringAsync();
            Assert.True(created.StatusCode == HttpStatusCode.Created, createdText); Assert.True(created.Headers.CacheControl!.NoStore);
            using (var replay = await Send(HttpMethod.Post, route, input)) Assert.Equal(createdText, await replay.Content.ReadAsStringAsync());
            var draft = JsonSerializer.Deserialize<JsonElement>(createdText); route = $"/api/v1/drafts/{draft.GetProperty("id").GetGuid()}";
            Assert.Equal(route, created.Headers.Location!.ToString());
            etag = created.Headers.ETag!.Tag; key = Guid.NewGuid().ToString();
            using var acquired = await Send(HttpMethod.Post, route + "/lease", new { mode = "acquire" }); acquired.EnsureSuccessStatusCode();
            etag = acquired.Headers.ETag!.Tag;
            draft = await acquired.Content.ReadFromJsonAsync<JsonElement>(); var fence = draft.GetProperty("lease").GetProperty("leaseToken").GetString()!;
            foreach (var (method, suffix, body) in new (HttpMethod, string, object?)[] {
                (HttpMethod.Put, "/proposal", draft.GetProperty("proposal")), (HttpMethod.Put, "/lease", null),
                (HttpMethod.Delete, "/lease", null), (HttpMethod.Post, "/abandon", new { reason = "Fictional abandonment reason" }) })
            {
                using (var missing = await Send(method, route + suffix, body, fence, "missing")) Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
                using (var missing = await Send(method, route + suffix, body)) Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            }
            using var saved = await Send(HttpMethod.Put, route + "/proposal", draft.GetProperty("proposal"), fence); saved.EnsureSuccessStatusCode();
            using var read = await client.GetAsync(route); read.EnsureSuccessStatusCode(); Assert.True(read.Headers.CacheControl!.NoStore);
            Assert.Equal(await saved.Content.ReadAsStringAsync(), await read.Content.ReadAsStringAsync());
            Assert.Equal(2, await db.Set<ServicingRevision>().CountAsync());
        });
    }
}
