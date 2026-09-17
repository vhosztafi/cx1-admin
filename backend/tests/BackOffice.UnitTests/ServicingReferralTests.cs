using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingReferralTests
{
    private static readonly DateTimeOffset Start=new(2026,1,1,0,0,0,TimeSpan.Zero);
    private static readonly ResolvedQuoteTerm Term=new("annual",Start,Start.AddYears(1),"Europe/London");
    private static readonly Guid Driver=Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static UnderwritingRisk Risk()=>new(600m,0m,30000m,10,false,false,[1],[new(Driver,35,10,false,false)],new Dictionary<string,decimal>{{"road-risks",30000m}});
    private static JsonElement Config(string kind,Action<JsonNode>? change=null,string product="motor-trade-road-risks")
    {var node=JsonNode.Parse(QuoteRatingRulesTests.Definition(kind,product).GetRawText())!;change?.Invoke(node);return JsonSerializer.SerializeToElement(node);}
    private static ServicingAuthoritySlice Slice(int month,UnderwritingRisk? risk=null)=>new(Start.AddMonths(month),risk??Risk());

    [Fact]
    public void EarlierRemovedDriverKeepsItsDatedReferralAndDistinctSourceReasons()
    {
        var earlier=Risk() with {Drivers=[new(Driver,18,1,true,true)],HasClaims=true};
        var later=Risk() with {Drivers=[new(Guid.NewGuid(),35,10,false,false)]};
        var needs=ServicingReferralRules.Assess(Term,[Slice(2,earlier),Slice(3,later)],Config("binder"),Config("authority"));
        var age=Assert.Single(needs,x=>x.RuleCode=="driver-age" && x.RiskItemId==Driver);
        Assert.All(age.Triggers,x=>Assert.Equal(Start.AddMonths(2),x.EffectiveAt));
        Assert.Contains(needs,x=>x.RuleCode=="conviction-history" && x.RiskItemId==Driver && x.Triggers.Any(t=>t.Source=="source"));
        Assert.False(ServicingReferralRules.AuthorityAllows(Term,[Slice(2,earlier),Slice(3,later)],Config("binder"),Config("authority")));
        Assert.True(ServicingReferralRules.AuthorityAllows(Term,[Slice(3,later)],Config("binder"),Config("authority")));
    }

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void AmountTriggersRetainEachDateRequestedValueAndIndependentLimits(string product)
    {
        var binder=Config("binder",n=>n["limits"]!["stockLimit"]="200000.00",product);
        var authority=Config("authority",n=>n["limits"]!["stockLimit"]="100000.00",product);
        var needs=ServicingReferralRules.Assess(Term,[Slice(2,Risk() with {StockLimit=250000m}),Slice(3,Risk() with {StockLimit=150000m}),Slice(4)],binder,authority);
        var stock=Assert.Single(needs,x=>x.RuleCode=="stock-limit");Assert.Equal(3,stock.Triggers.Count);
        Assert.Contains(stock.Triggers,x=>x.EffectiveAt==Start.AddMonths(2) && x.Source=="binder" && x.Requirement.RequestedAmount==250000m && x.Requirement.AuthorisedAmount==200000m);
        Assert.Contains(stock.Triggers,x=>x.EffectiveAt==Start.AddMonths(3) && x.Source=="authority" && x.Requirement.RequestedAmount==150000m && x.Requirement.AuthorisedAmount==100000m);
        Assert.DoesNotContain(stock.Triggers,x=>x.EffectiveAt==Start.AddMonths(4));
    }

    [Fact]
    public void CurrentAuthorityDoesNotEraseIndependentSourceReferralOrSupplyProof()
    {
        var binder=Config("binder",n=>n["limits"]!["reviewClaims"]=true);
        var grant=Config("authority",n=>n["limits"]!["reviewClaims"]=true);
        var slices=new[]{Slice(2,Risk() with {HasClaims=true,Drivers=[new(Driver,35,10,false,true)]})};
        Assert.True(ServicingReferralRules.AuthorityAllows(Term,slices,binder,grant));
        var need=Assert.Single(ServicingReferralRules.Assess(Term,slices,binder,grant),x=>x.RuleCode=="claims-history");
        Assert.Equal("source",Assert.Single(need.Triggers).Source);
    }

    [Fact]
    public void AGrantMustCoverAllSlicesRatherThanCombiningPermissionsAcrossDates()
    {
        var binder=Config("binder",n=>{n["limits"]!["annualPremiumLimit"]="2000.00";n["limits"]!["stockLimit"]="200000.00";});
        var first=Config("authority",n=>{n["limits"]!["annualPremiumLimit"]="2000.00";n["limits"]!["stockLimit"]="100000.00";});
        var second=Config("authority",n=>{n["limits"]!["annualPremiumLimit"]="1000.00";n["limits"]!["stockLimit"]="200000.00";});
        var a=Slice(2,Risk() with {AnnualPremium=1500m});var b=Slice(3,Risk() with {StockLimit=150000m});
        Assert.True(ServicingReferralRules.AuthorityAllows(Term,[a],binder,first));Assert.True(ServicingReferralRules.AuthorityAllows(Term,[b],binder,second));
        Assert.False(ServicingReferralRules.AuthorityAllows(Term,[a,b],binder,first));Assert.False(ServicingReferralRules.AuthorityAllows(Term,[a,b],binder,second));
    }

    [Fact]
    public void InvalidGrantCeilingAndUnknownUnnamedDriverExperienceCannotAuthorize()
    {
        var binder=Config("binder",n=>n["limits"]!["annualPremiumLimit"]="1000.00");
        var grant=Config("authority",n=>n["limits"]!["annualPremiumLimit"]="2000.00");
        Assert.False(ServicingReferralRules.AuthorityAllows(Term,[Slice(2)],binder,grant));
        var unnamed=Risk() with {Drivers=[],AnyDriverCount=2,AnyDriverMinimumAge=30,AnyDriverMaximumAge=65};
        var needs=ServicingReferralRules.Assess(Term,[Slice(2,unnamed),Slice(3)],Config("binder"),Config("authority"));
        var experience=Assert.Single(needs,x=>x.RuleCode=="any-driver-licence-years");
        Assert.Null(experience.RiskItemId);Assert.All(experience.Triggers,x=>Assert.Equal(Start.AddMonths(2),x.EffectiveAt));
        Assert.False(ServicingReferralRules.AuthorityAllows(Term,[Slice(2,unnamed),Slice(3)],Config("binder"),Config("authority")));
    }

    [Fact]
    public void InvalidScheduleAndForeignOrSwappedConfigurationFailClosed()
    {
        var binder=Config("binder");var grant=Config("authority");var a=Slice(2);
        foreach(var slices in new ServicingAuthoritySlice[][]{[],[a,a],[Slice(3),a],[new(Term.EndsAt,Risk())],[new(Start.AddSeconds(-1),Risk())],[a with {EffectiveAt=a.EffectiveAt.ToOffset(TimeSpan.FromHours(1))}],[a,Slice(3,Risk() with {AnnualPremium=0m})]})
            Assert.Throws<ArgumentException>(()=>ServicingReferralRules.Assess(Term,slices,binder,grant));
        Assert.Throws<ArgumentException>(()=>ServicingReferralRules.Assess(Term,[a],grant,binder));
        Assert.Throws<ArgumentException>(()=>ServicingReferralRules.Assess(Term,[a],binder,Config("authority",product:"motor-trade-combined")));
        Assert.Throws<ArgumentException>(()=>ServicingReferralRules.Assess(Term,[a],binder,grant,-1));
    }
}
