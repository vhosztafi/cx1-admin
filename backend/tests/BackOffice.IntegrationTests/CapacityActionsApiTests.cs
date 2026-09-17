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
    public async Task RealSqlCapacityActionApiRoutesOnlyActiveSeniorUsersAndRechecksScopeBeforeReplay()
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, escalation, _) = await CapacityRequest(db, password, "query-proof");
            var (_, foreign, _) = await CapacityRequest(db, password, "query-proof");
            var senior = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Email == "senior-underwriter@cover.example");
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:CapacityWorkerEnabled", "false")
                .UseSetting("Cover:QuoteRatingWorkerEnabled", "false").UseSetting("Cover:AgencyNotificationWorkerEnabled", "false")
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "capacity-actions-keys", db.Database.GetDbConnection().Database)))
                .ConfigureServices(s => {
                    s.AddScoped(p => new CapacityService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), f.Clock));
                    s.AddScoped(p => new CapacityReadModel(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), f.Clock));
                }));
            using var client = host.CreateClient();
            var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "underwriter@cover.example", password })).EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Remove("X-CSRF-Token");
            csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            var path = $"/api/v1/escalations/{escalation.Id}";
            var view = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.Contains(view.GetProperty("assignmentOptions").EnumerateArray(), x => x.GetProperty("id").GetGuid() == senior.Id);
            Assert.DoesNotContain(view.GetProperty("similarReferrals").EnumerateArray(), x => x.GetProperty("id").GetGuid() == foreign.Id);
            var body = new { cycleId = f.CycleId, escalationEtag = view.GetProperty("etag").GetString(), action = "assign", assignedUserId = senior.Id, reason = "Senior review of this exact request" };
            var version = view.GetProperty("quoteEtag").GetString(); var key = Guid.NewGuid().ToString();
            async Task<HttpResponseMessage> Send(object data, string requestKey, bool withCsrf = true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, path + "/actions") { Content = JsonContent.Create(data) };
                request.Headers.Add("If-Match", version); request.Headers.Add("Idempotency-Key", requestKey);
                if (withCsrf) request.Headers.Add("X-CSRF-Token", csrf);
                return await client.SendAsync(request);
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(body, key, false)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(new { body.cycleId, body.escalationEtag, body.action, body.assignedUserId, body.reason, ignoreAuthority = true }, key)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(new { body.cycleId, body.escalationEtag, body.action, assignedUserId = f.Servicing.UserId, body.reason }, Guid.NewGuid().ToString())).StatusCode);
            using var written = await Send(body, key); Assert.Equal(HttpStatusCode.OK, written.StatusCode); Assert.True(written.Headers.CacheControl!.NoStore);
            var saved = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.Equal(senior.DisplayName, saved.GetProperty("assignedUserLabel").GetString());
            Assert.Single(saved.GetProperty("actionHistory").EnumerateArray());
            Assert.Equal("assign", saved.GetProperty("actionHistory")[0].GetProperty("action").GetString());
            Assert.Equal(HttpStatusCode.OK, (await Send(body, key)).StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await Send(body, Guid.NewGuid().ToString())).StatusCode);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={senior.Id}");
            Assert.Equal(HttpStatusCode.Conflict, (await Send(body, key)).StatusCode);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'active' WHERE Id={senior.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason=N'Revoked test scope' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(body, key)).StatusCode);
        });
    }
}
