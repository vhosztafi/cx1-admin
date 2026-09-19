using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlCancellationIssueStorageRequiresExactApprovalAndImmutableUniqueDecision()
    {
        await WithDatabase(async(db,password)=>
        {
            var setup=await AcceptedIssue(db,password,"motor-trade-combined");var f=setup.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var drafts=new ServicingDraftService(f.Factory,f.Clock);var review=new CancellationReviewService(f.Factory,f.Clock);
            static byte[] V(string value)=>Convert.FromBase64String(value.Trim('"'));
            static string K()=>Guid.NewGuid().ToString();
            var created=await drafts.CreateAsync(f.Underwriter,basis.TermId,V((await drafts.ListAsync(f.Underwriter,basis.TermId)).Etag),
                new("cancellation",basis.Id,JsonSerializer.SerializeToElement(new{localDate="2026-10-15",localTime="00:00",timeZone="Europe/London"}),"Fictional cancellation issue storage test"),K(),Guid.NewGuid());
            var lease=await drafts.LeaseAsync(f.Underwriter,created.ResourceId,V(created.Etag!),"acquire",null,null,K(),Guid.NewGuid());
            var body=JsonNode.Parse(lease.Body)!;var fence=body["lease"]!["leaseToken"]!.GetValue<Guid>();
            body["proposal"]!["cancellationReasonCode"]="insured-request";
            var saved=await drafts.SaveAsync(f.Underwriter,created.ResourceId,V(lease.Etag!),fence,body["proposal"]!.ToJsonString(),K(),Guid.NewGuid());
            var upload=await review.UploadAsync(f.Underwriter,created.ResourceId,V(saved.Etag!),fence,"cancellation-request",null,"request.txt","text/plain",
                Encoding.UTF8.GetBytes("Fictional insured request for cancellation, retained for the issue decision."),K(),Guid.NewGuid());
            var evidence=await review.ReviewEvidenceAsync(f.Underwriter,created.ResourceId,upload.ResourceId,V(upload.Etag!),fence,"accepted","Insured cancellation request reviewed",K(),Guid.NewGuid());
            var view=await review.ReadAsync(f.Underwriter,created.ResourceId);Assert.Empty(view.Blockers);
            var prepared=await review.PrepareAsync(f.Underwriter,created.ResourceId,V(evidence.Etag!),fence,view.PreviewHash,K(),Guid.NewGuid());
            var approved=await review.ApproveAsync(f.Underwriter,created.ResourceId,V(prepared.Etag!),fence,prepared.ResourceId,view.PreviewHash,"Cancellation request independently checked",K(),Guid.NewGuid());
            var approval=await db.Set<CancellationApproval>().AsNoTracking().SingleAsync(x=>x.Id==approved.ResourceId);
            CancellationIssueDecision Decision()=>new(){DraftId=created.ResourceId,PolicyId=basis.PolicyId,BaseTermId=basis.TermId,BaseVersionId=basis.Id,
                RevisionId=view.RevisionId,PreviewId=prepared.ResourceId,ApprovalId=approval.Id,PreviewHash=Convert.FromHexString(view.PreviewHash),
                AuthorityGrantId=approval.AuthorityGrantId,AuthorityVersionId=approval.AuthorityVersionId,ActorId=f.Underwriter.UserId,
                EffectiveAt=view.EffectiveAt,Reason="Issue the reviewed cancellation request",CreatedBy=f.Underwriter.UserId,CreatedAt=f.Clock.GetUtcNow()};
            var wrong=Decision();wrong.ApprovalId=Guid.NewGuid();db.Add(wrong);
            await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            var valid=Decision();db.Add(valid);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            var retained=await db.Set<CancellationIssueDecision>().SingleAsync(x=>x.Id==valid.Id);retained.Reason="Attempt to rewrite an issued decision";
            await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            db.Add(Decision());await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            Assert.Equal(1,await db.Set<CancellationIssueDecision>().CountAsync());
            Assert.Equal(1,await db.Set<PolicyTransaction>().CountAsync());
        });
    }
}
