using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;
public sealed class CommercialServicingCapacityTests
{
    private static JsonElement Proposal()
    {
        using var stream=typeof(CommercialServicingCapacityTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!;
        using var document=JsonDocument.Parse(stream);return document.RootElement.Clone();
    }
    [Fact]
    public void CommercialExtentIsClosedAndBindsSubjectDatesAndSubmission()
    {
        var proposal=Proposal();var location=proposal.GetProperty("risk").GetProperty("locations")[0].GetProperty("id").GetGuid();
        var now=DateTimeOffset.Parse("2026-09-20T00:00:00Z");var start=now.AddDays(11);var end=start.AddMonths(6);
        var definition=new ServicingCapacityResponseDefinition("approve",now,end,
            [JsonSerializer.SerializeToElement(new {dimension="single-location",maximumAmount="3000000.00",riskItemId=location})],[]);
        var parsed=ServicingCapacityResponseRules.Parse(definition,"AU-05","single-location",now,now,now,[new(start,proposal,null)]);
        Assert.Empty(parsed.Extensions);Assert.Single(parsed.CommercialExtensions!);
        var subject=new ServicingCapacitySubject(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new string('a',64));
        var response=new ServicingCapacityResponse(Guid.NewGuid(),subject,"approve",now,end,[]){CommercialExtensions=parsed.CommercialExtensions};
        var exposure=new ServicingCapacityExposure("single-location",location,start,end,2500000.01m);
        Assert.True(ServicingCapacityRules.ExtentApplies(response,subject,response.Id,"approved",location,exposure,now));
        foreach(var changed in new[]{exposure with {TargetId=Guid.NewGuid()},exposure with {RequestedAmount=3000000.01m},exposure with {EndsAt=end.AddTicks(1)},exposure with {Dimension="district-property"}})
            Assert.False(ServicingCapacityRules.ExtentApplies(response,subject,response.Id,"approved",location,changed,now));
        Assert.False(ServicingCapacityRules.ExtentApplies(response,subject with {SubmissionId=Guid.NewGuid()},response.Id,"approved",location,exposure,now));
        Assert.False(ServicingCapacityRules.ExtentApplies(response,subject,response.Id,"approved",location,exposure,end));
        var foreign=definition with {AuthorisedLimits=[JsonSerializer.SerializeToElement(new {dimension="single-location",maximumAmount="3000000.00",riskItemId=Guid.NewGuid()})]};
        Assert.Throws<ArgumentException>(()=>ServicingCapacityResponseRules.Parse(foreign,"AU-05","single-location",now,now,now,[new(start,proposal,null)]));
        var motor=definition with {AuthorisedLimits=[JsonSerializer.SerializeToElement(new {dimension="stock-limit",maximumAmount="3000000.00"})]};
        Assert.Throws<ArgumentException>(()=>ServicingCapacityResponseRules.Parse(motor,"AU-05","single-location",now,now,now,[new(start,proposal,null)]));
    }
}
