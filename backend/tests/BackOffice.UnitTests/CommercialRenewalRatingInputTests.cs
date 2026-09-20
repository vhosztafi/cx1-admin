using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialRenewalRatingInputTests
{
    private static ServicingRatingRequestInput Input()
    {
        var adjustment=CommercialServicingRatingInputTests.Input();
        return adjustment with {Format="commercial-servicing-rating-input-2",Fee=45m,
            Renewal=new(Guid.NewGuid(),null,null,null,null,false,"commercial-demo-renewal-1",5000,800),
            Slices=[adjustment.Slices[0] with {EffectiveAt=adjustment.Term.StartsAt,ChangeIds=[]}]};
    }

    [Fact]
    public void CommercialRenewalHasAnIndependentClosedFormatAndFullTermPrice()
    {
        var input=Input();var encoded=ServicingRatingInput.Encode(input);var read=ServicingRatingInput.Read(encoded.Json,encoded.ContentHash);
        Assert.True(read.IsCommercial);Assert.NotNull(read.Renewal);Assert.Null(read.Slices[0].Input);
        Assert.Equal(4195m,ServicingRatingInput.Calculate(read).Premium);Assert.Equal(45m,ServicingRatingInput.Calculate(read).Fee);
        Assert.Equal(encoded.Json,ServicingRatingInput.Encode(read).Json);
        Assert.Throws<ArgumentException>(()=>ServicingRatingInput.Encode(input with {Format="commercial-servicing-rating-input-1"}));
        Assert.Throws<ArgumentException>(()=>ServicingRatingInput.Encode(input with {Renewal=null}));
        Assert.Throws<ArgumentException>(()=>ServicingRatingInput.Encode(input with {Slices=[input.Slices[0] with {EffectiveAt=input.Term.StartsAt.AddDays(1)}]}));
    }

    [Fact]
    public void ExperienceMustDescribeTheSameCommercialBaseRevisionAndCompleteRisk()
    {
        var input=Input();using var stream=typeof(CommercialRenewalRatingInputTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!;
        using var document=JsonDocument.Parse(stream);var proposal=document.RootElement.Clone();
        var subjects=CommercialRenewalExperienceRules.Subjects(input.BaseVersionId,input.RevisionId,proposal,
            new(input.ProductVersionId,input.AgencyTermsVersionId,"1.0",CommercialCaptureRules.QuestionVersion,CommercialCaptureRules.ReferenceVersion));
        var facts=new RenewalExperienceFacts(new(2025,1,1),new(2026,1,1),0,0,0,1000,"agency","Fictional commercial experience",Guid.NewGuid(),subjects);
        input=input with {Renewal=input.Renewal! with {ExperienceVersionId=Guid.NewGuid(),ExperienceReviewId=Guid.NewGuid(),Experience=facts,EvidenceAccepted=true},
            Slices=[input.Slices[0] with {Commercial=input.Slices[0].Commercial! with {Pricing=JsonSerializer.SerializeToElement(new{proposal})}}]};
        var encoded=ServicingRatingInput.Encode(input);
        var restored=ServicingRatingInput.Read(encoded.Json,encoded.ContentHash);
        Assert.True(ServicingRatingInput.SameRenewalContext(input.Renewal,restored.Renewal));
        Assert.False(ServicingRatingInput.SameRenewalContext(input.Renewal,restored.Renewal! with {Experience=facts with {CommercialSubjects=subjects with {RevisionId=Guid.NewGuid()}}}));
        foreach(var invalid in new[]{subjects with {BaseVersionId=Guid.NewGuid()},subjects with {RevisionId=Guid.NewGuid()},subjects with {InputHash=new string('f',64)}})
            Assert.Throws<ArgumentException>(()=>ServicingRatingInput.Encode(input with {Renewal=input.Renewal! with {Experience=facts with {CommercialSubjects=invalid}}}));
        Assert.Throws<ArgumentException>(()=>ServicingRatingInput.Encode(input with {Renewal=input.Renewal! with {Experience=facts with {CommercialSubjects=null}}}));
    }
}
