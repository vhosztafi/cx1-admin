using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialServicingEvidenceTests
{
    private static JsonElement Proposal()
    {
        using var stream=typeof(CommercialServicingEvidenceTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!;
        using var document=JsonDocument.Parse(stream);return document.RootElement.Clone();
    }
    private static ServicingProofContext Context()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new string('a',64),
        new(Guid.NewGuid(),Guid.NewGuid(),"1.0",CommercialCaptureRules.QuestionVersion,CommercialCaptureRules.ReferenceVersion));
    private static DateTimeOffset At(string date)=>DateTimeOffset.Parse(date,System.Globalization.CultureInfo.InvariantCulture);
    [Fact]
    public void CommercialProofsBindOwnedTargetsWholeScheduleAndExactServicingContext()
    {
        var context=Context();var proposal=Proposal();var date=At("2026-10-01T00:00:00Z");
        var slices=new[]{new ServicingEvidenceSlice(date,proposal,14)};
        var required=ServicingEvidenceRules.Requirements(context,slices);
        Assert.Contains(required,x=>x.Code=="cc-property-proof");Assert.Contains(required,x=>x.Code=="cc-bi-proof");
        Assert.DoesNotContain(required,x=>x.Code=="trading-history"||x.Code=="premises-security"||x.Code.Contains("driver",StringComparison.Ordinal));
        Assert.Equal(2,required.Count(x=>x.Code=="cc-location-proof"));
        var local=required.First(x=>x.Code=="cc-location-proof");
        var proof=new ServicingReviewedProof(context.DraftId,context.CycleId,context.RevisionId,context.RatingId,local.Code,local.RiskItemId,local.InputFingerprint,"accepted","accepted",false);
        Assert.True(ServicingEvidenceRules.Satisfied(context,local,proof));
        foreach(var bad in new[]{proof with {DraftId=Guid.NewGuid()},proof with {RiskItemId=Guid.NewGuid()},proof with {RevisionId=Guid.NewGuid()},proof with {Withdrawn=true},proof with {ScreeningState="pending"}})
            Assert.False(ServicingEvidenceRules.Satisfied(context,local,bad));
        var changed=JsonNode.Parse(proposal.GetRawText())!;changed["risk"]!["locations"]![1]!["stock"]="200.00";
        var newer=ServicingEvidenceRules.Requirements(context,[new(date,JsonSerializer.SerializeToElement(changed),14)]);
        Assert.All(required,old=>Assert.NotEqual(old.InputFingerprint,Assert.Single(newer,x=>x.Code==old.Code&&x.RiskItemId==old.RiskItemId).InputFingerprint));
    }
    [Fact]
    public void DatedCommercialConditionsCannotBorrowADeletedOrForeignSubject()
    {
        var proposal=Proposal();var first=At("2026-10-01T00:00:00Z");var second=At("2026-11-01T00:00:00Z");
        var id=proposal.GetProperty("risk").GetProperty("locations")[0].GetProperty("id").GetGuid();
        var changed=JsonNode.Parse(proposal.GetRawText())!;changed["risk"]!["locations"]!.AsArray().RemoveAt(0);
        var slices=new[]{new ServicingEvidenceSlice(first,proposal,14),new ServicingEvidenceSlice(second,JsonSerializer.SerializeToElement(changed),14)};
        var definition=JsonSerializer.SerializeToElement(new {code="provide-cc-location-proof",riskItemId=id});
        Assert.Single(ServicingConditionRules.Parse(definition,slices,[first]));
        Assert.Throws<ArgumentException>(()=>ServicingConditionRules.Parse(definition,slices,[first,second]));
        Assert.Throws<ArgumentException>(()=>ServicingConditionRules.Parse(JsonSerializer.SerializeToElement(new {code="provide-cc-location-proof",riskItemId=Guid.NewGuid()}),slices,[first]));
        Assert.Throws<ArgumentException>(()=>ServicingConditionRules.Parse(JsonSerializer.SerializeToElement(new {code="provide-trading-history"}),slices,[first]));
        var required=ServicingEvidenceRules.Requirements(Context(),slices);
        Assert.Equal([first],Assert.Single(required,x=>x.Code=="cc-location-proof"&&x.RiskItemId==id).EffectiveDates);
    }
    [Fact]
    public void CommercialProofSchedulesRejectMixedProductsAndWrongCataloguePins()
    {
        var proposal=Proposal();var date=At("2026-10-01T00:00:00Z");var changed=JsonNode.Parse(proposal.GetRawText())!;changed["productCode"]="motor-trade-combined";
        Assert.Throws<ArgumentException>(()=>ServicingEvidenceRules.Requirements(Context(),[new(date,proposal,14),new(date.AddDays(1),JsonSerializer.SerializeToElement(changed),14)]));
        var context=Context();Assert.Throws<ArgumentException>(()=>ServicingEvidenceRules.Requirements(context with {Pins=context.Pins with {QuestionSetVersion="prototype-quote-1"}},[new(date,proposal,14)]));
    }
}
