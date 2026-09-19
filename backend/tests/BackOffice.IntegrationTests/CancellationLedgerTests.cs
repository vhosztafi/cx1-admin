using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private sealed record CancellationLedgerProbe(Guid DraftId, Guid Lease, byte[] Version, string Hash);
    private static async Task<CancellationLedgerProbe> CaptureCancellationLedger(DecisionFixture f, Guid termId, Guid versionId)
    {
        var drafts=new ServicingDraftService(f.Factory,f.Clock);
        static byte[] V(string etag)=>Convert.FromBase64String(etag.Trim('"'));
        var created=await drafts.CreateAsync(f.Underwriter,termId,V((await drafts.ListAsync(f.Underwriter,termId)).Etag),
            new("cancellation",versionId,JsonSerializer.SerializeToElement(new{localDate="2026-09-20",localTime="00:00",timeZone="Europe/London"}),
                "Fictional cancellation preview before another policy transaction"),Guid.NewGuid().ToString(),Guid.NewGuid());
        var lease=await drafts.LeaseAsync(f.Underwriter,created.ResourceId,V(created.Etag!),"acquire",null,null,Guid.NewGuid().ToString(),Guid.NewGuid());
        var fence=JsonSerializer.Deserialize<JsonElement>(lease.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();
        var proposal=JsonNode.Parse(lease.Body)!["proposal"]!;
        proposal["cancellationReasonCode"]="insured-request";
        var saved=await drafts.SaveAsync(f.Underwriter,created.ResourceId,V(lease.Etag!),fence,proposal.ToJsonString(),Guid.NewGuid().ToString(),Guid.NewGuid());
        var before=await new CancellationReviewService(f.Factory,f.Clock).ReadAsync(f.Underwriter,created.ResourceId);
        return new(created.ResourceId,fence,V(saved.Etag!),before.PreviewHash);
    }
    private static async Task VerifyCancellationLedgerChanged(DecisionFixture f,CancellationLedgerProbe probe,string expectedBlocker)
    {
        var service=new CancellationReviewService(f.Factory,f.Clock);var after=await service.ReadAsync(f.Underwriter,probe.DraftId);
        Assert.NotEqual(probe.Hash,after.PreviewHash);Assert.Contains(expectedBlocker,after.Blockers);
        Assert.Null(after.PreviewId);Assert.Null(after.ApprovalId);
        Assert.Equal("cancellation-preview-stale",(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.PrepareAsync(f.Underwriter,probe.DraftId,
            probe.Version,probe.Lease,probe.Hash,Guid.NewGuid().ToString(),Guid.NewGuid()))).Code);
    }
}
