using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Application.Parties;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class AgencyDraftTests
{
    private static ValidatedAgencyDraft Validate(string json){using var doc=JsonDocument.Parse(json);return AgencyDraftRules.Validate(doc.RootElement);}
    [Fact]public void PartialAnswersNormalizeWithoutInventingMandatoryValues()
    {
        var value=Validate("{\"mainContact\":{\"name\":\" Fictional Sam \"},\"legalName\":\"  Fictional   LLP  \",\"companyNumber\":\" \"}");
        Assert.Equal("Fictional   LLP",value.LegalName);Assert.Equal("FICTIONAL LLP",value.NormalizedName);
        Assert.DoesNotContain("companyNumber",value.Json);Assert.Contains("Fictional Sam",value.Json);
        Assert.Equal("{}",Validate("{}").Json);Assert.Null(Validate("{}").RelationshipManagerId);
    }
    [Theory]
    [InlineData("{\"state\":\"active\"}")]
    [InlineData("{\"legalName\":null}")]
    [InlineData("{\"legalName\":\"one\",\"legalName\":\"two\"}")]
    [InlineData("{\"LegalName\":\"wrong case\"}")]
    [InlineData("{\"arrangesGeneralInsurance\":true}")]
    [InlineData("{\"territory\":\"UK-and-EEA\"}")]
    [InlineData("{\"commercialTerms\":{\"volumeCommitment\":\"-1.00\"}}")]
    [InlineData("{\"commercialTerms\":{\"flatCommissionBasisPoints\":12.5}}")]
    [InlineData("{\"commercialTerms\":{\"flatCommissionBasisPoints\":10001}}")]
    [InlineData("{\"creditLimit\":\"100000000000000000.00\"}")]
    [InlineData("{\"creditLimit\":\"1.001\"}")]
    [InlineData("{\"paymentTermsDays\":31}")]
    [InlineData("{\"compliance\":{\"piExpiresOn\":\"2026-02-30\"}}")]
    [InlineData("{\"compliance\":{\"verifiedBy\":\"someone\"}}")]
    [InlineData("{\"relationshipManagerId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"mainContact\":{\"email\":\"Name <x@example.test>\"}}")]
    public void RejectsMalformedOrAuthorityBearingDrafts(string json)=>Assert.Throws<PartyValidationException>(()=>Validate(json));
    [Fact]public void AllDraftStagesCanRetainUnverifiedDeclarationsAndExactValues()
    {
        var value=Validate("""{"territory":"NI","arrangesGeneralInsurance":"unchecked","correspondencePreference":"portal-only","creditLimit":"99999999999999999.99","paymentTermsDays":45,"commercialTerms":{"commissionBasis":"flat-rate","volumeCommitmentMode":"target-tiered","referralRouting":"standard-internal-underwriting"},"compliance":{"beneficialOwnershipVerified":"refer","tobaVersion":"2025.2","piExpiresOn":"2028-02-29"}}""");
        Assert.Contains("99999999999999999.99",value.Json);Assert.DoesNotContain("flatCommissionBasisPoints",value.Json);
        for(var step=1;step<=6;step++)AgencyDraftRules.ValidateStep(step);
        Assert.Throws<AgencyCommandException>(()=>AgencyDraftRules.ValidateStep(7));
    }
    [Fact]public void ProductsRejectDuplicatesMissingDatesAndInvalidBasisPoints()
    {
        var product=new AgencyProductInput(Guid.NewGuid(),new(2026,9,14),1250);AgencyDraftRules.ValidateProducts([product]);
        Assert.Throws<AgencyCommandException>(()=>AgencyDraftRules.ValidateProducts([product,product]));
        Assert.Throws<AgencyCommandException>(()=>AgencyDraftRules.ValidateProducts([product with{EffectiveFrom=default}]));
        Assert.Throws<AgencyCommandException>(()=>AgencyDraftRules.ValidateProducts([product with{BrokerCommissionBasisPoints=-1}]));
    }
    [Fact]public void AgencyCapabilitiesNeverConferBrokerOrServicingAdministration()
    {
        var actor=new ActorContext(Guid.NewGuid(),null,null,new HashSet<string>{"agency-admin"});Assert.True(actor.HasCapability("agency-admin"));Assert.True(actor.HasCapability("agency-read"));
        Assert.False((actor with{AgencyId=Guid.NewGuid()}).HasCapability("agency-admin"));
        Assert.False((actor with{Roles=new HashSet<string>{"servicing"}}).HasCapability("agency-read"));
        var uw=actor with{Roles=new HashSet<string>{"underwriter"}};Assert.True(uw.HasCapability("agency-read"));Assert.False(uw.HasCapability("agency-admin"));
    }
}
