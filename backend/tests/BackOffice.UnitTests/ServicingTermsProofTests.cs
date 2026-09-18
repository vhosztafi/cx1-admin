using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingTermsProofTests
{
    private static readonly DateTimeOffset Day=new(2026,10,1,0,0,0,TimeSpan.Zero);
    private static readonly ServicingProofContext Context=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new string('a',64),
        new(Guid.NewGuid(),Guid.NewGuid(),"1.0","1","1"));
    private static readonly ServicingTermsSubject Terms=new(Context.DraftId,Context.CycleId,Context.RevisionId,Guid.NewGuid(),Context.RatingId,Guid.NewGuid(),new string('b',64));

    [Theory]
    [InlineData("signed-statement")]
    [InlineData("acceptance-proof")]
    public void ProofBelongsToExactContractAndPurpose(string code)
    {
        var requirement=ServicingTermsProofRules.Requirement(Context,Terms,code,[Day,Day.AddMonths(1)]);
        Assert.Equal(Terms.TermsId,requirement.TermsVersionId);Assert.Null(requirement.CapacitySubmissionId);Assert.Null(requirement.RiskItemId);
        var proof=new ServicingReviewedProof(Context.DraftId,Context.CycleId,Context.RevisionId,Context.RatingId,code,null,
            requirement.InputFingerprint,"accepted","accepted",false,TermsVersionId:Terms.TermsId);
        Assert.True(ServicingEvidenceRules.Satisfied(Context,requirement,proof));
        Assert.False(ServicingEvidenceRules.Satisfied(Context,requirement,proof with{TermsVersionId=null}));
        Assert.False(ServicingEvidenceRules.Satisfied(Context,requirement,proof with{TermsVersionId=Guid.NewGuid()}));
        Assert.False(ServicingEvidenceRules.Satisfied(Context,requirement,proof with{Withdrawn=true}));
        Assert.False(ServicingEvidenceRules.Satisfied(Context,requirement,proof with{Code=code=="signed-statement"?"acceptance-proof":"signed-statement"}));
        foreach(var changed in new[]{Terms with{TermsId=Guid.NewGuid()},Terms with{TermsHash=new string('c',64)},Terms with{BaseVersionId=Guid.NewGuid()}})
            Assert.NotEqual(requirement.InputFingerprint,ServicingTermsProofRules.Requirement(Context,changed,code,[Day,Day.AddMonths(1)]).InputFingerprint);
        Assert.NotEqual(requirement.InputFingerprint,ServicingTermsProofRules.Requirement(Context,Terms,code,[Day]).InputFingerprint);
    }

    [Fact]
    public void MismatchedOwnersInvalidPurposeAndUnorderedDatesCannotCreateProof()
    {
        foreach(var changed in new[]{Terms with{DraftId=Guid.NewGuid()},Terms with{CycleId=Guid.NewGuid()},Terms with{RevisionId=Guid.NewGuid()},
            Terms with{RatingId=Guid.NewGuid()},Terms with{BaseVersionId=Guid.Empty},Terms with{TermsId=Guid.Empty},Terms with{TermsHash="invalid"}})
            Assert.Throws<ArgumentException>(()=>ServicingTermsProofRules.Requirement(Context,changed,"signed-statement",[Day]));
        Assert.Throws<ArgumentException>(()=>ServicingTermsProofRules.Requirement(Context,Terms,"capacity-response",[Day]));
        foreach(var dates in new[]{Array.Empty<DateTimeOffset>(),new[]{Day,Day},new[]{Day.AddDays(1),Day},new[]{Day.ToOffset(TimeSpan.FromHours(1))}})
            Assert.Throws<ArgumentException>(()=>ServicingTermsProofRules.Requirement(Context,Terms,"signed-statement",dates));
    }
}
