using BackOffice.Application.Operations;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class OperationalOccurrenceTests
{
    [Fact]
    public void MotorSelectionsCannotBorrowForeignDriverOrCommercialIdentity()
    {
        using var stream=typeof(OperationalOccurrenceTests).Assembly.GetManifestResourceStream("PolicyExamples.issued-motor-trade-road-risks.json")!;
        using var snapshot=System.Text.Json.JsonDocument.Parse(stream);var driver=snapshot.RootElement.GetProperty("risk").GetProperty("drivers")[0].GetProperty("id").GetGuid();
        var subject=System.Text.Json.JsonSerializer.SerializeToElement(new{kind="registered-vehicle",driverId=driver,driverDeclaration="named"});
        Assert.False(IncidentSubjectRules.Ready(snapshot.RootElement,subject));
        Assert.Throws<IncidentOccurrenceException>(()=>IncidentSubjectRules.Ready(snapshot.RootElement,System.Text.Json.JsonSerializer.SerializeToElement(new{kind="registered-vehicle",driverId=Guid.NewGuid(),driverDeclaration="named"})));
        Assert.Throws<IncidentOccurrenceException>(()=>IncidentSubjectRules.Ready(snapshot.RootElement,System.Text.Json.JsonSerializer.SerializeToElement(new{kind="property",locationId=Guid.NewGuid(),coverCode="buildings"})));
    }
    [Theory]
    [InlineData("2026-03-29",23)]
    [InlineData("2026-10-25",25)]
    [InlineData("2026-09-16",24)]
    public void LocalDayRetainsActualDstLengthAndApproximationDoesNotInventAnInstant(string date,int hours)
    {
        var day=DateOnly.Parse(date);var plain=IncidentOccurrenceRules.Window(new(day,"Europe/London","date"));
        var approximate=IncidentOccurrenceRules.Window(new(day,"Europe/London","approximate",new TimeOnly(12,30)));
        Assert.False(plain.IsExact);Assert.Equal(hours,(plain.To-plain.From).TotalHours);Assert.Equal(plain,approximate);
    }
    [Fact]
    public void ExactLocalRejectsGapAndRequiresOverlapOffset()
    {
        Assert.Throws<IncidentOccurrenceException>(()=>IncidentOccurrenceRules.ExactLocal(new(2026,3,29),new(1,30)));
        Assert.Throws<IncidentOccurrenceException>(()=>IncidentOccurrenceRules.ExactLocal(new(2026,10,25),new(1,30)));
        var first=IncidentOccurrenceRules.ExactLocal(new(2026,10,25),new(1,30),TimeSpan.FromHours(1));
        var second=IncidentOccurrenceRules.ExactLocal(new(2026,10,25),new(1,30),TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(1),second-first);
        Assert.Throws<IncidentOccurrenceException>(()=>IncidentOccurrenceRules.ExactLocal(new(2026,10,25),new(1,30),TimeSpan.FromHours(2)));
    }
    [Fact]
    public void ExactInstantMustAgreeWithLondonDateAndClosedPrecisionFields()
    {
        var instant=DateTimeOffset.Parse("2026-09-15T23:30:00Z");
        var window=IncidentOccurrenceRules.Window(new(new(2026,9,16),"Europe/London","exact",OccurredAt:instant));
        Assert.True(window.IsExact);Assert.Equal(instant,window.From);Assert.Equal(instant,window.To);
        Assert.Throws<IncidentOccurrenceException>(()=>IncidentOccurrenceRules.Window(new(new(2026,9,15),"Europe/London","exact",OccurredAt:instant)));
        Assert.Throws<IncidentOccurrenceException>(()=>IncidentOccurrenceRules.Window(new(new(2026,9,16),"UTC","date")));
        Assert.Throws<IncidentOccurrenceException>(()=>IncidentOccurrenceRules.Window(new(new(2026,9,16),"Europe/London","date",OccurredAt:instant)));
    }
    [Fact]
    public void WholeDayResolutionDistinguishesVersionBoundaryCancellationAndUnknownCover()
    {
        var window=IncidentOccurrenceRules.Window(new(new(2026,9,16),"Europe/London","date"));var known=window.To.AddDays(1);
        var first=new IncidentOccurrenceSlice(Guid.NewGuid(),new string('a',64),window.From,window.To);
        Assert.Equal("resolved",IncidentOccurrenceRules.State(window,known,[first]));
        var noon=window.From.AddHours(12);var second=new IncidentOccurrenceSlice(Guid.NewGuid(),new string('b',64),noon,window.To);
        Assert.Equal("ambiguous",IncidentOccurrenceRules.State(window,known,[first with{To=noon},second]));
        Assert.Equal("partly-uncovered",IncidentOccurrenceRules.State(window,known,[first with{To=noon}]));
        Assert.Equal("uncovered",IncidentOccurrenceRules.State(window,known,[]));
        Assert.Equal("incomplete",IncidentOccurrenceRules.State(window,noon,[first]));
    }
}
