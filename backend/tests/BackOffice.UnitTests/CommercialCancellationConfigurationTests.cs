using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialCancellationConfigurationTests
{
    [Fact]
    public void TrustedProductScopeIsRequiredAndMotorDefaultsRemainUnchanged()
    {
        Assert.NotNull(CancellationConfiguration.Parse(CancellationConfiguration.DemoJson));
        Assert.Null(CancellationConfiguration.Parse(CancellationConfiguration.CommercialDemoJson));
        Assert.Null(CancellationConfiguration.Parse(CancellationConfiguration.DemoJson,CancellationConfiguration.CommercialScope));
        var commercial=Assert.IsType<CancellationSettings>(CancellationConfiguration.Parse(CancellationConfiguration.CommercialDemoJson,CancellationConfiguration.CommercialScope));
        Assert.Equal("demo-servicing-1",commercial.RuleVersion);
        Assert.Equal(new[]{"commercial-demo-senior-1"},commercial.AuthorityVersions);
        Assert.Equal(commercial.AuthorityVersions,commercial.SeniorAuthorityVersions);
        Assert.Null(CancellationConfiguration.Parse(CancellationConfiguration.CommercialDemoJson,"untrusted"));
    }

    [Theory]
    [InlineData("{\"extra\":true,", "{")]
    [InlineData("\"seniorAuthorityVersions\":[\"foreign-authority\"]", "\"seniorAuthorityVersions\":[\"commercial-demo-senior-1\"]")]
    [InlineData("\"demo\":false", "\"demo\":true")]
    public void CommercialConfigurationRejectsUnknownOrInconsistentAuthority(string replacement,string original)
    {
        Assert.Null(CancellationConfiguration.Parse(CancellationConfiguration.CommercialDemoJson.Replace(original,replacement),CancellationConfiguration.CommercialScope));
    }
}
