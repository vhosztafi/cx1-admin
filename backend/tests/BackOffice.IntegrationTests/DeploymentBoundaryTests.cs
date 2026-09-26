using BackOffice.Api;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Xunit;

public class DeploymentBoundaryTests
{
    private const string Secret = "test-only-origin-secret-01234567890123456789";

    [Theory]
    [InlineData("/")]
    [InlineData("/health/live")]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/files/a/download")]
    public async Task DirectOriginRequestsAreDeniedBeforeAnyEndpoint(string path)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Staging" });
        builder.WebHost.UseTestServer();
        builder.Configuration["Cover:OriginSecret"] = Secret;
        await using var app = builder.Build();
        app.UseDeploymentBoundary();
        app.Run(context => { context.Response.StatusCode = 204; return Task.CompletedTask; });
        await app.StartAsync();
        using var client = app.GetTestClient();
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Cx1-Origin-Key", "wrong");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Cx1-Origin-Key");
        client.DefaultRequestHeaders.Add("X-Cx1-Origin-Key", Secret);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task HostedApiCannotStartWithoutOriginProtection()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Staging" });
        await using var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() => app.UseDeploymentBoundary());
    }

    [Theory]
    [InlineData(true, "https", "https")]
    [InlineData(false, "https", "http")]
    [InlineData(true, "http", "http")]
    public async Task TunnelHttpsRequiresOptInAndAuthenticatedGateway(bool enabled, string forwarded, string expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Staging" });
        builder.WebHost.UseTestServer();
        builder.Configuration["Cover:OriginSecret"] = Secret;
        builder.Configuration["Cover:TrustGatewayHttps"] = enabled.ToString();
        await using var app = builder.Build();
        app.UseDeploymentBoundary();
        app.Run(context => context.Response.WriteAsync(context.Request.Scheme));
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Cx1-Forwarded-Proto", forwarded);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Cx1-Origin-Key", Secret);
        Assert.Equal(expected, await client.GetStringAsync("/"));
    }

    [Theory]
    [InlineData("Staging", true, true)]
    [InlineData("Staging", false, false)]
    [InlineData("Production", true, false)]
    [InlineData("Development", false, true)]
    public void DemoWorkersRequireExplicitHostedOptIn(string environment, bool enabled, bool expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration["Cover:HostedDemoEnabled"] = enabled.ToString();
        Assert.Equal(expected, DeploymentBoundary.DemoWorkersEnabled(builder));
    }

    [Fact]
    public void HostedInitializationDoesNotWidenTheExistingResetGuard()
    {
        DemoDatabase.ValidateHostedDevTarget("Server=.\\SQL2022;Database=Cx1_Dev;Integrated Security=true");
        Assert.Throws<InvalidOperationException>(() => DemoDatabase.ValidateHostedDevTarget(DemoDatabase.DefaultConnection));
        Assert.Throws<InvalidOperationException>(() => DemoDatabase.ValidateHostedDevTarget("Server=.;Database=Cx1_Dev;AttachDbFilename=C:\\other.mdf"));
        Assert.Throws<InvalidOperationException>(() => DemoDatabase.ValidateDemoTarget("Server=.;Database=Cx1_Dev"));
    }
}
