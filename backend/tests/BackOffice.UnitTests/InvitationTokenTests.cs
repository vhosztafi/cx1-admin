using BackOffice.Application.Agencies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class InvitationTokenTests
{
    [Fact]
    public void AgencyInvitationTokensAreIndependentCanonicalAndHashOnlyComparable()
    {
        var first=InvitationToken.Create();var second=InvitationToken.Create();
        Assert.Equal(43,first.Value.Length);Assert.Equal(32,first.Hash.Length);Assert.NotEqual(first.Value,second.Value);
        Assert.True(InvitationToken.TryHash(first.Value,out var hash));Assert.Equal(first.Hash,hash);Assert.DoesNotContain(first.Value,first.ToString());
        Assert.False(InvitationToken.TryHash(first.Value+"=",out _));
        // Different unused base64 pad bits must not create aliases for the same token bytes.
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var alias=first.Value[..^1]+alphabet[alphabet.IndexOf(first.Value[^1])+1];
        Assert.False(InvitationToken.TryHash(alias,out _));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    public void AgencyInvitationInvalidTokenCannotProduceLookupHash(string? value)
    {Assert.False(InvitationToken.TryHash(value,out var hash));Assert.Empty(hash);}
}
