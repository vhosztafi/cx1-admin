using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public async Task RealSqlServicingRatingApiRunsHostedWorkerAndProtectsCommandsAndHistory(string product)
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password, product); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            await using (var seed = await db.Database.BeginTransactionAsync()) { await ServicingRatingSeed.SeedAsync(db); await seed.CommitAsync(); }
            var issued = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var drafts = new ServicingDraftService(f.Factory, f.Clock);
            static byte[] Version(string value) => Convert.FromBase64String(value.Trim('"'));
            var created = await drafts.CreateAsync(f.Servicing, issued.TermId, Version((await drafts.ListAsync(f.Servicing, issued.TermId)).Etag),
                new("adjustment", issued.Id, JsonSerializer.SerializeToElement(new { localDate = "2026-10-01", localTime = "00:00", timeZone = "Europe/London" }), "Fictional HTTP rating test"), Guid.NewGuid().ToString(), Guid.NewGuid());
            var leased = await drafts.LeaseAsync(f.Servicing, created.ResourceId, Version(created.Etag!), "acquire", null, null, Guid.NewGuid().ToString(), Guid.NewGuid());
            var body = JsonNode.Parse(leased.Body)!; var fence = body["lease"]!["leaseToken"]!.GetValue<Guid>(); var proposal = body["proposal"]!.DeepClone();
            var snapshot = JsonNode.Parse(issued.SnapshotJson)!;
            proposal["changes"] = JsonSerializer.SerializeToNode(new[] { new { changeId = Guid.NewGuid(),
                riskItemId = snapshot["risk"]!["drivers"]![0]!["id"]!.GetValue<Guid>(), kind = "driver", operation = "update",
                payload = new { firstName = "Fictional", surname = "Api", fullName = "Fictional Api" } } });
            var saved = await drafts.SaveAsync(f.Servicing, created.ResourceId, Version(leased.Etag!), fence, proposal.ToJsonString(), Guid.NewGuid().ToString(), Guid.NewGuid());
            var revisionId = JsonSerializer.Deserialize<JsonElement>(saved.Body).GetProperty("revisionId").GetGuid();
            using var host = ServicingRatingApiHost(db, f.Clock, true);
            var route = $"/api/v1/drafts/{created.ResourceId:D}";
            using var anonymous = host.CreateClient();
            using (var denied = await anonymous.GetAsync(route + "/ratings")) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            var login = await ServicingRatingLogin(host, password); using var client = login.Client;
            var key = Guid.NewGuid().ToString(); var etag = saved.Etag!;
            async Task<HttpResponseMessage> Rate(object input, bool csrf = true, string? tag = null, string? editLease = null, string suffix = "/rate")
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, route + suffix) { Content = input is string raw ? new StringContent(raw, Encoding.UTF8, "application/json") : JsonContent.Create(input) };
                if (csrf) request.Headers.Add("X-CSRF-Token", login.Csrf);
                request.Headers.Add("Idempotency-Key", key);
                if (tag != "missing") request.Headers.TryAddWithoutValidation("If-Match", tag ?? etag);
                if (editLease != "missing") request.Headers.Add("X-Edit-Lease", editLease ?? fence.ToString());
                return await client.SendAsync(request);
            }
            var valid = new { revisionId, reason = "Rate exact fictional HTTP proposal" };
            using (var denied = await Rate(valid, csrf: false)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var denied = await Rate(valid, tag: "missing")) Assert.Equal(HttpStatusCode.PreconditionRequired, denied.StatusCode);
            using (var denied = await Rate(valid, tag: "W/" + etag)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using (var denied = await Rate(valid, editLease: "missing")) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using (var denied = await Rate(valid, editLease: Guid.NewGuid().ToString())) Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
            using (var denied = await Rate(new { revisionId, valid.reason, premium = "1.00" })) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using (var denied = await Rate("{\"revisionId\":\"" + revisionId + "\",\"reason\":\"first\",\"reason\":\"second\"}")) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using (var denied = await Rate(valid, suffix: "/rate?fee=0")) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using var requested = await Rate(valid); var text = await requested.Content.ReadAsStringAsync();
            Assert.True(requested.StatusCode == HttpStatusCode.Accepted, text); Assert.True(requested.Headers.CacheControl!.NoStore);
            var receipt = JsonSerializer.Deserialize<JsonElement>(text); var jobId = receipt.GetProperty("jobId").GetGuid();
            Assert.Equal($"/api/v1/jobs/{jobId:D}", requested.Headers.Location!.ToString());
            using (var replay = await Rate(valid)) Assert.Equal(text, await replay.Content.ReadAsStringAsync());
            etag = requested.Headers.ETag!.Tag; key = Guid.NewGuid().ToString();
            using var rerated = await Rate(valid); Assert.Equal(HttpStatusCode.Accepted, rerated.StatusCode);
            var currentId = (await rerated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            JsonElement history = default;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                using var response = await client.GetAsync(route + "/ratings?pageSize=1"); response.EnsureSuccessStatusCode(); Assert.True(response.Headers.CacheControl!.NoStore);
                history = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (history.GetProperty("current").GetProperty("applicable").GetBoolean()) break;
                await Task.Delay(100);
            }
            Assert.Equal(currentId, history.GetProperty("current").GetProperty("id").GetGuid());
            Assert.True(history.GetProperty("current").GetProperty("applicable").GetBoolean());
            Assert.Equal("15.00", history.GetProperty("current").GetProperty("result").GetProperty("fee").GetString());
            var cursor = history.GetProperty("nextCursor").GetString()!;
            using var older = await client.GetAsync(route + "/ratings?pageSize=1&cursor=" + Uri.EscapeDataString(cursor)); older.EnsureSuccessStatusCode();
            Assert.False((await older.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0].GetProperty("applicable").GetBoolean());
            foreach (var query in new[] { "pageSize=51", "pageSize=1&pageSize=2", "fee=0", "cursor=forged", "pageSize=2&cursor=" + Uri.EscapeDataString(cursor) })
            { using var denied = await client.GetAsync(route + "/ratings?" + query); Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode); }
            using var job = await client.GetAsync($"/api/v1/jobs/{jobId:D}"); job.EnsureSuccessStatusCode(); Assert.True(job.Headers.CacheControl!.NoStore);
            Assert.Equal("servicing-rating", (await job.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kind").GetString());
            for (var attempt = 0; attempt < 100 && await db.Set<ServicingRatingResult>().CountAsync() < 2; attempt++) await Task.Delay(100);
            Assert.Equal(2, await db.Set<ServicingRatingResult>().CountAsync());
            using (var denied = await client.GetAsync($"/api/v1/jobs/{jobId:D}?fee=0")) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using (var denied = await client.GetAsync($"/api/v1/drafts/{Guid.NewGuid():D}/ratings")) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            // A new saved revision invalidates the old cursor even for the same actor and route.
            proposal["reason"] = "Updated fictional reason after rating";
            await drafts.SaveAsync(f.Servicing, created.ResourceId, Version(rerated.Headers.ETag!.Tag), fence, proposal.ToJsonString(), Guid.NewGuid().ToString(), Guid.NewGuid());
            using (var denied = await client.GetAsync(route + "/ratings?pageSize=1&cursor=" + Uri.EscapeDataString(cursor))) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            Assert.Equal(issued.SnapshotJson, (await db.Set<PolicyVersion>().AsNoTracking().SingleAsync()).SnapshotJson);
        });
    }

    private static async Task VerifyServicingRatingHttpRetry(BackOfficeDbContext db, TimeProvider clock, string password,
        Guid workId, byte[] version, string reason, string key)
    {
        using var host = ServicingRatingApiHost(db, clock, false);
        var login = await ServicingRatingLogin(host, password); using var client = login.Client;
        async Task<HttpResponseMessage> Retry(bool csrf = true, string? etag = null, object? body = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/jobs/{workId:D}/retry") { Content = JsonContent.Create(body ?? new { reason }) };
            if (csrf) request.Headers.Add("X-CSRF-Token", login.Csrf);
            request.Headers.Add("Idempotency-Key", key);
            if (etag != "missing") request.Headers.TryAddWithoutValidation("If-Match", etag ?? "\"" + Convert.ToBase64String(version) + "\"");
            return await client.SendAsync(request);
        }
        using (var denied = await Retry(csrf: false)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await Retry(etag: "missing")) Assert.Equal(HttpStatusCode.PreconditionRequired, denied.StatusCode);
        using (var denied = await Retry(body: new { reason, premium = "0.00" })) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using var response = await Retry(); var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, body); Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal($"/api/v1/jobs/{workId:D}", response.Headers.Location!.ToString());
        using var replay = await Retry(); Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(body, await replay.Content.ReadAsStringAsync());
        using var job = await client.GetAsync($"/api/v1/jobs/{workId:D}"); job.EnsureSuccessStatusCode();
        var view = await job.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(12, view.GetProperty("attemptLimit").GetInt32());
        Assert.False(view.GetProperty("retryAllowed").GetBoolean());
    }

    private static WebApplicationFactory<Program> ServicingRatingApiHost(BackOfficeDbContext db, TimeProvider clock, bool worker)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .UseUrls("http://127.0.0.1:0")
            .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:ServicingRatingWorkerEnabled", worker.ToString())
            .UseSetting("Cover:QuoteRatingWorkerEnabled", "false").UseSetting("Cover:CapacityWorkerEnabled", "false")
            .UseSetting("Cover:QuoteDeliveryWorkerEnabled", "false").UseSetting("Cover:AgencyNotificationWorkerEnabled", "false")
            .UseSetting("Cover:DiagnosticWorkerEnabled", "false").UseSetting("Cover:QuoteLookupWorkerEnabled", "false")
            .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "servicing-rating-api-keys", db.Database.GetDbConnection().Database)))
            .ConfigureServices(services => {
                services.AddScoped(p => new ServicingRatingService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), clock));
                services.AddScoped(p => new ServicingRatingReadModel(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), clock));
                services.AddScoped(p => new ServicingRatingJobs(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), clock));
                services.AddSingleton(p => new ServicingRatingWorker(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), clock));
                services.AddSingleton(p => new SqlJobLeases(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), clock));
            }));

    private static async Task<(HttpClient Client, string Csrf)> ServicingRatingLogin(WebApplicationFactory<Program> host, string password)
    {
        var client = host.CreateClient(); var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "servicing@cover.example", password }) };
        request.Headers.Add("X-CSRF-Token", csrf); using var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
        return (client, (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!);
    }
}
