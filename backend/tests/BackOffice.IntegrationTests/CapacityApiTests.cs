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
    public async Task RealSqlCapacityApiRequiresCsrfExactVersionsAndRoleBeforeRetainedReplay()
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, escalation, submission) = await CapacityRequest(db, password, "query-proof");
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:QuoteRatingWorkerEnabled", "false")
                .UseSetting("Cover:CapacityWorkerEnabled", "false")
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "capacity-api-test-keys", db.Database.GetDbConnection().Database)))
                .ConfigureServices(services => {
                    services.AddScoped(provider => new CapacityService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), f.Clock));
                    services.AddScoped(provider => new CapacityReadModel(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), f.Clock));
                }));
            async Task<(HttpClient Client, string Csrf)> Login(string email)
            {
                var client = host.CreateClient(); var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password }) }; request.Headers.Add("X-CSRF-Token", token);
                using var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
                return (client, (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!);
            }
            var (client, csrf) = await Login("underwriter@cover.example"); using var ownedClient = client;
            using var read = await client.GetAsync($"/api/v1/escalations/{escalation.Id}"); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Contains("no-store", read.Headers.CacheControl!.ToString());
            var view = await read.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(submission.Id, view.GetProperty("currentSubmissionId").GetGuid());
            Assert.Equal(6, view.GetProperty("scenarios").GetArrayLength()); Assert.Equal("queued", view.GetProperty("state").GetString());
            Assert.False(view.GetProperty("capabilities").GetProperty("canSend").GetBoolean());
            using var messages = await client.GetAsync($"/api/v1/escalations/{escalation.Id}/messages?pageSize=1"); messages.EnsureSuccessStatusCode();
            Assert.Single((await messages.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
            var referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.CycleId == f.CycleId && x.RuleCode == "cover-stock-custody");
            var quoteVersion = await db.Set<Quote>().AsNoTracking().Where(x => x.Id == f.QuoteId).Select(x => x.RowVersion).SingleAsync();
            var body = new { cycleId = f.CycleId, referralEtag = "\"" + Convert.ToBase64String(referral.RowVersion) + "\"", providerId = escalation.ProviderId, reason = "Another exact exposure request" };
            async Task<HttpResponseMessage> Send(string? csrfValue, object payload, string key)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/referrals/{referral.Id}/escalations") { Content = JsonContent.Create(payload) };
                request.Headers.Add("If-Match", "\"" + Convert.ToBase64String(quoteVersion) + "\""); request.Headers.Add("Idempotency-Key", key);
                if (csrfValue is not null) request.Headers.Add("X-CSRF-Token", csrfValue); return await client.SendAsync(request);
            }
            using var deniedCsrf = await Send(null, body, Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.Forbidden, deniedCsrf.StatusCode);
            using var unknown = await Send(csrf, new { body.cycleId, body.referralEtag, body.providerId, body.reason, ignoreAuthority = true }, Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            var key = Guid.NewGuid().ToString(); using var created = await Send(csrf, body, key); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var receipt = await created.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(f.QuoteId, receipt.GetProperty("quoteId").GetGuid());
            using var replay = await Send(csrf, body, key); Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(receipt.GetProperty("id").GetGuid(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            using var stale = await Send(csrf, body, Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
            var servicingEmail = await db.Set<StaffUser>().Where(x => x.Id == f.Servicing.UserId).Select(x => x.Email).SingleAsync();
            var (servicing, servicingCsrf) = await Login(servicingEmail); using var scopedServicing = servicing;
            using var readOnly = await servicing.GetAsync($"/api/v1/escalations/{escalation.Id}"); Assert.Equal(HttpStatusCode.OK, readOnly.StatusCode);
            Assert.False((await readOnly.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("capabilities").GetProperty("canRecordResponse").GetBoolean());
            using var forbidden = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/referrals/{referral.Id}/escalations") { Content = JsonContent.Create(body) };
            forbidden.Headers.Add("X-CSRF-Token", servicingCsrf); forbidden.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); forbidden.Headers.Add("If-Match", receipt.GetProperty("quoteEtag").GetString());
            using var forbiddenResult = await servicing.SendAsync(forbidden); Assert.Equal(HttpStatusCode.Forbidden, forbiddenResult.StatusCode);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason=N'Revoke before replay' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
            using var revoked = await Send(csrf, body, key); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        });
    }
}
