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
    public Task RealSqlOperationalCancellationApiUsesExactScopedVersion() => WithDatabase(async (db, password) =>
    {
        var setup = await AcceptedIssue(db, password); var f = setup.Source;
        await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        var version = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("Cover:SqlConnection", db.Database.GetConnectionString())
            .UseSetting("Cover:OperationalCancellationWorkerEnabled", "false").UseSetting("Cover:OperationalMidWorkerEnabled", "false").UseSetting("Cover:DiagnosticWorkerEnabled", "false")
            .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "cancellation-api-keys", db.Database.GetDbConnection().Database)))
            .ConfigureServices(s => s.AddSingleton<TimeProvider>(f.Clock)));
        using var client = host.CreateClient(); var path = $"/api/v1/versions/{version.Id}/cancellation-consequences";
        using (var anonymous = await client.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using (var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "underwriter@cover.example", password }) })
        { login.Headers.Add("X-CSRF-TOKEN", csrf); (await client.SendAsync(login)).EnsureSuccessStatusCode(); }
        using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Empty((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
        using var missing = await client.GetAsync($"/api/v1/versions/{Guid.NewGuid()}/cancellation-consequences");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    });
}
