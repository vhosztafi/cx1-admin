using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingCapacityProofTests
{
    private static readonly DateTimeOffset Day=new(2026,10,1,0,0,0,TimeSpan.Zero);
    private static readonly ServicingProofContext Context=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new string('a',64),
        new(Guid.NewGuid(),Guid.NewGuid(),"1.0","1","1"));

    [Fact]
    public void ResponseProofPinsExactSubmissionAndAllServicingOwners()
    {
        var submission=Guid.NewGuid();var hash=new string('b',64);
        var original=ServicingCapacityProofRules.Requirement(Context,submission,hash,[Day,Day.AddMonths(1)]);
        Assert.Equal("capacity-response",original.Code);Assert.Null(original.RiskItemId);Assert.Equal(submission,original.CapacitySubmissionId);
        Assert.Matches("^[0-9a-f]{64}$",original.InputFingerprint);
        Assert.NotEqual(original.InputFingerprint,ServicingCapacityProofRules.Requirement(Context,Guid.NewGuid(),hash,[Day,Day.AddMonths(1)]).InputFingerprint);
        Assert.NotEqual(original.InputFingerprint,ServicingCapacityProofRules.Requirement(Context,submission,new string('c',64),[Day,Day.AddMonths(1)]).InputFingerprint);
        foreach(var context in new[]{Context with {DraftId=Guid.NewGuid()},Context with {CycleId=Guid.NewGuid()},Context with {RevisionId=Guid.NewGuid()},
            Context with {RatingId=Guid.NewGuid()},Context with {InputHash=new string('d',64)},Context with {Pins=Context.Pins with {AgencyTermsVersionId=Guid.NewGuid()}}})
            Assert.NotEqual(original.InputFingerprint,ServicingCapacityProofRules.Requirement(context,submission,hash,[Day,Day.AddMonths(1)]).InputFingerprint);
    }

    [Fact]
    public void InvalidIdentityHashOrDatedScheduleCannotCreateResponsePurpose()
    {
        var submission=Guid.NewGuid();var hash=new string('b',64);
        Assert.Throws<ArgumentException>(()=>ServicingCapacityProofRules.Requirement(Context,Guid.Empty,hash,[Day]));
        Assert.Throws<ArgumentException>(()=>ServicingCapacityProofRules.Requirement(Context,submission,"wrong",[Day]));
        Assert.Throws<ArgumentException>(()=>ServicingCapacityProofRules.Requirement(Context with {CycleId=Guid.Empty},submission,hash,[Day]));
        foreach(var dates in new[]{Array.Empty<DateTimeOffset>(),new[]{Day,Day},new[]{Day.AddDays(1),Day},new[]{Day.ToOffset(TimeSpan.FromHours(1))}})
            Assert.Throws<ArgumentException>(()=>ServicingCapacityProofRules.Requirement(Context,submission,hash,dates));
    }
}
