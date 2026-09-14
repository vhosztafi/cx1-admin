using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Agencies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class AgencyActivationTests
{
    private static readonly Guid Rule=Guid.Parse("12492635-a721-477f-8871-4a462404de5f");
    private static readonly DateOnly Today=new(2026,9,14);
    private static JsonObject Complete()=>JsonNode.Parse("""
    {"legalName":"Fictional Complete Agency","entityType":"limited-company","companyNumber":"12345678","address":{"line1":"1 Fictional Road","town":"Leeds","postcode":"LS1 1AA","country":"GB"},"tradingAddressMode":"different","tradingAddress":{"line1":"2 Fictional Road","town":"Leeds","postcode":"LS1 1AB","country":"GB"},"regulatoryReference":"123456","regulatoryStatus":"appointed-representative","principalFirm":"Fictional Principal","clientMoneyBasis":"cass5-client-money","arrangesGeneralInsurance":"confirmed","territory":"UK","mainContact":{"name":"Fictional Main","email":"main@example.test","telephone":"01130000001"},"complianceContact":{"name":"Fictional Compliance","email":"compliance@example.test","telephone":"01130000002"},"relationshipManagerId":"cc1f5e5b-d08b-4a9c-bab4-d40310e491b8","correspondencePreference":"portal-only","commercialTerms":{"effectiveFrom":"2026-09-14","commissionBasis":"flat-rate","flatCommissionBasisPoints":1250,"feeSharing":"agreed-split","feeShareBasisPoints":1000,"volumeCommitmentMode":"target-tiered","volumeCommitment":"100000.00","minimumPremiumOverrideMode":"capacity-provider-agreed","minimumPremiumOverride":"150.00","referralRouting":"standard-internal-underwriting"},"compliance":{"tobaStatus":"signed","tobaVersion":"2026.1","tobaSignedOn":"2026-09-01","professionalIndemnityStatus":"meets-minimum","professionalIndemnityLimit":"2000000.00","piExpiresOn":"2027-09-14","dataProcessingAgreement":"signed"},"paymentTermsDays":30,"creditLimit":"0.00","settlement":{"statementCycle":"monthly","method":"bank-transfer","premiumCollection":"agency","commissionSettlement":"net-remittance"}}
    """)!.AsObject();
    private static IReadOnlyList<AgencyChecklistItem> Evaluate(JsonObject input,bool evidence=true,AgencyActivationDependencies? dependencies=null,string? omitEvidence=null)
    {
        using var document=JsonDocument.Parse(input.ToJsonString());AgencyDraftRules.Validate(document.RootElement);
        var facts=new Dictionary<string,AgencyEvidenceFact>();
        if(evidence)foreach(var kind in AgencyEvidenceRules.CheckKinds.Concat(AgencyEvidenceRules.AttestationKinds))if(kind!=omitEvidence)facts[kind]=new(Guid.NewGuid(),kind,"verified",AgencyEvidenceRules.Fingerprint(document.RootElement,kind),Rule,kind=="professional-indemnity"?new DateOnly(2027,9,14):null);
        return AgencyActivationRules.Evaluate(document.RootElement,facts,Rule,Today,1300000m,"2026.1",dependencies??new(true,true));
    }
    [Fact]
    public void CompleteCurrentDemoPrerequisitesPassWithoutGrantingActivation()
    {
        var items=Evaluate(Complete());Assert.All(items,x=>Assert.Equal("satisfied",x.State));Assert.InRange(items.Count,1,80);Assert.Equal(items.Count,items.Select(x=>x.Code).Distinct().Count());
    }
    [Theory]
    [InlineData("legalName")][InlineData("entityType")][InlineData("companyNumber")]
    [InlineData("address.line1")][InlineData("address.town")][InlineData("address.postcode")][InlineData("address.country")]
    [InlineData("tradingAddress.line1")][InlineData("tradingAddress.town")][InlineData("tradingAddress.postcode")][InlineData("tradingAddress.country")]
    [InlineData("regulatoryReference")][InlineData("principalFirm")][InlineData("territory")][InlineData("relationshipManagerId")]
    [InlineData("mainContact.name")][InlineData("mainContact.email")][InlineData("mainContact.telephone")]
    [InlineData("complianceContact.name")][InlineData("complianceContact.email")][InlineData("complianceContact.telephone")]
    [InlineData("commercialTerms.flatCommissionBasisPoints")][InlineData("commercialTerms.feeShareBasisPoints")]
    [InlineData("commercialTerms.volumeCommitment")][InlineData("commercialTerms.minimumPremiumOverride")]
    [InlineData("creditLimit")][InlineData("paymentTermsDays")][InlineData("settlement.statementCycle")][InlineData("settlement.method")][InlineData("settlement.premiumCollection")][InlineData("settlement.commissionSettlement")]
    public void MissingFieldHasItsOwnActionablePath(string path)
    {
        var input=Complete();var parts=path.Split('.');var owner=input;foreach(var part in parts[..^1])owner=owner[part]!.AsObject();owner.Remove(parts[^1]);
        Assert.Contains(Evaluate(input),x=>x.Path=="/details/"+path.Replace('.','/')&&x.State=="missing");
    }
    [Theory]
    [InlineData("fca")][InlineData("financial-check")][InlineData("sanctions")][InlineData("ownership")]
    [InlineData("toba")][InlineData("professional-indemnity")][InlineData("dpa")][InlineData("client-money")]
    public void EveryRequiredEvidenceKindRemainsMissingDespiteCompleteDeclarations(string kind)=>Assert.Contains(Evaluate(Complete(),omitEvidence:kind),x=>x.Code=="evidence-"+kind&&x.State=="missing");
    [Fact]
    public void UnavailableDependenciesAndFormatOnlyDeclarationsNeverProduceReadiness()
    {
        var items=Evaluate(Complete(),false,new(null,null));Assert.Contains(items,x=>x.Code=="eligible-products"&&x.State=="unavailable");Assert.Contains(items,x=>x.Code=="broker-administrator"&&x.State=="unavailable");Assert.Equal(8,items.Count(x=>x.Code.StartsWith("evidence-")&&x.State=="missing"));
    }
    [Fact]
    public void IntroducerAndFutureTermsFailEvenWithRecordedEvidence()
    {
        var input=Complete();input["regulatoryStatus"]="introducer-appointed-representative";input["commercialTerms"]!["effectiveFrom"]="2026-09-15";
        var items=Evaluate(input);Assert.Contains(items,x=>x.Code=="arranging-permission"&&x.State=="failed");Assert.Contains(items,x=>x.Code=="terms-effective"&&x.State=="failed");
    }
    [Fact]
    public void InapplicableConditionalFieldsAreNotInventedRequirements()
    {
        var input=Complete();input["tradingAddressMode"]="same-as-registered";input.Remove("tradingAddress");input["regulatoryStatus"]="directly-authorised";input.Remove("principalFirm");input["entityType"]="sole-trader";input.Remove("companyNumber");input["clientMoneyBasis"]="risk-transfer";
        var terms=input["commercialTerms"]!.AsObject();terms["commissionBasis"]="per-product";terms.Remove("flatCommissionBasisPoints");terms["feeSharing"]="none";terms.Remove("feeShareBasisPoints");terms["volumeCommitmentMode"]="none";terms.Remove("volumeCommitment");terms["minimumPremiumOverrideMode"]="none";terms.Remove("minimumPremiumOverride");
        Assert.All(Evaluate(input),x=>Assert.Equal("satisfied",x.State));
    }
    [Theory]
    [InlineData("tobaVersion","2025.2","toba")][InlineData("tobaSignedOn","2026-10-01","toba")]
    [InlineData("professionalIndemnityLimit","100.00","professional-indemnity")][InlineData("piExpiresOn","2025-09-14","professional-indemnity")]
    [InlineData("dataProcessingAgreement","sent","dpa")]
    public void ReadinessRechecksDeclarationThresholdsAndDates(string field,string value,string kind)
    {
        var input=Complete();input["compliance"]![field]=value;Assert.Contains(Evaluate(input),x=>x.Code=="evidence-"+kind&&x.State=="failed");
    }
}
