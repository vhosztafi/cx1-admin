using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlCommercialIssueHttpCapacityConflictIsScopedBoundedAndNotCacheable() => CommercialTermsScenario(stopAfterAccepted: true, inspectAccepted: async (db, password) =>
    {
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync();
        var senior = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Email == "senior-underwriter@cover.example");
        var f = await CommercialIssueCommand(db, cycle, cycle.CurrentAcceptanceId!.Value, senior.Id);
        await PublishCommercialTestLimit(db, senior.Id, Now, 1m);
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:QuoteRatingWorkerEnabled", "false")
            .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "commercial-issue-api-keys", db.Database.GetDbConnection().Database)))
            .ConfigureServices(services => services.AddSingleton<TimeProvider>(new RatingClock())));
        var route = $"/api/v1/quotes/{f.Quote.Id:D}/issue";
        async Task<(HttpClient Client, string Csrf)> SignIn(string email)
        {
            var client = host.CreateClient();
            var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password }) };
            login.Headers.Add("X-CSRF-Token", csrf); using var response = await client.SendAsync(login); response.EnsureSuccessStatusCode();
            return (client, (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!);
        }
        async Task<HttpResponseMessage> Issue(HttpClient client, string csrf)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(f.Input) };
            request.Headers.Add("X-CSRF-Token", csrf); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            request.Headers.TryAddWithoutValidation("If-Match", "\"" + Convert.ToBase64String(f.Quote.RowVersion) + "\"");
            return await client.SendAsync(request);
        }
        var internalUser = await SignIn("senior-underwriter@cover.example"); using var internalClient = internalUser.Client;
        using var denied = await Issue(internalClient, internalUser.Csrf); Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.True(denied.Headers.CacheControl!.NoStore);
        var problem = await denied.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("commercial-district-capacity-exceeded", problem.GetProperty("code").GetString());
        var capacity = problem.GetProperty("commercialCapacity"); Assert.Equal("commercial-issue-capacity-1", capacity.GetProperty("format").GetString());
        Assert.Equal(Now, capacity.GetProperty("observedAt").GetDateTimeOffset()); Assert.False(capacity.GetProperty("truncated").GetBoolean());
        Assert.InRange(capacity.GetProperty("intervals").GetArrayLength(), 1, 100);
        foreach (var interval in capacity.GetProperty("intervals").EnumerateArray())
        {
            Assert.Equal("1.00", interval.GetProperty("publishedLimit").GetString()); Assert.Equal("1.00", interval.GetProperty("effectiveLimit").GetString());
            Assert.NotEqual(Guid.Empty, interval.GetProperty("limitVersionId").GetGuid()); Assert.Equal(64, interval.GetProperty("limitHash").GetString()!.Length);
            Assert.Equal(cycle.StartsAt, interval.GetProperty("startsAt").GetDateTimeOffset()); Assert.Equal(cycle.EndsAt, interval.GetProperty("endsAt").GetDateTimeOffset());
            Assert.False(interval.TryGetProperty("policyId", out _));
        }
        var agency = await SignIn("agency-admin@cover.example"); using var agencyClient = agency.Client;
        using var hidden = await Issue(agencyClient, agency.Csrf); Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        Assert.DoesNotContain("commercialCapacity", await hidden.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await db.Set<Policy>().AnyAsync()); Assert.False(await db.Set<CommercialExposureVersion>().AnyAsync());
        Assert.False(await db.Set<IdempotencyRecord>().AnyAsync(x => x.Route == route));
    });
}
