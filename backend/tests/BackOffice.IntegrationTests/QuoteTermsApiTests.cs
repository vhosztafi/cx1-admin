using System.Net;
using System.Net.Http.Json;
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
    public async Task RealSqlQuoteTermsApiRequiresStrictTransportAndReadsScopedStructuredHistory()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await SignedTerms(db, password); var f = setup.Fixture;
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:QuoteRatingWorkerEnabled", "false")
                .UseSetting("Cover:CapacityWorkerEnabled", "false").UseSetting("Cover:QuoteDeliveryWorkerEnabled", "false").UseSetting("Cover:AgencyNotificationWorkerEnabled", "false")
                .UseSetting("Cover:DiagnosticWorkerEnabled", "false").UseSetting("Cover:QuoteLookupWorkerEnabled", "false")
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "terms-api-keys", db.Database.GetDbConnection().Database)))
                .ConfigureServices(services => {
                    services.AddScoped(_ => new QuoteTermsService(f.Factory, f.Clock)); services.AddScoped(_ => new QuoteTermsReadModel(f.Factory, f.Clock));
                    services.AddScoped(_ => new QuoteAcceptanceService(f.Factory, f.Clock)); services.AddScoped(_ => new QuoteDeliveryJobs(f.Factory, f.Clock));
                }));
            using var client = host.CreateClient();
            var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "servicing@cover.example", password }) })
            { login.Headers.Add("X-CSRF-Token", csrf); using var response = await client.SendAsync(login); response.EnsureSuccessStatusCode(); }
            csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            var route = $"/api/v1/quotes/{f.QuoteId:D}/terms";
            async Task<HttpResponseMessage> Send(object body, bool token = true, bool etag = true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
                if (token) request.Headers.Add("X-CSRF-Token", csrf);
                if (etag) request.Headers.TryAddWithoutValidation("If-Match", "\"" + Convert.ToBase64String(await TermsVersion(db, f)) + "\"");
                return await client.SendAsync(request);
            }
            using (var read = await client.GetAsync(route + "?pageSize=1"))
            {
                Assert.True(read.IsSuccessStatusCode, await read.Content.ReadAsStringAsync()); Assert.True(read.Headers.CacheControl!.NoStore);
                var view = await read.Content.ReadFromJsonAsync<JsonElement>(); var terms = Assert.Single(view.GetProperty("terms").EnumerateArray());
                Assert.Equal(setup.TermsId, terms.GetProperty("id").GetGuid()); Assert.Equal("structured-payload", terms.GetProperty("documentState").GetString());
                Assert.True(terms.GetProperty("rating").GetProperty("applicable").GetBoolean()); Assert.NotEmpty(terms.GetProperty("cover").EnumerateArray());
                Assert.Contains(view.GetProperty("recipientOptions").EnumerateArray(), x => x.GetProperty("id").GetGuid() == setup.ContactId);
                Assert.NotEmpty(view.GetProperty("templates").EnumerateArray());
            }
            var input = new { termsVersionId = setup.TermsId, recipientContactIds = new[] { setup.ContactId } };
            using (var missing = await Send(input, token: false)) Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
            using (var missing = await Send(input, etag: false)) Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
            using (var unknown = await Send(new { input.termsVersionId, input.recipientContactIds, price = "1.00" })) Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            using (var foreign = await Send(new { input.termsVersionId, recipientContactIds = new[] { Guid.NewGuid() } })) Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);
            using (var sent = await Send(input))
            {
                Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode); var receipt = await sent.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("queued", receipt.GetProperty("state").GetString()); Assert.NotNull(sent.Headers.Location);
                using var job = await client.GetAsync(sent.Headers.Location); job.EnsureSuccessStatusCode();
                Assert.Equal("quote-delivery", (await job.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kind").GetString());
            }
            using (var unknown = await client.GetAsync(route + "?sort=secret")) Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            using (var invalid = await client.GetAsync(route + "?termsCursor=made-up&pageSize=1")) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using (var foreign = await client.GetAsync($"/api/v1/quotes/{Guid.NewGuid():D}/terms")) Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        });
    }
}
