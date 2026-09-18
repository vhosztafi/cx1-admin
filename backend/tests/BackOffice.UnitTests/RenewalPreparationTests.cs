using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class RenewalPreparationTests
{
    [Theory]
    [InlineData(12,"648.00","77.76","64.80","760.76")]
    [InlineData(6,"321.34","38.56","32.13","394.90")]
    public void RenewalRatesTheFullNextTermWithConfiguredExperienceLoading(int months,string premium,string tax,string commission,string gross)
    {
        var term=RenewalPreparationRules.Term(DateTimeOffset.Parse("2027-01-01T00:00:00Z"),months,[6,12]).Term;
        var rated=RenewalRatingRules.Calculate(QuoteRatingRulesTests.Definition("rating"),QuoteRatingRulesTests.Facts(),term,1000,
            Experience(500.01m),true,Now,5000,800,35m);
        decimal Amount(string text)=>decimal.Parse(text,System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(648m,rated.Price.AnnualPremium);Assert.Equal(Amount(premium),rated.Price.TermPremium);
        Assert.Equal(Amount(tax),rated.Price.Tax);Assert.Equal(Amount(commission),rated.Price.BrokerCommission);Assert.Equal(Amount(gross),rated.Price.GrossPayable);
        Assert.Equal("UW-31",rated.Experience.State);Assert.True(rated.Experience.RequiresSeniorDecision);
        Assert.Equal(48m,Assert.Single(rated.Price.Factors,x=>x.Code=="renewal-experience").Amount);
    }

    [Fact]
    public void RenewalUnknownExperienceDoesNotInventALoadingOrACompleteAssessment()
    {
        var rated=RenewalRatingRules.Calculate(QuoteRatingRulesTests.Definition("rating"),QuoteRatingRulesTests.Facts(),
            QuoteRatingRulesTests.Term(),1000,null,false,Now,5000,800,15m);
        Assert.Equal(600m,rated.Price.TermPremium);Assert.Equal(687m,rated.Price.GrossPayable);
        Assert.False(rated.Experience.InformationComplete);Assert.Null(rated.Experience.LossRatio);
        Assert.DoesNotContain(rated.Price.Factors,x=>x.Code=="renewal-experience");
        Assert.Throws<ArgumentException>(()=>RenewalRatingRules.Calculate(QuoteRatingRulesTests.Definition("rating"),QuoteRatingRulesTests.Facts(),
            QuoteRatingRulesTests.Term(),1000,Experience(),true,Now,5000,800,-1m));
    }

    [Fact]
    public void ExperienceLoadingCannotBypassTheConfiguredAnnualMaximum()
    {
        var definition=System.Text.Json.Nodes.JsonNode.Parse(QuoteRatingRulesTests.Definition("rating").GetRawText())!;
        definition["maximumAnnualPremium"]="640.00";
        var config=System.Text.Json.JsonSerializer.SerializeToElement(definition);
        Assert.Equal(600m,QuoteRatingRules.Calculate(config,QuoteRatingRulesTests.Facts(),QuoteRatingRulesTests.Term(),1000).AnnualPremium);
        Assert.Throws<ArgumentException>(()=>RenewalRatingRules.Calculate(config,QuoteRatingRulesTests.Facts(),QuoteRatingRulesTests.Term(),
            1000,Experience(500.01m),true,Now,5000,800,35m));
    }

    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-09-18T12:00:00Z");
    private static RenewalExperienceFacts Experience(decimal paid=500m,decimal earned=1000m)=>new(new(2025,9,18),new(2026,9,18),1,paid,0m,earned,"agency","Fictional annual claims statement",Guid.NewGuid());

    [Theory]
    [InlineData("2024-02-29T00:00:00Z",12,"2025-02-28T00:00:00Z")]
    [InlineData("2026-08-30T23:00:00Z",6,"2027-02-28T00:00:00Z")]
    [InlineData("2026-09-30T23:00:00Z",12,"2027-09-30T23:00:00Z")]
    public void RenewalStartsAtTheExclusiveEndAndUsesLondonCalendarMonths(string end,int months,string expected)
    {
        var result=RenewalPreparationRules.Term(DateTimeOffset.Parse(end),months,[6,12]);
        Assert.Equal(DateTimeOffset.Parse(end),result.Term.StartsAt);
        Assert.Equal(DateTimeOffset.Parse(expected),result.Term.EndsAt);
        Assert.Equal(months==12?"annual":"short-period",result.Term.Kind);
    }

    [Fact]
    public void ShorterTermsRequireConfigurationAndDstGapIsNotSilentlyMoved()
    {
        Assert.Throws<ArgumentException>(()=>RenewalPreparationRules.Term(Now,6,[12]));
        Assert.Throws<ArgumentException>(()=>RenewalPreparationRules.Term(DateTimeOffset.Parse("2026-03-28T01:30:00Z"),12,[12]));
    }

    [Theory]
    [InlineData("499.99","1000.00",false,0)]
    [InlineData("500.00","1000.00",false,0)]
    [InlineData("500.01","1000.00",true,800)]
    public void LossRatioUsesExactValuesBeforeDisplayRounding(string paid,string earned,bool refer,int loading)
    {
        var assessment=RenewalPreparationRules.Experience(Experience(decimal.Parse(paid,System.Globalization.CultureInfo.InvariantCulture),decimal.Parse(earned,System.Globalization.CultureInfo.InvariantCulture)),true,Now,5000,800);
        Assert.True(assessment.InformationComplete);Assert.Equal(refer,assessment.RequiresSeniorDecision);Assert.Equal(loading,assessment.LoadingBasisPoints);
    }

    [Fact]
    public void UnknownUnreviewedAndZeroDenominatorNeverBecomeZeroPercent()
    {
        foreach(var result in new[]{RenewalPreparationRules.Experience(null,false,Now,5000,800),
            RenewalPreparationRules.Experience(Experience(),false,Now,5000,800),RenewalPreparationRules.Experience(Experience(0m,0m),true,Now,5000,800)})
        {Assert.False(result.InformationComplete);Assert.Null(result.LossRatio);Assert.Equal(0,result.LoadingBasisPoints);}
        var declaredZero=RenewalPreparationRules.Experience(Experience(0m),true,Now,5000,800);
        Assert.True(declaredZero.InformationComplete);Assert.Equal(0m,declaredZero.LossRatio);
    }

    [Fact]
    public void ExperienceRejectsFutureReversedOverprecisionAndUnownedEvidenceInputs()
    {
        var value=Experience();
        foreach(var invalid in new[]{value with{ObservationEndsOn=new(2026,9,19)},value with{ObservationStartsOn=value.ObservationEndsOn},
            value with{Paid=-1m},value with{Outstanding=0.001m},value with{EvidenceAssociationId=Guid.Empty},value with{SourceCode="invented"},value with{ClaimCount=100001}})
            Assert.Throws<ArgumentException>(()=>RenewalPreparationRules.Experience(invalid,true,Now,5000,800));
    }
}
