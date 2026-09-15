using BackOffice.Application.Agencies;
using Xunit;
namespace BackOffice.UnitTests;
public sealed class AgencyPermissionMatrixTests
{
    [Fact]
    public void FutureWorkflowsNeverClaimAvailableOrGrantedAccess()
    {
        var matrix = AgencyPermissionMatrixRules.Build("active", true);
        Assert.Equal(8, matrix.Rows.Count); Assert.Equal(4, matrix.Columns.Count);
        foreach (var row in matrix.Rows.Where(x => x.Capability is "quote-create" or "policy-read" or "adjustment-create" or "referral-decide"))
            Assert.All(row.Cells, cell => { Assert.False(cell.Available); Assert.False(cell.Allowed); });
        Assert.All(matrix.Rows.Single(x => x.Capability == "other-agency-read").Cells.Where(x => x.Role.StartsWith("broker-")), cell => Assert.False(cell.Allowed));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistedBordereauPermissionIsSeparateFromFeatureAvailability(bool granted)
    {
        var row = AgencyPermissionMatrixRules.Build("active", granted).Rows.Single(x => x.Capability == "bordereau-download");
        Assert.All(row.Cells, cell => Assert.False(cell.Available));
        Assert.Equal(granted, row.Cells.Single(x => x.Role == "broker-admin").Allowed);
        Assert.False(row.Cells.Single(x => x.Role == "broker-user").Allowed);
    }
    [Fact]
    public void CurrentUserAuthorityAndAgencyStateControlTheMatrix()
    {
        foreach (var state in new[] { "active", "suspended", "draft", "abandoned" })
        {
            var row = AgencyPermissionMatrixRules.Build(state, true).Rows.Single(x => x.Capability == "agency-user-manage");
            Assert.Equal(state == "active", row.Cells.Single(x => x.Role == "broker-admin").Allowed);
            Assert.Equal(state == "active", row.Cells.Single(x => x.Role == "broker-admin").Available);
            Assert.False(row.Cells.Single(x => x.Role == "servicing").Allowed);
            Assert.True(row.Cells.Single(x => x.Role == "agency-admin").Available);
        }
    }
}
