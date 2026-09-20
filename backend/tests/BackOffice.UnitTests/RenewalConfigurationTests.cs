using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class RenewalConfigurationTests
{
    private const string Definition="""
        {"demo":true,"kind":"renewal-preparation","schemaVersion":"1","ruleVersion":"demo-servicing-1","currency":"GBP",
         "allowedTermMonths":[6,12],"defaultTermMonths":12,"lossRatioThresholdBasisPoints":5000,"experienceLoadingBasisPoints":800,
         "renewalFee":"35.00","invitationDaysBeforeExpiry":45,"lapseDaysAfterExpiry":14}
        """;

    [Fact]
    public void CommercialRenewalRequiresItsOwnExplicitPublishedScope()
    {
        var commercial=Definition.Replace("renewal-preparation","commercial-renewal-preparation").Replace("35.00","45.00");
        Assert.Null(RenewalConfiguration.Parse(commercial));
        Assert.Null(RenewalConfiguration.Parse(Definition,"commercial-renewal-preparation"));
        Assert.Null(RenewalConfiguration.Parse(commercial,"caller-supplied-scope"));
        Assert.Equal(45m,Assert.IsType<RenewalSettings>(RenewalConfiguration.Parse(commercial,"commercial-renewal-preparation")).RenewalFee);
    }

    [Fact]
    public void PublishedRenewalRulesCarryActualCalendarPricingAndWorkflowChoices()
    {
        var parsed=Assert.IsType<RenewalSettings>(RenewalConfiguration.Parse(Definition));
        Assert.Equal(new[]{6,12},parsed.AllowedTermMonths);Assert.Equal(12,parsed.DefaultTermMonths);
        Assert.Equal(5000,parsed.LossRatioThresholdBasisPoints);Assert.Equal(800,parsed.ExperienceLoadingBasisPoints);
        Assert.Equal(35m,parsed.RenewalFee);Assert.Equal(45,parsed.InvitationDaysBeforeExpiry);Assert.Equal(14,parsed.LapseDaysAfterExpiry);
    }

    [Theory]
    [InlineData("allowedTermMonths","[6,6]")]
    [InlineData("allowedTermMonths","[13]")]
    [InlineData("allowedTermMonths","[]")]
    [InlineData("defaultTermMonths","3")]
    [InlineData("lossRatioThresholdBasisPoints","-1")]
    [InlineData("experienceLoadingBasisPoints","10001")]
    [InlineData("renewalFee","\"35.001\"")]
    [InlineData("renewalFee","35")]
    [InlineData("invitationDaysBeforeExpiry","366")]
    [InlineData("lapseDaysAfterExpiry","-1")]
    [InlineData("currency","\"USD\"")]
    public void InvalidPublishedSettingsFailClosed(string field,string value)
    {
        var changed=JsonNode.Parse(Definition)!;changed[field]=JsonNode.Parse(value);
        Assert.Null(RenewalConfiguration.Parse(changed.ToJsonString()));
    }

    [Fact]
    public void MissingDuplicateAndUnknownSettingsAreNotSilentlyDefaulted()
    {
        Assert.Null(RenewalConfiguration.Parse("{}"));
        Assert.Null(RenewalConfiguration.Parse(Definition.Replace("\"currency\":\"GBP\"","\"currency\":\"GBP\",\"currency\":\"GBP\"",StringComparison.Ordinal)));
        Assert.Null(RenewalConfiguration.Parse(Definition.Replace("\"demo\":true","\"demo\":true,\"waiveExperience\":true",StringComparison.Ordinal)));
    }
}
