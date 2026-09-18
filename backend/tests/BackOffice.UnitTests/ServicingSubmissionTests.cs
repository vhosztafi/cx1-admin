using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingSubmissionTests
{
    [Fact]
    public void SubmissionRequiresOwnedIdentifiersAndAnEditingFence()
    {
        var id=Guid.NewGuid();var version=new byte[8];
        Assert.Equal("Review this servicing case",ServicingSubmissionRules.Validate(id,id,id,version,id,"  Review this servicing case  "));
        Assert.Throws<ArgumentException>(()=>ServicingSubmissionRules.Validate(Guid.Empty,id,id,version,id,"Review this servicing case"));
        Assert.Throws<ArgumentException>(()=>ServicingSubmissionRules.Validate(id,Guid.Empty,id,version,id,"Review this servicing case"));
        Assert.Throws<ArgumentException>(()=>ServicingSubmissionRules.Validate(id,id,Guid.Empty,version,id,"Review this servicing case"));
        Assert.Throws<ArgumentException>(()=>ServicingSubmissionRules.Validate(id,id,id,version,Guid.Empty,"Review this servicing case"));
        Assert.Throws<ArgumentException>(()=>ServicingSubmissionRules.Validate(id,id,id,[],id,"Review this servicing case"));
        Assert.Throws<ArgumentException>(()=>ServicingSubmissionRules.Validate(id,id,id,null,id,"Review this servicing case"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("         ")]
    [InlineData("Too short")]
    [InlineData("Review\nthis case")]
    public void SubmissionRejectsUnusableAuditReasons(string? reason)
    {
        var id=Guid.NewGuid();
        Assert.Throws<ArgumentException>(()=>ServicingSubmissionRules.Validate(id,id,id,new byte[8],id,reason));
    }

    [Fact]
    public void SubmissionReasonHasAnExactStorageBound()
    {
        var id=Guid.NewGuid();
        Assert.Equal(2000,ServicingSubmissionRules.Validate(id,id,id,new byte[8],id,new string('x',2000)).Length);
        Assert.Throws<ArgumentException>(()=>ServicingSubmissionRules.Validate(id,id,id,new byte[8],id,new string('x',2001)));
    }
}
