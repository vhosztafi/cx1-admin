using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingEvidenceTests
{
    private static readonly Guid Driver = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Premises = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static ServicingProofContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a',64),
        new(Guid.NewGuid(),Guid.NewGuid(),"1.0","1","1"));
    private static ServicingEvidenceSlice Slice(int day, bool driver = true, bool premises = false, int tradingYears = 6) => new(
        new DateTimeOffset(2026,10,day,0,0,0,TimeSpan.Zero),
        JsonSerializer.SerializeToElement(new { productCode = "motor-trade-combined", risk = new { drivers = driver ? new[] { new { id = Driver, evidenceReceived = true } } : [],
            premises = new[] { new { id = Premises } } }, cover = new { requestedSections = new[] { new { code = "premises", selected = premises, premisesIds = new[] { Premises } } } } }), tradingYears);

    [Fact]
    public void CurrentConditionsCanRequestTradingHistoryForAnEstablishedBusiness()
    {
        var context=Context();var a=Slice(1);var b=Slice(15);
        Assert.DoesNotContain(ServicingEvidenceRules.Requirements(context,[a,b]),x=>x.Code=="trading-history");
        var requested=Assert.Single(ServicingEvidenceRules.Requirements(context,[a,b],[a.EffectiveAt]),x=>x.Code=="trading-history");
        Assert.Equal(new[]{a.EffectiveAt},requested.EffectiveDates);
        Assert.NotEqual(requested.InputFingerprint,Assert.Single(ServicingEvidenceRules.Requirements(context,[a,b],[a.EffectiveAt,b.EffectiveAt]),x=>x.Code=="trading-history").InputFingerprint);
        foreach(var invalid in new[]{new[]{a.EffectiveAt,a.EffectiveAt},new[]{b.EffectiveAt,a.EffectiveAt},new[]{a.EffectiveAt.AddDays(1)},new[]{a.EffectiveAt.ToOffset(TimeSpan.FromHours(1))}})
            Assert.Throws<ArgumentException>(()=>ServicingEvidenceRules.Requirements(context,[a,b],invalid));
    }

    [Fact]
    public void RequirementsRetainEarlierDriverAndPremisesEvenWhenRemovedFromLaterRisk()
    {
        var requirements = ServicingEvidenceRules.Requirements(Context(), [Slice(1,premises:true,tradingYears:2),Slice(15,driver:false)]);
        var licence = Assert.Single(requirements,x => x.Code == "photocard-both-sides");
        Assert.Equal(Driver,licence.RiskItemId); Assert.Single(licence.EffectiveDates);
        Assert.Contains(requirements,x=>x.Code=="driving-record"&&x.RiskItemId==Driver);
        Assert.Contains(requirements,x=>x.Code=="premises-security"&&x.RiskItemId==Premises);
        Assert.Contains(requirements,x=>x.Code=="trading-history");
        Assert.Equal(2,Assert.Single(requirements,x=>x.Code=="motor-trader-proof").EffectiveDates.Count);
        Assert.All(requirements,x=>Assert.Matches("^[a-f0-9]{64}$",x.InputFingerprint));
    }

    [Fact]
    public void StableItemRequirementsAggregateAcrossDatesAndBindEveryOwnershipBoundary()
    {
        var context=Context(); var slices=new[]{Slice(1),Slice(15)};
        var original=ServicingEvidenceRules.Requirements(context,slices);
        Assert.Equal(3,original.Count);Assert.All(original,x=>Assert.Equal(2,x.EffectiveDates.Count));
        foreach(var changed in new[]{context with{DraftId=Guid.NewGuid()},context with{CycleId=Guid.NewGuid()},context with{RevisionId=Guid.NewGuid()},
            context with{RatingId=Guid.NewGuid()},context with{InputHash=new string('b',64)},context with{Pins=context.Pins with{AgencyTermsVersionId=Guid.NewGuid()}}})
            Assert.NotEqual(original[0].InputFingerprint,ServicingEvidenceRules.Requirements(changed,slices)[0].InputFingerprint);
        Assert.NotEqual(original[0].InputFingerprint,ServicingEvidenceRules.Requirements(context,[Slice(1),Slice(16)])[0].InputFingerprint);
        Assert.NotEqual(original[0].InputFingerprint,ServicingEvidenceRules.Requirements(context,[Slice(1),Slice(15,tradingYears:2)])[0].InputFingerprint);
    }

    [Fact]
    public void ProofRequiresItsOwnCurrentAcceptedReviewAndExactPurposeAndOwner()
    {
        var context=Context();var requirement=ServicingEvidenceRules.Requirements(context,[Slice(1)]).Single(x=>x.Code=="driving-record");
        var proof=new ServicingReviewedProof(context.DraftId,context.CycleId,context.RevisionId,context.RatingId,requirement.Code,Driver,
            requirement.InputFingerprint,"accepted","accepted",false);
        Assert.True(ServicingEvidenceRules.Satisfied(context,requirement,proof));
        Assert.False(ServicingEvidenceRules.Satisfied(context with{InputHash=new string('b',64)},requirement,proof));
        Assert.False(ServicingEvidenceRules.Satisfied(context with{Pins=context.Pins with{ProductVersionId=Guid.NewGuid()}},requirement,proof));
        foreach(var invalid in new[]{proof with{DraftId=Guid.NewGuid()},proof with{CycleId=Guid.NewGuid()},proof with{RevisionId=Guid.NewGuid()},proof with{RatingId=Guid.NewGuid()},
            proof with{Code="photocard-both-sides"},proof with{RiskItemId=Premises},proof with{InputFingerprint=new string('b',64)},
            proof with{ScreeningState="pending"},proof with{ReviewState="unreviewed"},proof with{ReviewState="rejected"},proof with{Withdrawn=true}})
            Assert.False(ServicingEvidenceRules.Satisfied(context,requirement,invalid));
    }

    [Fact]
    public void RejectsMalformedIdentityScheduleAndForeignPremisesPurpose()
    {
        var context=Context();Assert.Throws<ArgumentException>(()=>ServicingEvidenceRules.Requirements(context,[]));
        Assert.Throws<ArgumentException>(()=>ServicingEvidenceRules.Requirements(context,[Slice(15),Slice(1)]));
        Assert.Throws<ArgumentException>(()=>ServicingEvidenceRules.Requirements(context,[Slice(1),Slice(1)]));
        Assert.Throws<ArgumentException>(()=>ServicingEvidenceRules.Requirements(context with{CycleId=Guid.Empty},[Slice(1)]));
        Assert.Throws<ArgumentException>(()=>ServicingEvidenceRules.Requirements(context with{InputHash="forged"},[Slice(1)]));
        var foreign=JsonSerializer.SerializeToElement(new{risk=new{drivers=Array.Empty<object>(),premises=new[]{new{id=Premises}}},
            cover=new{requestedSections=new[]{new{code="premises",selected=true,premisesIds=new[]{Guid.NewGuid()}}}}});
        Assert.Throws<ArgumentException>(()=>ServicingEvidenceRules.Requirements(context,[Slice(1) with{Proposal=foreign}]));
        var duplicate=JsonSerializer.SerializeToElement(new{risk=new{drivers=new[]{new{id=Driver},new{id=Driver}}}});
        Assert.Throws<ArgumentException>(()=>ServicingEvidenceRules.Requirements(context,[Slice(1) with{Proposal=duplicate}]));
    }
}
