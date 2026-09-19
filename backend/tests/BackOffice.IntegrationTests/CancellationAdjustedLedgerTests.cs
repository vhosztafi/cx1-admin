using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private sealed record ReadyCancellation(CancellationReviewService Service,Guid DraftId,byte[] Version,Guid Lease,CancellationIssueInput Input);
    private static async Task VerifyCancellationOfAdjustedLedger(BackOfficeDbContext db,DecisionFixture f,Guid baseVersionId,bool requireAdjusted=true,Func<ReadyCancellation,Task>? onPrepared=null)
    {
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==baseVersionId);
        var priorJournals=await db.Set<Journal>().CountAsync(x=>x.PostedAt!=null);
        var originals=await db.Set<IssueFinancialComponent>().AsNoTracking().Where(x=>x.OriginalComponentId==null).ToArrayAsync();
        var history=JsonSerializer.Serialize(originals.OrderBy(x=>x.Id));
        var drafts=new ServicingDraftService(f.Factory,f.Clock);var cancellation=new CancellationReviewService(f.Factory,f.Clock);
        static byte[] V(string etag)=>Convert.FromBase64String(etag.Trim('"'));
        static string K()=>Guid.NewGuid().ToString();
        var created=await drafts.CreateAsync(f.Underwriter,basis.TermId,V((await drafts.ListAsync(f.Underwriter,basis.TermId)).Etag),
            new("cancellation",basis.Id,JsonSerializer.SerializeToElement(new{localDate="2026-11-01",localTime="00:00",timeZone="Europe/London"}),"Cancellation of the actual adjusted ledger"),K(),Guid.NewGuid());
        var leased=await drafts.LeaseAsync(f.Underwriter,created.ResourceId,V(created.Etag!),"acquire",null,null,K(),Guid.NewGuid());
        var body=JsonNode.Parse(leased.Body)!;var fence=body["lease"]!["leaseToken"]!.GetValue<Guid>();body["proposal"]!["cancellationReasonCode"]="insured-request";
        var saved=await drafts.SaveAsync(f.Underwriter,created.ResourceId,V(leased.Etag!),fence,body["proposal"]!.ToJsonString(),K(),Guid.NewGuid());
        var uploaded=await cancellation.UploadAsync(f.Underwriter,created.ResourceId,V(saved.Etag!),fence,"cancellation-request",null,"request.txt","text/plain",
            Encoding.UTF8.GetBytes("Fictional insured request to cancel adjusted policy"),K(),Guid.NewGuid());
        var reviewed=await cancellation.ReviewEvidenceAsync(f.Underwriter,created.ResourceId,uploaded.ResourceId,V(uploaded.Etag!),fence,"accepted","Reviewed adjusted policy cancellation request",K(),Guid.NewGuid());
        var preview=await cancellation.ReadAsync(f.Underwriter,created.ResourceId);Assert.Empty(preview.Blockers);
        var prepared=await cancellation.PrepareAsync(f.Underwriter,created.ResourceId,V(reviewed.Etag!),fence,preview.PreviewHash,K(),Guid.NewGuid());
        var approved=await cancellation.ApproveAsync(f.Underwriter,created.ResourceId,V(prepared.Etag!),fence,prepared.ResourceId,preview.PreviewHash,"Approved cancellation of adjusted policy",K(),Guid.NewGuid());
        if(onPrepared is not null){await onPrepared(new(cancellation,created.ResourceId,V(approved.Etag!),fence,new(prepared.ResourceId,approved.ResourceId,preview.PreviewHash,"Issue adjusted policy cancellation")));return;}
        var issued=await cancellation.IssueAsync(f.Underwriter,created.ResourceId,V(approved.Etag!),fence,
            new(prepared.ResourceId,approved.ResourceId,preview.PreviewHash,"Issue adjusted policy cancellation"),K(),Guid.NewGuid());
        var returned=await db.Set<IssueFinancialComponent>().AsNoTracking().Where(x=>x.TransactionId==issued.ResourceId).ToArrayAsync();
        Assert.Equal(originals.Length,returned.Length);Assert.Equal(originals.Length,returned.Select(x=>x.OriginalComponentId).Distinct().Count());
        foreach(var component in returned)
        {
            var original=Assert.Single(originals,x=>x.Id==component.OriginalComponentId);
            Assert.Equal(original.Code,component.Code);Assert.Equal(original.CoverageEndsAt,component.CoverageEndsAt);
            Assert.Equal(preview.EffectiveAt,component.CoverageStartsAt);
            Assert.True(Math.Abs(component.Amount)<=Math.Abs(original.Amount));
            if(original.Amount<0)Assert.True(component.Amount>=0);
            if(original.Amount>0)Assert.True(component.Amount<=0);
        }
        using var snapshot=JsonDocument.Parse(basis.SnapshotJson);
        if(requireAdjusted&&snapshot.RootElement.GetProperty("productCode").GetString()=="motor-trade-combined")
            Assert.Contains(returned,x=>x.Code=="premium"&&x.Amount>0);
        Assert.Equal(history,JsonSerializer.Serialize((await db.Set<IssueFinancialComponent>().AsNoTracking().Where(x=>x.OriginalComponentId==null).ToArrayAsync()).OrderBy(x=>x.Id)));
        Assert.Equal(preview.Amounts!.Posting.InvoiceDue,await db.Set<IssueFinancialObligation>().Where(x=>x.TransactionId==issued.ResourceId).Select(x=>x.InvoiceDue).SingleAsync());
        Assert.Equal(priorJournals+1,await db.Set<Journal>().CountAsync(x=>x.PostedAt!=null));
        Assert.Equal(basis.ContentHash,await db.Set<PolicyVersion>().Where(x=>x.Id==basis.Id).Select(x=>x.ContentHash).SingleAsync());
    }
}
