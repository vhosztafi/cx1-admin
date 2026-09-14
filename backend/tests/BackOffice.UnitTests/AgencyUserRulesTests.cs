using BackOffice.Application.Agencies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class AgencyUserRulesTests
{
    [Theory]
    [InlineData("broker-admin")]
    [InlineData("broker-user")]
    [InlineData("broker-readonly")]
    public void AgencyInvitationNormalizesEmailWithoutGuessingNameOrRole(string role)
    {
        var value=AgencyUserRules.Validate("  Fictional.Person@cover.example  ","  Fictional Broker Person  ",role);
        Assert.Equal("Fictional.Person@cover.example",value.Email);Assert.Equal("FICTIONAL.PERSON@COVER.EXAMPLE",value.NormalizedEmail);
        Assert.Equal("Fictional Broker Person",value.DisplayName);Assert.Equal(role,value.Role);
    }
    [Theory]
    [InlineData("bad","Person","broker-user")]
    [InlineData("Person <person@cover.example>","Person","broker-user")]
    [InlineData("person@cover.example","","broker-user")]
    [InlineData("person@cover.example","Person\nOther","broker-user")]
    [InlineData("person@cover.example","Person","agency-admin")]
    [InlineData("person@cover.example","Person","system-admin")]
    [InlineData("person@cover.example","Person","broker-user,broker-admin")]
    public void AgencyInvitationRejectsInvalidOrPrivilegedIdentity(string email,string name,string role)
        =>Assert.Throws<AgencyCommandException>(()=>AgencyUserRules.Validate(email,name,role));
}
