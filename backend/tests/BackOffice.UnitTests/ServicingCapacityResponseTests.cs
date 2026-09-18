using System.Text.Json;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingCapacityResponseTests
{
    private static readonly DateTimeOffset Now = new(2026,9,18,9,0,0,TimeSpan.Zero);
    private static readonly Guid Premises = Guid.NewGuid();
    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
    private static ServicingEvidenceSlice Slice(DateTimeOffset at, Guid premises) => new(at,
        JsonSerializer.SerializeToElement(new {risk=new {premises=new[]{new {id=premises}},drivers=Array.Empty<object>(),vehicles=Array.Empty<object>()},
            cover=new {requestedSections=new[]{new {code="premises",selected=true,premisesIds=new[]{premises}}}}}), 10);
    private static ServicingCapacityResponseDefinition Definition => new("approve", Now, Now.AddYears(1),
        [Json("""{"dimension":"stock-limit","maximumAmount":"150000.00"}""")], []);
    private static void Parse(ServicingCapacityResponseDefinition value, IReadOnlyList<ServicingEvidenceSlice>? slices = null,
        DateTimeOffset? receivedAt = null) => ServicingCapacityResponseRules.Parse(value, "cover-stock-custody", "cover-selection",
            Now.AddMinutes(-1), receivedAt ?? Now, Now, slices ?? [Slice(Now,Premises),Slice(Now.AddMonths(1),Premises)]);

    [Fact]
    public void ApprovalRequiresExactDimensionClosedLimitsAndUtcExtent()
    {
        Parse(Definition);
        Assert.Throws<ArgumentException>(()=>Parse(Definition with { ValidFrom=null }));
        Assert.Throws<ArgumentException>(()=>Parse(Definition with { ValidTo=Now }));
        Assert.Throws<ArgumentException>(()=>Parse(Definition with { ValidFrom=Now.ToOffset(TimeSpan.FromHours(1)) }));
        Assert.Throws<ArgumentException>(()=>Parse(Definition with { AuthorisedLimits=[] }));
        Assert.Throws<ArgumentException>(()=>Parse(Definition with { AuthorisedLimits=[Json("""{"dimension":"tools-limit","maximumAmount":"150000.00"}""")] }));
        Assert.Throws<ArgumentException>(()=>Parse(Definition with { AuthorisedLimits=[Definition.AuthorisedLimits[0],Definition.AuthorisedLimits[0]] }));
        Assert.Throws<ArgumentException>(()=>Parse(Definition with { AuthorisedLimits=[Json("""{"dimension":"stock-limit","maximumAmount":"150000.00","waiveProof":true}""")] }));
    }

    [Fact]
    public void QueryAndDeclineCannotSmuggleApprovalExtentOrConditions()
    {
        foreach(var outcome in new[]{"query","decline"})
        {
            var response = Definition with { Outcome=outcome, ValidFrom=null,ValidTo=null,AuthorisedLimits=[] };
            Parse(response);
            Assert.Throws<ArgumentException>(()=>Parse(response with { AuthorisedLimits=Definition.AuthorisedLimits }));
            Assert.Throws<ArgumentException>(()=>Parse(response with { ValidFrom=Now }));
            Assert.Throws<ArgumentException>(()=>Parse(response with { Conditions=[Condition()] }));
        }
        Assert.Throws<ArgumentException>(()=>Parse(Definition with { Outcome="approved" }));
    }

    private static ServicingCarrierConditionInput Condition() => new(
        Json($$"""{"code":"overnight-security","premisesId":"{{Premises}}","wordingVersion":"1"}"""), [Now,Now.AddMonths(1)]);

    [Fact]
    public void ConditionsValidateTargetsInEveryExplicitDatedRisk()
    {
        var response = Definition with { Outcome="approve-with-conditions",Conditions=[Condition()] };
        Parse(response);
        Assert.Throws<ArgumentException>(()=>Parse(response, [Slice(Now,Premises),Slice(Now.AddMonths(1),Guid.NewGuid())]));
        Assert.Throws<ArgumentException>(()=>Parse(response with { Conditions=[] }));
        Assert.Throws<ArgumentException>(()=>Parse(response with { Conditions=[Condition(),Condition()] }));
        Assert.Throws<ArgumentException>(()=>Parse(response with { Conditions=[Condition() with { EffectiveDates=[Now.AddDays(2)] }] }));
        Assert.Throws<ArgumentException>(()=>Parse(Definition with { Conditions=[Condition()] }));
    }

    [Fact]
    public void ReplyTimeAndWholeScheduleRemainBoundedAndOrdered()
    {
        Assert.Throws<ArgumentException>(()=>Parse(Definition,receivedAt:Now.AddTicks(1)));
        Assert.Throws<ArgumentException>(()=>Parse(Definition,receivedAt:Now.AddMinutes(-2)));
        Assert.Throws<ArgumentException>(()=>Parse(Definition,receivedAt:Now.ToOffset(TimeSpan.FromHours(1))));
        Assert.Throws<ArgumentException>(()=>Parse(Definition,[]));
        Assert.Throws<ArgumentException>(()=>Parse(Definition,[Slice(Now.AddMonths(1),Premises),Slice(Now,Premises)]));
    }
}
