using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialDocumentSelectionTests
{
    [Fact]
    public void UnselectedEmployersLiabilityNeverRequestsItsCertificate() =>
        Assert.Equal(["policy-schedule", "policy-statement"], CommercialDocumentSelection.Kinds(false));

    [Fact]
    public void SelectedEmployersLiabilityRequestsCertificateWithItsScheduleAndStatement() =>
        Assert.Equal(["policy-schedule", "policy-certificate", "policy-statement"], CommercialDocumentSelection.Kinds(true));

    [Fact]
    public void ReturnedDocumentSelectionCannotChangeTheNextIssue()
    {
        var kinds = CommercialDocumentSelection.Kinds(true); kinds[0] = "not-a-policy-document";
        Assert.Equal("policy-schedule", CommercialDocumentSelection.Kinds(true)[0]);
    }
}
