using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Agencies;
using BackOffice.Application.Parties;
using Xunit;
namespace BackOffice.UnitTests;
public sealed class AgencyTermsTests
{
    private static readonly DateOnly Today=new(2026,9,14);
    private static JsonObject Complete()=>JsonNode.Parse("""
    {"effectiveFrom":"2026-09-14","reason":" Fictional terms review ","commercialTerms":{"effectiveFrom":"2026-09-14","commissionBasis":"per-product","feeSharing":"none","volumeCommitmentMode":"none","minimumPremiumOverrideMode":"none","referralRouting":"standard-internal-underwriting"},"settlement":{"statementCycle":"monthly","method":"bank-transfer","premiumCollection":"agency","commissionSettlement":"net-remittance"},"paymentTermsDays":30,"creditLimit":"99999999999999999.99","products":[{"productVersionId":"11111111-1111-4111-8111-111111111111","effectiveFrom":"2026-09-14","brokerCommissionBasisPoints":1250}]}
    """)!.AsObject();
    private static ValidatedAgencyTerms Validate(JsonObject input,DateOnly? latest=null){using var doc=JsonDocument.Parse(input.ToJsonString());return AgencyTermsRules.ValidateProposal(doc.RootElement,Today,latest);}
    [Fact]public void CompleteSnapshotPreservesExactMoneyAndExcludesRequestReason()
    {
        var result=Validate(Complete());Assert.Equal("Fictional terms review",result.Reason);Assert.Contains("99999999999999999.99",result.SnapshotJson);Assert.DoesNotContain("reason",result.SnapshotJson);Assert.Equal(64,result.Fingerprint.Length);Assert.Single(result.Products);
    }
    [Theory][InlineData("commissionBasis","flat-rate","flatCommissionBasisPoints")][InlineData("feeSharing","agreed-split","feeShareBasisPoints")][InlineData("volumeCommitmentMode","target-tiered","volumeCommitment")][InlineData("minimumPremiumOverrideMode","capacity-provider-agreed","minimumPremiumOverride")]
    public void SelectedModeRequiresItsActualValue(string mode,string enabled,string field)
    {
        var input=Complete();input["commercialTerms"]![mode]=enabled;var error=Assert.Throws<PartyValidationException>(()=>Validate(input));Assert.Contains(error.Issues,x=>x.Path=="/commercialTerms/"+field);
    }
    [Theory][InlineData("commercialTerms")][InlineData("settlement")][InlineData("creditLimit")][InlineData("products")][InlineData("paymentTermsDays")]
    public void PartialDraftCannotBecomeAgreedTerms(string field){var input=Complete();input.Remove(field);Assert.Throws<AgencyCommandException>(()=>Validate(input));}
    [Fact]public void ScheduleRejectsBackdatingCollisionAndInsertionBeforeFutureVersion()
    {
        Assert.Throws<AgencyCommandException>(()=>AgencyTermsRules.ValidateSchedule(Today.AddDays(-1),Today,null));Assert.Throws<AgencyCommandException>(()=>Validate(Complete(),Today));Assert.Throws<AgencyCommandException>(()=>Validate(Complete(),Today.AddDays(1)));AgencyTermsRules.ValidateSchedule(Today.AddDays(2),Today,Today.AddDays(1));
    }
    [Fact]public void ProductDatesCannotBackdateOrLeaveInitialCoverageGap()
    {
        var input=Complete();input["products"]![0]!["effectiveFrom"]="2026-09-13";Assert.Throws<AgencyCommandException>(()=>Validate(input));input["products"]![0]!["effectiveFrom"]="2026-09-15";Assert.Throws<AgencyCommandException>(()=>Validate(input));
    }
    [Fact]public void DuplicateProductsAndOutOfRangeCommissionAreRejected()
    {
        var input=Complete();input["products"]!.AsArray().Add(input["products"]![0]!.DeepClone());Assert.Throws<AgencyCommandException>(()=>Validate(input));input=Complete();input["products"]![0]!["brokerCommissionBasisPoints"]=10001;Assert.Throws<AgencyCommandException>(()=>Validate(input));
    }
    [Fact]public void UnknownAuthorityAndDuplicateFieldsCannotEnterSnapshot()
    {
        var input=Complete();input["approvedBy"]="forged";Assert.Throws<AgencyCommandException>(()=>Validate(input));using var duplicate=JsonDocument.Parse(Complete().ToJsonString().Replace("\"reason\":", "\"reason\":\"first\",\"reason\":"));Assert.Throws<AgencyCommandException>(()=>AgencyTermsRules.ValidateProposal(duplicate.RootElement,Today,null));
    }
    [Fact]public void CanonicalFingerprintIgnoresPropertyOrderAndReasonButBindsFinancialChange()
    {
        var input=Complete();var first=Validate(input);var reversed=new JsonObject();foreach(var pair in input.Reverse())reversed[pair.Key]=pair.Value!.DeepClone();reversed["reason"]="Different review wording";Assert.Equal(first.Fingerprint,Validate(reversed).Fingerprint);reversed["creditLimit"]="0.00";Assert.NotEqual(first.Fingerprint,Validate(reversed).Fingerprint);
    }
    [Fact]public void ExplicitZeroValuesRemainDistinctFromMissingConditionalAnswers()
    {
        var input=Complete();var terms=input["commercialTerms"]!;terms["commissionBasis"]="flat-rate";terms["flatCommissionBasisPoints"]=0;terms["feeSharing"]="agreed-split";terms["feeShareBasisPoints"]=0;terms["volumeCommitmentMode"]="target-no-penalty";terms["volumeCommitment"]="0.00";terms["minimumPremiumOverrideMode"]="capacity-provider-agreed";terms["minimumPremiumOverride"]="0.00";Assert.Contains("\"flatCommissionBasisPoints\":0",Validate(input).SnapshotJson);
    }
    [Theory][InlineData("-1.00")][InlineData("1.001")][InlineData("1")]
    public void MoneyCannotLosePrecisionOrChangeSign(string amount){var input=Complete();input["creditLimit"]=amount;Assert.Throws<PartyValidationException>(()=>Validate(input));}
    [Fact]public void CommercialDateMustMatchVersionAndReasonMustBeBounded()
    {
        var input=Complete();input["commercialTerms"]!["effectiveFrom"]="2026-09-15";Assert.Throws<AgencyCommandException>(()=>Validate(input));input=Complete();input["reason"]=new string('x',1001);Assert.Throws<AgencyCommandException>(()=>Validate(input));input["reason"]="";Assert.Throws<AgencyCommandException>(()=>Validate(input));
    }
    [Fact]public void NestedDuplicateInputIsRejectedBeforeFingerprinting()
    {
        using var doc=JsonDocument.Parse(Complete().ToJsonString().Replace("\"feeSharing\":", "\"feeSharing\":\"none\",\"feeSharing\":"));Assert.Throws<PartyValidationException>(()=>AgencyTermsRules.ValidateProposal(doc.RootElement,Today,null));
    }

    private static ValidatedAgencyTerms Initial(JsonObject input,DateOnly? productDate=null)
    {
        var details=new JsonObject();foreach(var key in new[]{"commercialTerms","settlement","paymentTermsDays","creditLimit"})details[key]=input[key]?.DeepClone();
        details["legalName"]="Fictional initial agency";
        using var doc=JsonDocument.Parse(details.ToJsonString());
        return AgencyTermsRules.ExtractInitial(doc.RootElement,[new(Guid.Parse("11111111-1111-4111-8111-111111111111"),productDate??DateOnly.Parse(input["effectiveFrom"]!.GetValue<string>()),1250)],Today,"Initial review");
    }
    [Fact]public void InitialTermsPreserveHistoricalDeclarationsWithoutPermittingBackdatedChanges()
    {
        var input=Complete();input["effectiveFrom"]="2026-09-01";input["commercialTerms"]!["effectiveFrom"]="2026-09-01";
        var result=Initial(input);Assert.Equal(new DateOnly(2026,9,1),result.EffectiveFrom);Assert.Contains("2026-09-01",result.SnapshotJson);Assert.DoesNotContain("legalName",result.SnapshotJson);
        Assert.Throws<AgencyCommandException>(()=>Validate(input));
    }
    [Fact]public void InitialTermsRejectFutureDatesAndDoNotSilentlyShiftProductDates()
    {
        var input=Complete();input["effectiveFrom"]="2026-09-15";input["commercialTerms"]!["effectiveFrom"]="2026-09-15";
        Assert.Throws<AgencyCommandException>(()=>Initial(input));
        Assert.Throws<AgencyCommandException>(()=>Initial(Complete(),Today.AddDays(-1)));
        Assert.Throws<AgencyCommandException>(()=>Initial(Complete(),Today.AddDays(1)));
    }
    [Fact]public void InitialTermsRequireCompleteConditionalMoneyAndSettlement()
    {
        var input=Complete();input["settlement"]!.AsObject().Remove("method");Assert.Throws<PartyValidationException>(()=>Initial(input));
        input=Complete();input["commercialTerms"]!["commissionBasis"]="flat-rate";Assert.Throws<PartyValidationException>(()=>Initial(input));
        input=Complete();input.Remove("creditLimit");Assert.Throws<PartyValidationException>(()=>Initial(input));
    }

}
