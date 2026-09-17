using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlQuoteIssueApiStrictInputCsrfEtagLostResponseAndProtectedReads()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:QuoteRatingWorkerEnabled", "false")
                .UseSetting("Cover:CapacityWorkerEnabled", "false").UseSetting("Cover:QuoteDeliveryWorkerEnabled", "false").UseSetting("Cover:AgencyNotificationWorkerEnabled", "false")
                .UseSetting("Cover:DiagnosticWorkerEnabled", "false").UseSetting("Cover:QuoteLookupWorkerEnabled", "false")
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "issue-api-keys", db.Database.GetDbConnection().Database)))
                .ConfigureServices(services => services.AddScoped(_ => new QuoteIssueService(f.Factory, f.Clock))));
            using var client = host.CreateClient();
            var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "underwriter@cover.example", password }) })
            { login.Headers.Add("X-CSRF-Token", csrf); using var response = await client.SendAsync(login); response.EnsureSuccessStatusCode(); }
            csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            var route = $"/api/v1/quotes/{f.QuoteId:D}/issue"; var key = Guid.NewGuid().ToString();
            async Task<HttpResponseMessage> Send(object body, bool token = true, bool etag = true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
                request.Headers.Add("Idempotency-Key", key); if (token) request.Headers.Add("X-CSRF-Token", csrf);
                if (etag) request.Headers.TryAddWithoutValidation("If-Match", "\"" + Convert.ToBase64String(setup.Version) + "\"");
                return await client.SendAsync(request);
            }
            using (var missing = await Send(setup.Input, token: false)) Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
            using (var missing = await Send(setup.Input, etag: false)) Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
            using (var unknown = await Send(new { setup.Input.CycleId, setup.Input.RatingId, setup.Input.AcceptanceId, setup.Input.TermsHash, setup.Input.AssuranceHash, setup.Input.Reason, premium = "1.00" })) Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            string body;
            using (var issued = await Send(setup.Input)) { body = await issued.Content.ReadAsStringAsync(); Assert.True(issued.StatusCode == HttpStatusCode.Created, body); Assert.True(issued.Headers.CacheControl!.NoStore); using var locationReceipt = JsonDocument.Parse(body); Assert.Equal("/api/v1/policies/" + locationReceipt.RootElement.GetProperty("policyId").GetString(), issued.Headers.Location!.ToString()); }
            using (var retry = await Send(setup.Input)) { Assert.Equal(HttpStatusCode.Created, retry.StatusCode); Assert.Equal(body, await retry.Content.ReadAsStringAsync()); }
            using var receipt = JsonDocument.Parse(body); var p = receipt.RootElement;
            var policyId = p.GetProperty("policyId").GetGuid(); var termId = p.GetProperty("termId").GetGuid(); var versionId = p.GetProperty("versionId").GetGuid();
            var boundQuote = await client.GetFromJsonAsync<JsonElement>($"/api/v1/quotes/{f.QuoteId}"); Assert.Equal(policyId, boundQuote.GetProperty("boundPolicyId").GetGuid());
            foreach (var path in new[] { $"/api/v1/policies/{policyId}", $"/api/v1/policies/{policyId}/terms/{termId}",
                $"/api/v1/policies/{policyId}/terms/{termId}/versions/{versionId}", $"/api/v1/policies/{policyId}/terms/{termId}/transactions/{p.GetProperty("transactionId").GetGuid()}",
                $"/api/v1/policies/{policyId}/terms/{termId}/obligations/{p.GetProperty("obligationId").GetGuid()}" })
            {
                using var read = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.True(read.Headers.CacheControl!.NoStore);
                var view = await read.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(versionId, view.GetProperty("versionId").GetGuid()); Assert.Equal(3, view.GetProperty("documentRequests").GetArrayLength());
                Assert.All(view.GetProperty("documentRequests").EnumerateArray(), x => Assert.Equal("requested", x.GetProperty("state").GetString()));
            }
            using (var wrong = await client.GetAsync($"/api/v1/policies/{policyId}/terms/{termId}/versions/{Guid.NewGuid()}")) Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
            using (var query = await client.GetAsync($"/api/v1/policies/{policyId}?agencyId={Guid.NewGuid()}")) Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
            Assert.Equal(3, await db.Set<OutboxWork>().CountAsync(x => x.Kind == "policy-document" && x.State == "pending" && x.Attempts == 0));
        });
    }
}
