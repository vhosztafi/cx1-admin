using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialServicingRatingTests
{
    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value,System.Globalization.CultureInfo.InvariantCulture);
    private static ResolvedQuoteTerm Term() => new("annual",At("2026-01-01T00:00:00Z"),At("2027-01-01T00:00:00Z"),"Europe/London");
    private static CommercialRatingFacts Facts(decimal buildingsDelta = 0)
    {
        var facts=CommercialRatingTests.Facts();
        return facts with { Locations=facts.Locations.Select(x=>x with {Buildings=x.Buildings+buildingsDelta}).ToArray() };
    }
    [Theory]
    [InlineData(100000,"25.21","3.03","3.78","53.24","49.46")]
    [InlineData(0,"0.00","0.00","0.00","25.00","25.00")]
    [InlineData(-100000,"-25.21","-3.03","-3.78","-3.24","0.54")]
    public void IndependentSignedGoldensChargeOnlyRemainingDeltaAndOneCommercialFee(int buildingsDelta,string premium,string tax,string commission,string gross,string net)
    {
        // The published fixture's complete annual risk is4195.00. A100000
        // buildings change at5bps changes it by50.00;184/365 remains on1July.
        var result=CommercialServicingRatingRules.Rate(CommercialRatingTests.Configuration(),Term(),4195m,
            [new CommercialServicingRiskSlice(At("2026-06-30T23:00:00Z"),[Guid.NewGuid()],Facts(buildingsDelta))],1500,25m);
        static decimal Money(string value)=>decimal.Parse(value,System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Money(premium),result.Premium);Assert.Equal(Money(tax),result.Tax);Assert.Equal(Money(commission),result.BrokerCommission);
        Assert.Equal(Money(gross),result.GrossPayable);Assert.Equal(Money(net),result.NetDue);Assert.Equal(25m,result.Fee);
        Assert.Equal(184,Assert.Single(result.Slices).RemainingDays);AssertPosting(result);
    }
    [Fact]
    public void TemporaryCoverPricesEachIncrementAgainstItsPredecessorAndBalancesOnce()
    {
        var first=Guid.NewGuid();var second=Guid.NewGuid();
        var result=CommercialServicingRatingRules.Rate(CommercialRatingTests.Configuration(),Term(),4195m,
            [new CommercialServicingRiskSlice(At("2026-03-01T00:00:00Z"),[first],Facts(200000)),
             new CommercialServicingRiskSlice(At("2026-08-31T23:00:00Z"),[first,second],Facts())],1500,25m);
        Assert.Equal(100m,result.Slices[0].AnnualDelta);Assert.Equal(-100m,result.Slices[1].AnnualDelta);
        Assert.Equal(83.84m,result.Slices[0].Premium);Assert.Equal(-33.42m,result.Slices[1].Premium);
        Assert.Equal(50.42m,result.Premium);Assert.Equal(6.05m,result.Tax);Assert.Equal(7.57m,result.BrokerCommission);
        Assert.Equal(81.47m,result.GrossPayable);Assert.Equal(73.90m,result.NetDue);Assert.Equal(25m,result.Fee);AssertPosting(result);
    }
    [Fact]
    public void WrongProductOrNonCumulativeScheduleCannotBecomeACommercialPrice()
    {
        var config=CommercialRatingTests.Configuration();var id=Guid.NewGuid();
        Assert.Throws<ArgumentException>(()=>CommercialServicingRatingRules.Rate(config,Term(),4195m,
            [new CommercialServicingRiskSlice(At("2026-03-01T00:00:00Z"),[id],Facts()),
             new CommercialServicingRiskSlice(At("2026-04-01T00:00:00Z"),[id],Facts(100000))],1500,25m));
        var wrong=System.Text.Json.Nodes.JsonNode.Parse(config.GetRawText())!;wrong["productCode"]="motor-trade-combined";
        Assert.Throws<ArgumentException>(()=>CommercialServicingRatingRules.Rate(System.Text.Json.JsonSerializer.SerializeToElement(wrong),Term(),4195m,
            [new CommercialServicingRiskSlice(At("2026-03-01T00:00:00Z"),[id],Facts())],1500,25m));
    }
    private static void AssertPosting(CalculatedServicingRating rating)
    {
        var movements=new List<ServicingPostingMovement>();
        for(var i=0;i<rating.Slices.Count;i++)
        {
            var slice=rating.Slices[i];var n=i+1;
            movements.Add(new("premium",n,slice.Premium,slice.EffectiveAt,slice.CoverageEndsAt));
            movements.Add(new("tax",n,slice.Tax,slice.EffectiveAt,slice.CoverageEndsAt));
            movements.Add(new("commission",n,slice.BrokerCommission,slice.EffectiveAt,slice.CoverageEndsAt));
        }
        movements.Add(new("fee",1,rating.Fee,rating.Slices[0].EffectiveAt,Term().EndsAt));
        movements.Add(new("fee-share",1,0,rating.Slices[0].EffectiveAt,Term().EndsAt));
        foreach(var settlement in new[]{"net-remittance","separate-payment"})
        {
            var posted=ServicingPostingRules.Calculate(new("adjustment","agency",settlement,movements));
            Assert.Equal(posted.Lines.Sum(x=>x.Debit),posted.Lines.Sum(x=>x.Credit));
            Assert.Equal(rating.GrossPayable,posted.GrossDue);Assert.Equal(rating.NetDue,posted.NetDue);
            Assert.Equal(25m,Assert.Single(posted.Movements,x=>x.Code=="fee").Amount);
        }
    }
}
