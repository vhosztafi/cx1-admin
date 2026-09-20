using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialServicingRatingInputTests
{
    private static ServicingRatingRequestInput Input()
    {
        var term=QuoteRatingRulesTests.Term();
        var projected=new ProjectedCommercialUnderwritingInput(CommercialRatingTests.Facts(),term,JsonSerializer.SerializeToElement(new {productCode="commercial-combined"}));
        return new() { Format="commercial-servicing-rating-input-1",DraftId=Guid.NewGuid(),RevisionId=Guid.NewGuid(),PolicyId=Guid.NewGuid(),BaseTermId=Guid.NewGuid(),BaseVersionId=Guid.NewGuid(),
            ProductVersionId=Guid.NewGuid(),AgencyTermsVersionId=Guid.NewGuid(),RatingRuleVersionId=Guid.NewGuid(),BinderVersionId=Guid.NewGuid(),AuthorityVersionId=Guid.NewGuid(),RuntimeVersionId=Guid.NewGuid(),
            ScenarioVersionId=Guid.NewGuid(),ServicingSettingVersionId=Guid.NewGuid(),RequestedBy=Guid.NewGuid(),RequestedAt=DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            BaseContentHash=new string('a',64),RevisionContentHash=new string('b',64),Term=term,BaseAnnualPremium=4195m,CommissionBasisPoints=1500,Fee=25m,
            RatingDefinition=CommercialRatingTests.Configuration(),Slices=[new(DateTimeOffset.Parse("2026-10-01T00:00:00Z"),[Guid.NewGuid()],null!){Commercial=projected}] };
    }
    [Fact]
    public void CommercialPersistenceRoundTripsExactHashAndNeverInventsMotorFacts()
    {
        var input=Input();var encoded=ServicingRatingInput.Encode(input);var restored=ServicingRatingInput.Read(encoded.Json,encoded.ContentHash);
        Assert.True(restored.IsCommercial);Assert.Null(restored.Slices[0].Input);Assert.NotNull(restored.Slices[0].Commercial);
        Assert.Equal(25m,ServicingRatingInput.Calculate(restored).GrossPayable);
        Assert.Equal(encoded.Json,ServicingRatingInput.Encode(restored).Json);
        var changed=input with {BaseContentHash=new string('c',64)};Assert.NotEqual(encoded.ContentHash,ServicingRatingInput.Encode(changed).ContentHash);
        Assert.Throws<ArgumentException>(()=>ServicingRatingInput.Read(encoded.Json.Replace("4195","4196",StringComparison.Ordinal),encoded.ContentHash));
    }
    [Fact]
    public void MixedFormatsWrongTermUnknownFieldsAndRenewalContextAreRejected()
    {
        var input=Input();
        foreach(var invalid in new[]{input with {Format="servicing-rating-input-1"},input with {Format="servicing-rating-input-2"},input with {Slices=[input.Slices[0] with {Commercial=null}]},
            input with {Slices=[input.Slices[0] with {Commercial=input.Slices[0].Commercial! with {Term=input.Term with {EndsAt=input.Term.EndsAt.AddDays(1)}}}]},
            input with {Renewal=new(Guid.NewGuid(),null,null,null,null,false,"fake",5000,100)},input with {RatingDefinition=QuoteRatingRulesTests.Definition("rating")}})
            Assert.Throws<ArgumentException>(()=>ServicingRatingInput.Encode(invalid));
        var encoded=ServicingRatingInput.Encode(input);var json=JsonNode.Parse(encoded.Json)!;json["inventedAuthority"]=true;
        var text=json.ToJsonString();Assert.Throws<ArgumentException>(()=>ServicingRatingInput.Read(text,SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }
}
