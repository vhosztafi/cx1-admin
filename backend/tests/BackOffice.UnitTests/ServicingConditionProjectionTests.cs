using System.Text.Json;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingConditionProjectionTests
{
    private static readonly Guid Driver=Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Premises=Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Start=new(2026,10,1,0,0,0,TimeSpan.Zero);
    private static JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value);
    private static ServicingEvidenceSlice Slice(int days,bool driver=true,string name="Alex",bool premises=true)=>new(Start.AddDays(days),
        Json(new{risk=new{drivers=driver?new[]{new{id=Driver,firstName=name,surname="Demo"}}:[],premises=premises?new[]{new{id=Premises}}:[],vehicles=Array.Empty<object>()}}),6);

    [Fact]
    public void RemovedDriverCanReceiveAnEarlierDatedConditionButNotALaterOne()
    {
        var definition=Json(new{code="provide-driver-proof",driverId=Driver,requirementCode="driving-record"});
        var a=Slice(0);var b=Slice(14,false);
        var parsed=Assert.Single(ServicingConditionRules.Parse(definition,[a,b],[a.EffectiveAt]));
        Assert.Equal(a.EffectiveAt,parsed.EffectiveAt);Assert.Equal(Driver,Assert.Single(parsed.Condition.TargetIds));
        Assert.Throws<ArgumentException>(()=>ServicingConditionRules.Parse(definition,[a,b],[b.EffectiveAt]));
        Assert.Throws<ArgumentException>(()=>ServicingConditionRules.Parse(definition,[a,b],[a.EffectiveAt,b.EffectiveAt]));
    }

    [Fact]
    public void NamedDriverWordingUsesEachDatedCapturedName()
    {
        var a=Slice(0);var b=Slice(14,name:"Sam");
        var rows=ServicingConditionRules.Parse(Json(new{code="named-drivers-only",driverIds=new[]{Driver},wordingVersion="1"}),[a,b],[a.EffectiveAt,b.EffectiveAt]);
        Assert.Contains("Alex Demo",rows[0].Condition.Wording);Assert.Contains("Sam Demo",rows[1].Condition.Wording);
        Assert.Equal(rows[0].Condition.DefinitionJson,rows[1].Condition.DefinitionJson);
    }

    [Fact]
    public void PremisesAndVehicleTargetsCannotBeBorrowedFromOtherDates()
    {
        var a=Slice(0);var b=Slice(14,premises:false);
        var definition=Json(new{code="overnight-security",premisesId=Premises,wordingVersion="1"});
        Assert.Equal("W-07",Assert.Single(ServicingConditionRules.Parse(definition,[a,b],[a.EffectiveAt])).Condition.EndorsementCode);
        Assert.Throws<ArgumentException>(()=>ServicingConditionRules.Parse(definition,[a,b],[b.EffectiveAt]));
        Assert.Throws<ArgumentException>(()=>ServicingConditionRules.Parse(Json(new{code="revise-vehicle-limit",vehicleId=Guid.NewGuid(),maximumAmount="1000.00"}),[a],[a.EffectiveAt]));
    }

    [Fact]
    public void UnknownDefinitionsAndMalformedOrUnownedDatesFailClosed()
    {
        var a=Slice(0);var b=Slice(14);var definition=Json(new{code="provide-trading-history"});
        foreach(var dates in new DateTimeOffset[][]{[],[a.EffectiveAt,a.EffectiveAt],[b.EffectiveAt,a.EffectiveAt],[Start.AddDays(1)],[a.EffectiveAt.ToOffset(TimeSpan.FromHours(1))]})
            Assert.Throws<ArgumentException>(()=>ServicingConditionRules.Parse(definition,[a,b],dates));
        foreach(var slices in new ServicingEvidenceSlice[][]{[],[b,a],[a,a],[a,null!],[a with{Proposal=Json("invalid")}],Enumerable.Repeat(a,101).ToArray()})
            Assert.Throws<ArgumentException>(()=>ServicingConditionRules.Parse(definition,slices,[a.EffectiveAt]));
        Assert.Throws<ArgumentException>(()=>ServicingConditionRules.Parse(Json(new{code="provide-trading-history",overrideAll=true}),[a],[a.EffectiveAt]));
    }

    [Fact]
    public void UnselectedMalformedStableItemsCannotHideBehindSelectedDates()
    {
        var a=Slice(0);var b=Slice(14) with{Proposal=Json(new{risk=new{drivers=new[]{new{id=Driver},new{id=Driver}}}})};
        Assert.Throws<ArgumentException>(()=>ServicingConditionRules.Parse(Json(new{code="provide-trading-history"}),[a,b],[a.EffectiveAt]));
        Assert.Throws<ArgumentException>(()=>ServicingConditionRules.Parse(Json(new{code="provide-trading-history"}),[a,Slice(14) with{Proposal=Json(new{risk=new{drivers=new[]{new{id="not-an-id"}}}})}],[a.EffectiveAt]));
    }
}
