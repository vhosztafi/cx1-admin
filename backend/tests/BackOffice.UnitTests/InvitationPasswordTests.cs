using BackOffice.Application.Agencies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class InvitationPasswordTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(128)]
    public void AcceptsSupportedBoundaries(int length)=>InvitationPassword.Validate(new string('x',length));
    [Theory]
    [InlineData(11)]
    [InlineData(129)]
    public void RejectsUnsupportedBoundaries(int length)=>Assert.Equal(422,Assert.Throws<AgencyCommandException>(()=>InvitationPassword.Validate(new string('x',length))).Status);
    [Fact]
    public void RejectsMissingPassword()=>Assert.Throws<AgencyCommandException>(()=>InvitationPassword.Validate(null));
    [Fact]
    public void SupportsPassphrasesWithUnicodeAndSpaces()=>InvitationPassword.Validate("  Fictional café phrase  ");
}
