using BackOffice.Application.Agencies;
using Xunit;
namespace BackOffice.UnitTests;
public sealed class AgencyDistributionTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"demo\":false,\"kind\":\"agency-distribution\",\"productVersionIds\":[]}")]
    [InlineData("{\"demo\":true,\"kind\":\"rating\",\"productVersionIds\":[]}")]
    [InlineData("{\"demo\":true,\"kind\":\"agency-distribution\",\"productVersionIds\":[\"bad\"]}")]
    [InlineData("{\"demo\":true,\"kind\":\"agency-distribution\",\"productVersionIds\":[null]}")]
    [InlineData("{\"demo\":true,\"kind\":\"agency-distribution\",\"productVersionIds\":[],\"approvedBy\":\"forged\"}")]
    [InlineData("{\"demo\":true,\"demo\":true,\"kind\":\"agency-distribution\",\"productVersionIds\":[]}")]
    public void InvalidCurrentConfigurationFailsClosed(string json)=>Assert.Null(AgencyDistributionRules.Parse(json));
    [Fact]public void ExplicitEmptyGrantIsValidAndDuplicateProductsAreInvalid()
    {
        Assert.Empty(AgencyDistributionRules.Parse("{\"demo\":true,\"kind\":\"agency-distribution\",\"productVersionIds\":[]}")!);
        var id=Guid.NewGuid();var json=System.Text.Json.JsonSerializer.Serialize(new{demo=true,kind="agency-distribution",productVersionIds=new[]{id}});
        Assert.Contains(id,AgencyDistributionRules.Parse(json)!);
        Assert.Null(AgencyDistributionRules.Parse(System.Text.Json.JsonSerializer.Serialize(new{demo=true,kind="agency-distribution",productVersionIds=new[]{id,id}})));
    }
}
