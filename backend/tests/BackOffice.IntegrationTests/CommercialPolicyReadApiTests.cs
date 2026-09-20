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
    public Task RealSqlCommercialPolicyReadHttpCookiesPrivacyForeignSubjectsAndFilters() => CommercialTermsScenario(stopAfterAccepted: true, inspectAccepted: async (db, password) =>
    {
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync();
        var senior = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Email == "senior-underwriter@cover.example");
        var f = await CommercialIssueCommand(db, cycle, cycle.CurrentAcceptanceId!.Value, senior.Id);
        var model = new CommercialExposureReadModel(f.Factory, new RatingClock());
        var proposed = JsonSerializer.SerializeToElement(await model.ReadAsync(f.Actor, "quotes", f.Quote.Id, cycle.StartsAt, Now));
        Assert.Equal("proposed", proposed.GetProperty("coverageState").GetString());
        var issued = await f.Service.IssueAsync(f.Actor, f.Quote.Id, f.Quote.RowVersion, f.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        async Task<Guid> Draft(Guid policyId, BackOffice.Application.ActorContext actor)
        {
            var term = await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x => x.PolicyId == policyId);
            var version = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.PolicyId == policyId);
            var input = new ServicingDraftCreate("adjustment", version.Id, JsonSerializer.SerializeToElement(new { localDate = "2026-10-01", localTime = "00:00", timeZone = "Europe/London" }), "Fictional exposure access test");
            return (await new ServicingDraftService(f.Factory, new RatingClock()).CreateAsync(actor, term.Id, term.RowVersion, input, Guid.NewGuid().ToString(), Guid.NewGuid())).ResourceId;
        }
        var ownDraft = await Draft(issued.ResourceId, f.Actor);
        Guid foreignQuote = default, foreignPolicy = default, foreignDraft = default;
        await CommercialTermsScenario(async (_, otherCycle, acceptance, actor, _) =>
        {
            var other = await CommercialIssueCommand(db, otherCycle, acceptance, actor);
            foreignQuote = other.Quote.Id;
            foreignPolicy = (await other.Service.IssueAsync(other.Actor, foreignQuote, other.Quote.RowVersion, other.Input, Guid.NewGuid().ToString(), Guid.NewGuid())).ResourceId;
            foreignDraft = await Draft(foreignPolicy, other.Actor);
        }, stopAfterAccepted: true, existingDb: db);
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:QuoteRatingWorkerEnabled", "false")
            .UseSetting("Cover:AgencyNotificationWorkerEnabled", "false").UseSetting("Cover:CapacityWorkerEnabled", "false")
            .UseSetting("Cover:QuoteDeliveryWorkerEnabled", "false").UseSetting("Cover:QuoteLookupWorkerEnabled", "false")
            .UseSetting("Cover:DiagnosticWorkerEnabled", "false")
            .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "commercial-read-api-keys", db.Database.GetDbConnection().Database)))
            .ConfigureServices(s => s.AddSingleton<TimeProvider>(new RatingClock())));
        async Task<JsonElement> Read(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore); return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        async Task<JsonElement> Post(HttpClient client, string path, object body, string? etag = null)
        {
            var csrf = (await Read(client, "/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-CSRF-Token", csrf); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            if (etag is not null) request.Headers.Add("If-Match", etag);
            using var response = await client.SendAsync(request); Assert.True(response.IsSuccessStatusCode, $"{path}: {response.StatusCode}");
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        using var staff = host.CreateClient(); await Post(staff, "/api/v1/auth/login", new { email = "senior-underwriter@cover.example", password });
        using var admin = host.CreateClient(); await Post(admin, "/api/v1/auth/login", new { email = "agency-admin@cover.example", password });
        using var broker = host.CreateClient();
        var agencyVersion = await db.Set<Agency>().AsNoTracking().Where(x => x.Id == f.Quote.AgencyId).Select(x => x.RowVersion).SingleAsync();
        var invitation = await Post(admin, $"/api/v1/agencies/{f.Quote.AgencyId}/invitations", new { email = "commercial-api-reader@example.invalid", displayName = "Fictional commercial reader", role = "broker-readonly" }, "\"" + Convert.ToBase64String(agencyVersion) + "\"");
        var link = await Post(admin, $"/api/v1/invitations/{invitation.GetProperty("invitationId").GetGuid()}/demo-link", new { });
        await Post(broker, "/api/v1/auth/invitations/accept", new { invitationToken = link.GetProperty("invitationToken").GetString(), password });
        await Post(broker, "/api/v1/auth/login", new { email = "commercial-api-reader@example.invalid", password });
        var cutoff = "?effectiveAt=" + Uri.EscapeDataString(cycle.StartsAt.ToString("O")) + "&knownAt=" + Uri.EscapeDataString(Now.ToString("O"));
        var path = $"/api/v1/policies/{issued.ResourceId}/commercial-exposure";
        var aggregate = await Read(staff, path + cutoff);
        Assert.All(aggregate.GetProperty("districts").EnumerateArray(), x => Assert.Equal(2, x.GetProperty("policyCount").GetInt32()));
        var safe = await Read(broker, path + cutoff); Assert.Equal("agency", safe.GetProperty("audience").GetString());
        Assert.Equal(aggregate.GetProperty("outcome").GetString(), safe.GetProperty("outcome").GetString());
        foreach (var field in new[] { "bookSumInsured", "policyCount", "limit", "headroom", "bookId", "limitVersionId", "limitHash", "policyIds" })
            Assert.DoesNotContain("\"" + field + "\"", safe.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(safe.GetProperty("districts").GetRawText(), (await Read(broker, $"/api/v1/quotes/{f.Quote.Id}/commercial-exposure" + cutoff)).GetProperty("districts").GetRawText());
        var draft = await Read(broker, $"/api/v1/drafts/{ownDraft}/commercial-exposure" + cutoff);
        Assert.Equal("unavailable", draft.GetProperty("outcome").GetString()); Assert.Empty(draft.GetProperty("districts").EnumerateArray());
        foreach (var (kind, id) in new[] { ("quotes", foreignQuote), ("policies", foreignPolicy), ("drafts", foreignDraft) })
        {
            using var denied = await broker.GetAsync($"/api/v1/{kind}/{id}/commercial-exposure" + cutoff);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.True(denied.Headers.CacheControl!.NoStore);
            Assert.DoesNotContain("bookSumInsured", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        foreach (var query in new[] { cutoff + "&district=S9", cutoff + "&agencyId=" + f.Quote.AgencyId, cutoff + "&pageSize=1", cutoff + "&knownAt=2026-09-16T12:00:00Z", "?knownAt=2026-09-16T12:00:00Z", "?effectiveAt=invalid&knownAt=invalid" })
            Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync(path + query)).StatusCode);
        var unchanged = await Read(staff, path + cutoff); Assert.Equal(aggregate.GetProperty("districts").GetRawText(), unchanged.GetProperty("districts").GetRawText());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path + cutoff)).StatusCode);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAgencyRelationship SET State=N'inactive' WHERE Id={f.Quote.RelationshipId}");
        Assert.Equal(HttpStatusCode.NotFound, (await broker.GetAsync(path + cutoff)).StatusCode);
    });
}
