using BackOffice.Application.Agencies;
using Xunit;
namespace BackOffice.UnitTests;
public sealed class AgencyAccessRulesTests
{
    [Theory]
    [InlineData("broker-admin",true)]
    [InlineData("broker-user",false)]
    [InlineData("broker-readonly",false)]
    public void ExclusiveActiveAgencyRoleHasOnlyItsExplicitCapabilities(string role,bool manage)
    {
        Assert.Equal(role,AgencyAccessRules.ActiveRole(Guid.NewGuid(),"active","active",[new(role,"agency")]));
        Assert.True(AgencyAccessRules.Allows(role,"agency-context-read"));Assert.True(AgencyAccessRules.Allows(role,"agency-sharing-read"));
        Assert.Equal(manage,AgencyAccessRules.Allows(role,"agency-user-manage"));Assert.Equal(manage,AgencyAccessRules.Allows(role,"agency-permission-request"));
        foreach(var capability in new[]{"agency-admin","platform-admin","client-write","policy-issue-within-authority","bordereau-download","unknown"})Assert.False(AgencyAccessRules.Allows(role,capability));
    }
    [Theory]
    [InlineData("invited","active")]
    [InlineData("suspended","active")]
    [InlineData("active","draft")]
    [InlineData("active","suspended")]
    [InlineData("active","abandoned")]
    public void UnavailableIdentityOrAgencyCannotResolve(string user,string agency)=>Assert.Null(AgencyAccessRules.ActiveRole(Guid.NewGuid(),user,agency,[new("broker-admin","agency")]));
    [Fact]
    public void MissingMixedOrForgedRoleScopeFailsClosed()
    {
        var agency=Guid.NewGuid();
        foreach(var roles in new AgencyIdentityRole[][]{[],[new("agency-admin","internal")],[new("broker-admin","internal")],[new("agency-admin","agency")],[new("broker-admin","agency"),new("system-admin","internal")],[new("broker-admin","agency"),new("broker-user","agency")]})Assert.Null(AgencyAccessRules.ActiveRole(agency,"active","active",roles));
        Assert.Null(AgencyAccessRules.ActiveRole(null,"active","active",[new("broker-admin","agency")]));
        Assert.Null(AgencyAccessRules.ActiveRole(Guid.Empty,"active","active",[new("broker-admin","agency")]));
        Assert.False(AgencyAccessRules.Allows(null,"agency-context-read"));
    }
}
