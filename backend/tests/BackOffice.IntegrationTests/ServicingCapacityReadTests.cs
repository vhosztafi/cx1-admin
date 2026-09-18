using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingCapacityReads(BackOfficeDbContext db,DecisionFixture f,ServicingCapacityCase capacity,string scenario)
    {
        var reads=new ServicingCapacityReadModel(f.Factory,f.Clock);
        var page=await reads.ListAsync(f.Underwriter,capacity.DraftId);
        Assert.Equal(capacity.Id,Assert.Single(page.Items).Id);Assert.Null(page.NextBeforeId);
        var detail=await reads.GetAsync(f.Underwriter,capacity.DraftId,capacity.Id);
        Assert.Equal(capacity.ReferralId,detail.Case.ReferralId);Assert.Equal(capacity.CycleId,detail.Case.CycleId);
        Assert.NotNull(detail.Submission);Assert.Equal("Fictional servicing carrier request",detail.Submission.Body);
        Assert.False(detail.Ready);
        if(scenario=="capacity-worker-revoked") Assert.False(detail.CanWrite);
        if(scenario=="capacity-worker-conditional") Assert.NotEmpty(detail.Conditions);
        var messages=await reads.MessagesAsync(f.Underwriter,capacity.DraftId,capacity.Id);
        Assert.Equal("submission",Assert.Single(messages.Items).Kind);
        var responses=await reads.ResponsesAsync(f.Underwriter,capacity.DraftId,capacity.Id);
        Assert.Equal("demo-provider",Assert.Single(responses.Items).Provenance);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>reads.GetAsync(f.Underwriter,Guid.NewGuid(),capacity.Id))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>reads.GetAsync(f.Underwriter,capacity.DraftId,Guid.NewGuid()))).Status);
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>reads.MessagesAsync(f.Underwriter,capacity.DraftId,capacity.Id,pageSize:1000))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>reads.GetAsync(f.Underwriter with {Roles=new HashSet<string>()},capacity.DraftId,capacity.Id))).Status);
        Assert.Empty((await reads.MessagesAsync(f.Underwriter,capacity.DraftId,capacity.Id,afterSequence:messages.Items[^1].Sequence)).Items);
        var persisted=await db.Set<ServicingCapacityCase>().AsNoTracking().SingleAsync(x=>x.Id==capacity.Id);
        Assert.Equal(persisted.State,detail.Case.State);
    }
}
