using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Microsoft.AspNetCore.Hosting;

namespace BackOffice.IntegrationTests;

// Host wiring checks only. Real-SQL migration/session tests are added in 02-02/03.
public class HostTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;
    public HostTests(WebApplicationFactory<Program> factory) => this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

    [Fact]
    public async Task LivenessIsPublicButNoPlaceholderBusinessRouteExists()
    {
        using var client = factory.CreateClient();
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(System.Net.HttpStatusCode.OK, live.StatusCode);
        using var policy = await client.GetAsync("/api/v1/policies");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, policy.StatusCode);
    }
}
