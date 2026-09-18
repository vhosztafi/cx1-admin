using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class RenewalLifecycleTests
{
    [Fact]
    public void TimelineUsesLondonCalendarDaysAcrossBothClockChanges()
    {
        var spring=RenewalLifecycleRules.Timeline(DateTimeOffset.Parse("2027-03-20T09:00:00Z"),45,14);
        Assert.Equal(DateTimeOffset.Parse("2027-02-03T09:00:00Z"),spring.InvitationDueAt);
        Assert.Equal(DateTimeOffset.Parse("2027-04-03T08:00:00Z"),spring.AutoLapseAt);
        var autumn=RenewalLifecycleRules.Timeline(DateTimeOffset.Parse("2027-10-20T08:00:00Z"),45,14);
        Assert.Equal(DateTimeOffset.Parse("2027-09-05T08:00:00Z"),autumn.InvitationDueAt);
        Assert.Equal(DateTimeOffset.Parse("2027-11-03T09:00:00Z"),autumn.AutoLapseAt);
        Assert.Equal(autumn.ExpiringEnd,autumn.RenewalInception);
    }

    [Fact]
    public void TimelineRejectsUnresolvedAmbiguousOrNonexistentDerivedTimes()
    {
        var ambiguity=DateTimeOffset.Parse("2027-10-17T00:30:00Z");
        Assert.Throws<ArgumentException>(()=>RenewalLifecycleRules.Timeline(ambiguity,45,14));
        var first=RenewalLifecycleRules.Timeline(ambiguity,45,14,lapseOffsetMinutes:60);
        var second=RenewalLifecycleRules.Timeline(ambiguity,45,14,lapseOffsetMinutes:0);
        Assert.Equal(TimeSpan.FromHours(1),second.AutoLapseAt-first.AutoLapseAt);
        Assert.Throws<ArgumentException>(()=>RenewalLifecycleRules.Timeline(DateTimeOffset.Parse("2027-03-14T01:30:00Z"),45,14));
    }

    [Theory]
    [InlineData(-1,14)]
    [InlineData(366,14)]
    [InlineData(45,-1)]
    [InlineData(45,366)]
    public void TimelineRejectsUnboundedConfiguration(int before,int after)=>
        Assert.Throws<ArgumentException>(()=>RenewalLifecycleRules.Timeline(DateTimeOffset.Parse("2027-09-18T08:00:00Z"),before,after));

    [Fact]
    public void TimelineRejectsNonUtcAndSubMinuteExpiry()
    {
        Assert.Throws<ArgumentException>(()=>RenewalLifecycleRules.Timeline(DateTimeOffset.Parse("2027-09-18T09:00:00+01:00"),45,14));
        Assert.Throws<ArgumentException>(()=>RenewalLifecycleRules.Timeline(DateTimeOffset.Parse("2027-09-18T08:00:01Z"),45,14));
    }

    [Fact]
    public void TimelineAcceptsTheSameZeroTo365DayBoundsAsVersionedConfiguration()
    {
        var expiry=DateTimeOffset.Parse("2027-09-18T08:00:00Z");
        var immediate=RenewalLifecycleRules.Timeline(expiry,0,0);
        Assert.Equal(expiry,immediate.InvitationDueAt);Assert.Equal(expiry,immediate.AutoLapseAt);
        var maximum=RenewalLifecycleRules.Timeline(expiry,365,365);
        Assert.True(maximum.InvitationDueAt<expiry);Assert.True(maximum.AutoLapseAt>expiry);
    }

    [Theory]
    [InlineData(-1,true)]
    [InlineData(0,true)]
    [InlineData(1,false)]
    public void AcceptanceAndIssueCannotBackfillAStartedTerm(int ticks,bool allowed)
    {
        var inception=DateTimeOffset.Parse("2027-09-18T08:00:00Z");
        Assert.Equal(allowed,RenewalLifecycleRules.WithinIssueWindow(inception.AddTicks(ticks),inception));
        Assert.False(RenewalLifecycleRules.WithinIssueWindow(inception.ToOffset(TimeSpan.FromHours(1)),inception));
    }

    [Theory]
    [InlineData(false,false,false,false,-1,false)]
    [InlineData(false,false,false,false,0,true)]
    [InlineData(false,false,false,false,1,true)]
    [InlineData(true,false,false,false,-1,true)]
    [InlineData(true,true,false,false,0,false)]
    [InlineData(false,true,false,false,0,false)]
    [InlineData(true,false,true,false,0,false)]
    [InlineData(false,false,true,false,0,false)]
    [InlineData(true,false,false,true,0,false)]
    [InlineData(false,false,false,true,0,false)]
    public void ManualAndAutomaticLapsePreserveAcceptedIssuedAndAlreadyLapsedTerms(bool manual,bool accepted,bool issued,bool lapsed,int ticks,bool allowed)
    {
        var due=DateTimeOffset.Parse("2027-10-02T08:00:00Z");
        Assert.Equal(allowed,RenewalLifecycleRules.CanLapse(due.AddTicks(ticks),due,manual,accepted,issued,lapsed));
    }
}
