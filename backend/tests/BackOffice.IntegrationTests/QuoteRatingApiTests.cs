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
    private static async Task CheckUnderwritingApi(BackOfficeDbContext db, string password, Guid quoteId, Guid revisionId, Guid ratingId, byte[] version)
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:QuoteRatingWorkerEnabled", "false")
            .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "underwriting-api-test-keys", db.Database.GetDbConnection().Database)))
            .ConfigureServices(services =>
            {
                services.AddScoped(provider => new QuoteRatingService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new RatingClock()));
                services.AddScoped(provider => new QuoteUnderwritingLifecycle(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new RatingClock()));
                services.AddScoped(provider => new QuoteUnderwritingReadModel(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new RatingClock()));
            }));
        var route = $"/api/v1/quotes/{quoteId:D}";
        using var anonymous = host.CreateClient();
        using var anonymousRead = await anonymous.GetAsync(route + "/underwriting"); Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
        async Task<(HttpClient Client, string Csrf)> SignIn(string email)
        {
            var client = host.CreateClient(); var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password }) }; login.Headers.Add("X-CSRF-Token", csrf);
            using var response = await client.SendAsync(login); response.EnsureSuccessStatusCode();
            return (client, (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!);
        }
        var signedIn = await SignIn("servicing@cover.example"); using var client = signedIn.Client;
        using var read = await client.GetAsync(route + "/underwriting"); read.EnsureSuccessStatusCode(); Assert.True(read.Headers.CacheControl!.NoStore);
        var assessment = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rated", assessment.GetProperty("state").GetString()); Assert.True(assessment.GetProperty("capabilities").GetProperty("canSubmit").GetBoolean());
        Assert.False(assessment.GetProperty("capabilities").GetProperty("canIssue").GetBoolean());
        Assert.Contains(assessment.GetProperty("blockers").EnumerateArray(), x => x.GetProperty("code").GetString()!.StartsWith("evidence-missing-", StringComparison.Ordinal));
        using var price = await client.GetAsync($"/api/v1/ratings/{ratingId:D}"); price.EnsureSuccessStatusCode(); Assert.True(price.Headers.CacheControl!.NoStore);
        var result = await price.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("600.00", result.GetProperty("annualPremium").GetString()); Assert.True(result.GetProperty("applicable").GetBoolean());
        Assert.Equal(revisionId, result.GetProperty("revisionId").GetGuid()); Assert.Equal(quoteId, result.GetProperty("quoteId").GetGuid());
        var workId = await db.Set<UnderwritingCycle>().Where(x => x.QuoteId == quoteId).Select(x => x.WorkId).SingleAsync();
        using var jobRead = await client.GetAsync($"/api/v1/jobs/{workId:D}"); jobRead.EnsureSuccessStatusCode();
        Assert.Equal("quote-rating", (await jobRead.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kind").GetString());
        var etag = "\"" + Convert.ToBase64String(version) + "\"";
        async Task Problem(object body, int status, string code, string? tag, bool csrf = true, string suffix = "/rate")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route + suffix) { Content = body is string text ? new StringContent(text, Encoding.UTF8, "application/json") : JsonContent.Create(body) };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); if (csrf) request.Headers.Add("X-CSRF-Token", signedIn.Csrf);
            if (tag is not null) request.Headers.TryAddWithoutValidation("If-Match", tag);
            using var response = await client.SendAsync(request); Assert.Equal(status, (int)response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        var valid = new { revisionId, reason = "Fictional rating request" };
        await Problem(valid, 403, "csrf-invalid", etag, csrf: false);
        await Problem(valid, 428, "version-required", null);
        await Problem(valid, 400, "invalid-version", "W/" + etag);
        await Problem(new { revisionId, reason = "Fictional", annualPremium = "1.00" }, 400, "unknown-field", etag);
        await Problem(new { revisionId, reason = new string('x', 1001) }, 422, "underwriting-reason-required", etag);
        await Problem("{\"revisionId\":\"" + revisionId + "\",\"reason\":\"a\",\"reason\":\"b\"}", 400, "duplicate-field", etag);
        await Problem(new { revisionId = Guid.NewGuid(), reason = "Stale proposal" }, 412, "stale-quote", etag);
        await Problem(new { cycleId = Guid.NewGuid(), reason = "Wrong cycle" }, 404, "underwriting-cycle-not-found", etag, suffix: "/submit");
        foreach (var email in new[] { "system-admin@cover.example", "agency-admin@cover.example" })
        {
            var denied = await SignIn(email); using var deniedClient = denied.Client; using var response = await deniedClient.GetAsync(route + "/underwriting"); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var deniedJob = await deniedClient.GetAsync($"/api/v1/jobs/{workId:D}"); Assert.Equal(HttpStatusCode.Forbidden, deniedJob.StatusCode);
        }
    }
}
