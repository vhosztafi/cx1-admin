using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingConditionAuthorityTests
{
    private static readonly DateTimeOffset Start=new(2026,1,1,0,0,0,TimeSpan.Zero);
    private static readonly ResolvedQuoteTerm Term=new("annual",Start,Start.AddYears(1),"Europe/London");
    private static readonly JsonElement Proposal=JsonSerializer.SerializeToElement(new {risk=new {drivers=Array.Empty<object>()}});
    private static ReferralCondition Warranty(int years=2)=>ReferralRules.Condition(
        JsonSerializer.SerializeToElement(new {code="any-driver-minimum-licence",minimumYears=years,wordingVersion="1"}),Proposal);
    private static ServicingAuthoritySlice Slice(int month)=>new(Start.AddMonths(month),
        new(600m,0m,30000m,10,false,false,[1],[],new Dictionary<string,decimal>{{"road-risks",30000m}})
            {AnyDriverCount=2,AnyDriverMinimumAge=30,AnyDriverMaximumAge=65});

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void WarrantyMustApplyToEveryDateWithUnknownUnnamedDriverExperience(string product)
    {
        var binder=QuoteRatingRulesTests.Definition("binder",product);var grant=QuoteRatingRulesTests.Definition("authority",product);
        var a=Slice(2);var b=Slice(3);var warranty=Warranty();
        Assert.False(ServicingReferralRules.AuthorityAllows(Term,[a,b],binder,grant,[new(a.EffectiveAt,[warranty])]));
        Assert.False(ServicingReferralRules.AuthorityAllows(Term,[a,b],binder,grant,[new(b.EffectiveAt,[warranty])]));
        Assert.True(ServicingReferralRules.AuthorityAllows(Term,[a,b],binder,grant,[new(a.EffectiveAt,[warranty]),new(b.EffectiveAt,[warranty])]));
        Assert.False(ServicingReferralRules.AuthorityAllows(Term,[a,b],binder,grant));
    }

    [Fact]
    public void WarrantyCannotWaiveOtherDimensionsOrAHigherLicenceMinimum()
    {
        var binder=QuoteRatingRulesTests.Definition("binder");var grant=QuoteRatingRulesTests.Definition("authority");
        var a=Slice(2);var conditions=new[]{new ServicingConditionSlice(a.EffectiveAt,[Warranty()])};
        foreach(var risk in new[]{a.Risk with{StockLimit=10000000m},a.Risk with{AnnualPremium=10000000m},a.Risk with{AnyDriverMinimumAge=16}})
            Assert.False(ServicingReferralRules.AuthorityAllows(Term,[a with{Risk=risk}],binder,grant,conditions));
        var high=JsonNode.Parse(grant.GetRawText())!;high["limits"]!["minimumLicenceYears"]=3;
        Assert.False(ServicingReferralRules.AuthorityAllows(Term,[a],binder,JsonSerializer.SerializeToElement(high),conditions));
    }

    [Fact]
    public void DocumentaryProofDoesNotSupplyContractualWarrantyOrInventDriverExperience()
    {
        var a=Slice(2);var proof=ReferralRules.Condition(JsonSerializer.SerializeToElement(new{code="provide-trading-history"}),Proposal);
        Assert.False(ServicingReferralRules.AuthorityAllows(Term,[a],QuoteRatingRulesTests.Definition("binder"),QuoteRatingRulesTests.Definition("authority"),[new(a.EffectiveAt,[proof])]));
        Assert.Empty(a.Risk.Drivers);
    }

    [Fact]
    public void ForeignDuplicateUnboundedAndNullConditionDatesFailClosed()
    {
        var a=Slice(2);var row=new ServicingConditionSlice(a.EffectiveAt,[Warranty()]);
        foreach(var conditions in new ServicingConditionSlice[][]{[row,row],[row with{EffectiveAt=Start.AddMonths(3)}],
            [row with{EffectiveAt=a.EffectiveAt.ToOffset(TimeSpan.FromHours(1))}],[row with{Conditions=Enumerable.Repeat(Warranty(),101).ToArray()}],
            [row with{Conditions=null!}],[row with{Conditions=[null!]}],[null!]})
            Assert.Throws<ArgumentException>(()=>ServicingReferralRules.AuthorityAllows(Term,[a],QuoteRatingRulesTests.Definition("binder"),QuoteRatingRulesTests.Definition("authority"),conditions));
    }

    [Fact]
    public void ConditionsCannotHideMalformedLaterRiskOrMakeAnOverBinderGrantEligible()
    {
        var a=Slice(2);var b=Slice(3);var binder=QuoteRatingRulesTests.Definition("binder");
        var grant=QuoteRatingRulesTests.Definition("authority");
        Assert.Throws<ArgumentException>(()=>ServicingReferralRules.AuthorityAllows(Term,[a,b with{Risk=b.Risk with{AnnualPremium=0m}}],binder,grant,[new(a.EffectiveAt,[Warranty()])]));
        var high=JsonNode.Parse(grant.GetRawText())!;high["limits"]!["annualPremiumLimit"]="99999999.00";
        Assert.False(ServicingReferralRules.AuthorityAllows(Term,[a],binder,JsonSerializer.SerializeToElement(high),[new(a.EffectiveAt,[Warranty()])]));
    }

    [Fact]
    public void TheSameActiveConditionsMayApplyAcrossTheWholeBoundedSchedule()
    {
        var slices=Enumerable.Range(1,100).Select(day=>Slice(2) with{EffectiveAt=Start.AddDays(day)}).ToArray();
        var proof=ReferralRules.Condition(JsonSerializer.SerializeToElement(new{code="provide-trading-history"}),Proposal);
        var conditions=slices.Select(x=>new ServicingConditionSlice(x.EffectiveAt,[Warranty(),proof])).ToArray();
        Assert.True(ServicingReferralRules.AuthorityAllows(Term,slices,QuoteRatingRulesTests.Definition("binder"),QuoteRatingRulesTests.Definition("authority"),conditions));
    }
}
