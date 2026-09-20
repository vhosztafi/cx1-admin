using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialRenewalRatingTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-09-15T12:00:00Z");
    private static readonly ResolvedQuoteTerm Term=new("annual",DateTimeOffset.Parse("2026-10-01T23:00:00Z"),DateTimeOffset.Parse("2027-10-01T23:00:00Z"),"Europe/London");
    private static RenewalExperienceFacts Experience(decimal paid=0)=>new(new(2025,9,1),new(2026,9,1),paid==0?0:1,paid,0,1000,"agency","Fictional full commercial schedule experience",Guid.NewGuid());

    [Fact]
    public void MissingExperienceCannotBecomeAReviewedZeroLossDeclaration()
    {
        var missing=Rate(null,true);var zero=Rate(Experience(),true);var unreviewed=Rate(Experience(),false);
        Assert.False(missing.Experience.InformationComplete);Assert.Equal("missing-experience",missing.Experience.State);
        Assert.True(zero.Experience.InformationComplete);Assert.Equal(0m,zero.Experience.LossRatio);
        Assert.False(unreviewed.Experience.InformationComplete);Assert.Equal("experience-evidence-required",unreviewed.Experience.State);
        Assert.Equal(4195m,zero.Price.AnnualPremium);Assert.Equal(503.40m,zero.Price.Tax);Assert.Equal(629.25m,zero.Price.BrokerCommission);
        Assert.Equal(45m,zero.Price.Fee);Assert.Equal(4743.40m,zero.Price.GrossPayable);
    }

    [Fact]
    public void ReviewedAdverseExperienceLoadsTheFullCommercialRiskAndChargesOneRenewalFee()
    {
        var price=Rate(Experience(600),true);
        Assert.True(price.Experience.RequiresSeniorDecision);Assert.Equal(800,price.Experience.LoadingBasisPoints);
        Assert.Equal(4530.60m,price.Price.AnnualPremium);Assert.Equal(543.67m,price.Price.Tax);
        Assert.Equal(679.59m,price.Price.BrokerCommission);Assert.Equal(45m,price.Price.Fee);Assert.Equal(5119.27m,price.Price.GrossPayable);
        Assert.Equal(335.60m,Assert.Single(price.Price.Factors,x=>x.Code=="renewal-experience").Amount);
    }

    [Fact]
    public void ExactThresholdDoesNotLoadButZeroEarnedPremiumRemainsUnresolved()
    {
        Assert.Equal(0,Rate(Experience(500),true).Experience.LoadingBasisPoints);
        Assert.Equal("zero-earned-premium",Rate(Experience() with {EarnedPremium=0},true).Experience.State);
    }

    [Fact]
    public void ForeignProductAndOverMaximumLoadedPremiumCannotBecomeCommercialRenewalPrices()
    {
        var config=JsonNode.Parse(CommercialRatingTests.Configuration().GetRawText())!;
        config["productCode"]="motor-trade-combined";
        Assert.Throws<ArgumentException>(()=>Rate(Experience(),true,JsonSerializer.SerializeToElement(config)));
        config=JsonNode.Parse(CommercialRatingTests.Configuration().GetRawText())!;config["maximumAnnualPremium"]="4400.00";
        Assert.Throws<ArgumentException>(()=>Rate(Experience(600),true,JsonSerializer.SerializeToElement(config)));
    }

    private static CalculatedRenewalRating Rate(RenewalExperienceFacts? experience,bool reviewed,JsonElement? config=null)=>
        RenewalRatingRules.CalculateCommercial(config??CommercialRatingTests.Configuration(),CommercialRatingTests.Facts(),Term,1500,experience,reviewed,Now,5000,800,45m);
}
