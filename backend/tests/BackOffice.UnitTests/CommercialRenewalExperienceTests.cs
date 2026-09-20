using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialRenewalExperienceTests
{
    private static JsonElement Proposal()
    {
        using var stream=typeof(CommercialRenewalExperienceTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!;
        using var document=JsonDocument.Parse(stream);return document.RootElement.Clone();
    }
    private static QuoteVersionPins Pins()=>new(Guid.NewGuid(),Guid.NewGuid(),"1.0",CommercialCaptureRules.QuestionVersion,CommercialCaptureRules.ReferenceVersion);

    [Fact]
    public void FullSavedCommercialRiskOwnsItsPropertyWageLiabilityAndLossManifest()
    {
        var proposal=Proposal();var subjects=CommercialRenewalExperienceRules.Subjects(Guid.NewGuid(),Guid.NewGuid(),proposal,Pins());
        Assert.Equal("commercial-renewal-subjects-1",subjects.Format);
        Assert.Equal(proposal.GetProperty("risk").GetProperty("locations").EnumerateArray().Select(x=>x.GetProperty("id").GetGuid()).Order(),subjects.PropertyLocationIds);
        Assert.Equal(proposal.GetProperty("risk").GetProperty("wages").GetArrayLength(),subjects.WageCategoryIds.Count);
        Assert.Empty(subjects.LossRecordIds);Assert.Contains("employers-liability",subjects.LiabilitySections);Assert.Contains("public-liability",subjects.LiabilitySections);
        Assert.DoesNotContain("products-liability",subjects.LiabilitySections);
        Assert.True(CommercialRenewalExperienceRules.Matches(subjects,subjects));
        Assert.False(CommercialRenewalExperienceRules.Matches(null,subjects));
        Assert.False(CommercialRenewalExperienceRules.Matches(subjects with {BaseVersionId=Guid.NewGuid()},subjects));
        Assert.False(CommercialRenewalExperienceRules.Matches(subjects with {RevisionId=Guid.NewGuid()},subjects));
        Assert.False(CommercialRenewalExperienceRules.Matches(subjects with {PropertyLocationIds=[Guid.NewGuid()]},subjects));
        Assert.False(CommercialRenewalExperienceRules.Matches(subjects with {WageCategoryIds=[]},subjects));
        Assert.False(CommercialRenewalExperienceRules.Matches(subjects with {LossRecordIds=[Guid.NewGuid()]},subjects));
    }

    [Fact]
    public void SameSubjectIdentitiesCannotCarryReviewOntoChangedCommercialRisk()
    {
        var basis=Guid.NewGuid();var revision=Guid.NewGuid();var pins=Pins();var proposal=Proposal();
        var prior=CommercialRenewalExperienceRules.Subjects(basis,revision,proposal,pins);
        var changed=JsonNode.Parse(proposal.GetRawText())!;changed["risk"]!["locations"]![0]!["stock"]="12345.67";
        var current=CommercialRenewalExperienceRules.Subjects(basis,revision,JsonSerializer.SerializeToElement(changed),pins);
        Assert.Equal(prior.PropertyLocationIds,current.PropertyLocationIds);Assert.NotEqual(prior.InputHash,current.InputHash);
        Assert.False(CommercialRenewalExperienceRules.Matches(prior,current));
    }

    [Fact]
    public void RetainedEmployersLimitDoesNotImplySelectedCover()
    {
        var proposal=JsonNode.Parse(Proposal().GetRawText())!;
        proposal["risk"]!["declarations"]!["answers"]!.AsArray().Single(x=>x!["questionId"]!.GetValue<string>()=="prototype.quote.36ef01068295")!["value"]=false;
        var subjects=CommercialRenewalExperienceRules.Subjects(Guid.NewGuid(),Guid.NewGuid(),JsonSerializer.SerializeToElement(proposal),Pins());
        Assert.DoesNotContain("employers-liability",subjects.LiabilitySections);
    }

    [Fact]
    public void InvalidOrForeignCaptureCannotProduceACommercialManifest()
    {
        var proposal=JsonNode.Parse(Proposal().GetRawText())!;proposal["productCode"]="motor-trade-combined";
        Assert.Throws<ArgumentException>(()=>CommercialRenewalExperienceRules.Subjects(Guid.NewGuid(),Guid.NewGuid(),JsonSerializer.SerializeToElement(proposal),Pins()));
        Assert.Throws<ArgumentException>(()=>CommercialRenewalExperienceRules.Subjects(Guid.Empty,Guid.NewGuid(),Proposal(),Pins()));
    }
}
