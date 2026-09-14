using System.Text;
using System.Text.Json;
using BackOffice.Application.Agencies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class AgencyEvidenceTests
{
    [Fact]
    public void FingerprintsAreCanonicalRelevantAndIgnoreContactEdits()
    {
        using var first=JsonDocument.Parse("""{"legalName":"Fictional","address":{"postcode":"AB1 2CD","line1":"1 Road"},"mainContact":{"name":"One"}}""");
        using var reordered=JsonDocument.Parse("""{"address":{"line1":"1 Road","postcode":"AB1 2CD"},"mainContact":{"name":"Two"},"legalName":"Fictional"}""");
        using var changed=JsonDocument.Parse("""{"legalName":"Another","address":{"line1":"1 Road","postcode":"AB1 2CD"}}""");
        Assert.Equal(AgencyEvidenceRules.Fingerprint(first.RootElement,"fca"),AgencyEvidenceRules.Fingerprint(reordered.RootElement,"fca"));
        Assert.NotEqual(AgencyEvidenceRules.Fingerprint(first.RootElement,"fca"),AgencyEvidenceRules.Fingerprint(changed.RootElement,"fca"));
    }
    [Theory]
    [InlineData("proof.html","text/plain","<html>bad</html>")]
    [InlineData("../proof.txt","text/plain","text")]
    [InlineData("proof.pdf","application/pdf","not a PDF")]
    [InlineData("proof.txt","text/plain","<script>bad</script>")]
    [InlineData("proof.svg","image/svg+xml","<svg/>")]
    [InlineData("proof.txt\r\nX: bad","text/plain","text")]
    public void UploadRejectsUnsafeNamesTypesAndSignatures(string name,string type,string bytes)=>Assert.Throws<AgencyCommandException>(()=>AgencyEvidenceRules.ValidateFile(name,type,Encoding.UTF8.GetBytes(bytes)));
    [Fact]
    public void FileHashAndBoundsUseActualBytes()
    {
        var file=AgencyEvidenceRules.ValidateFile("proof.txt","text/plain","Fictional evidence\n"u8.ToArray());Assert.Equal(64,file.Sha256.Length);
        Assert.Equal(413,Assert.Throws<AgencyCommandException>(()=>AgencyEvidenceRules.ValidateFile("proof.txt","text/plain",new byte[AgencyEvidenceRules.MaximumFileBytes+1])).Status);
        Assert.Throws<AgencyCommandException>(()=>AgencyEvidenceRules.ValidateFile("proof.txt","text/plain",[255]));
    }
    [Fact]
    public void FormatAloneCannotVerifyAndScenariosRemainExplicit()
    {
        using var missing=JsonDocument.Parse("""{"legalName":"Fictional","regulatoryReference":"123456"}""");
        using var complete=JsonDocument.Parse("""{"legalName":"Fictional","regulatoryReference":"123456","regulatoryStatus":"directly-authorised","arrangesGeneralInsurance":"confirmed"}""");
        Assert.Equal("refer",AgencyEvidenceRules.DemoCheck(missing.RootElement,"fca","pass"));
        Assert.Equal("pass",AgencyEvidenceRules.DemoCheck(complete.RootElement,"fca","pass"));
        Assert.Equal("refer",AgencyEvidenceRules.DemoCheck(complete.RootElement,"fca","refer"));
        Assert.Equal("unavailable",AgencyEvidenceRules.DemoCheck(complete.RootElement,"fca","unavailable"));
    }
    [Fact]
    public void ChecklistRejectsStaleRuleInputExpiredRejectedAndMissingEvidence()
    {
        using var input=JsonDocument.Parse("""{"legalName":"Fictional"}""");var rule=Guid.NewGuid();var today=new DateOnly(2026,9,14);
        var evidence=new AgencyEvidenceFact(Guid.NewGuid(),"fca","verified",AgencyEvidenceRules.Fingerprint(input.RootElement,"fca"),rule,today);
        string State(AgencyEvidenceFact? item)=>AgencyEvidenceRules.EvidenceItem(input.RootElement,"fca",item,rule,today).State;
        Assert.Equal("satisfied",State(evidence));Assert.Equal("missing",State(null));Assert.Equal("expired",State(evidence with{ExpiresOn=today.AddDays(-1)}));
        Assert.Equal("stale",State(evidence with{RuleVersionId=Guid.NewGuid()}));Assert.Equal("stale",State(evidence with{InputFingerprint=new string('0',64)}));
        Assert.Equal("failed",State(evidence with{State="rejected"}));Assert.Equal("unavailable",State(evidence with{State="unavailable"}));
    }
    [Fact]
    public void PiAttestationBindsSufficientLimitAndExactExpiry()
    {
        using var input=JsonDocument.Parse("""{"compliance":{"professionalIndemnityStatus":"meets-minimum","professionalIndemnityLimit":"2000000.00","piExpiresOn":"2027-09-14"}}""");
        var today=new DateOnly(2026,9,14);var expiry=new DateOnly(2027,9,14);
        Assert.True(AgencyEvidenceRules.AttestationValid(input.RootElement,"professional-indemnity",today,1300000m,"2026.1",expiry));
        Assert.False(AgencyEvidenceRules.AttestationValid(input.RootElement,"professional-indemnity",today,3000000m,"2026.1",expiry));
        Assert.False(AgencyEvidenceRules.AttestationValid(input.RootElement,"professional-indemnity",today,1300000m,"2026.1",expiry.AddDays(1)));
    }
}
