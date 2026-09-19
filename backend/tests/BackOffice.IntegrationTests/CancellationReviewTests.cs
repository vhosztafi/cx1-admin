using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlCancellationReviewTestsRequireDeliveredNoticeAndDistinctSeniorAndInvalidateConfiguration()
    {
        await WithDatabase(async(db,password)=>
        {
            var setup=await AcceptedIssue(db,password,"motor-trade-combined");var f=setup.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var issued=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var drafts=new ServicingDraftService(f.Factory,f.Clock);var service=new CancellationReviewService(f.Factory,f.Clock);
            static byte[] V(string etag)=>Convert.FromBase64String(etag.Trim('"'));
            static string K()=>Guid.NewGuid().ToString();
            var listed=await drafts.ListAsync(f.Underwriter,issued.TermId);
            var created=await drafts.CreateAsync(f.Underwriter,issued.TermId,V(listed.Etag),new("cancellation",issued.Id,
                JsonSerializer.SerializeToElement(new{localDate="2026-10-15",localTime="00:00",timeZone="Europe/London"}),"Fictional non-payment cancellation request"),K(),Guid.NewGuid());
            var id=created.ResourceId;
            var lease=await drafts.LeaseAsync(f.Underwriter,id,V(created.Etag!),"acquire",null,null,K(),Guid.NewGuid());
            var body=JsonNode.Parse(lease.Body)!;var fence=body["lease"]!["leaseToken"]!.GetValue<Guid>();var proposal=body["proposal"]!;
            proposal["cancellationReasonCode"]="non-payment";
            var saved=await drafts.SaveAsync(f.Underwriter,id,V(lease.Etag!),fence,proposal.ToJsonString(),K(),Guid.NewGuid());
            var uploaded=await service.UploadAsync(f.Underwriter,id,V(saved.Etag!),fence,"cancellation-notice",null,"notice.txt","text/plain",
                Encoding.UTF8.GetBytes("Fictional non-payment notice with actual delivery requiring review."),K(),Guid.NewGuid());
            var reviewed=await service.ReviewEvidenceAsync(f.Underwriter,id,uploaded.ResourceId,V(uploaded.Etag!),fence,"accepted","Fictional notice content accepted for review",K(),Guid.NewGuid());
            Assert.Contains("cancellation-notice-delivery-required",(await service.ReadAsync(f.Underwriter,id)).Blockers);
            uploaded=await service.UploadAsync(f.Underwriter,id,V(reviewed.Etag!),fence,"cancellation-notice",f.Clock.GetUtcNow(),"delivered-notice.txt","text/plain",
                Encoding.UTF8.GetBytes("Fictional delivered notice exact evidence for the cancellation."),K(),Guid.NewGuid());
            reviewed=await service.ReviewEvidenceAsync(f.Underwriter,id,uploaded.ResourceId,V(uploaded.Etag!),fence,"accepted","Fictional notice delivery independently checked",K(),Guid.NewGuid());
            var preview=await service.ReadAsync(f.Underwriter,id);Assert.Empty(preview.Blockers);Assert.False(preview.CanApprove);
            var prepared=await service.PrepareAsync(f.Underwriter,id,V(reviewed.Etag!),fence,preview.PreviewHash,K(),Guid.NewGuid());
            Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ApproveAsync(f.Underwriter,id,V(prepared.Etag!),fence,prepared.ResourceId,
                preview.PreviewHash,"Requester must not approve this cancellation",K(),Guid.NewGuid()))).Status);
            var seniorUser=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Email=="senior-underwriter@cover.example");
            var senior=new ActorContext(seniorUser.Id,seniorUser.TeamId,null,new HashSet<string>{"senior-underwriter"});
            var takeover=await drafts.LeaseAsync(senior,id,V(prepared.Etag!),"takeover",null,"Independent senior cancellation review",K(),Guid.NewGuid());
            var seniorFence=JsonNode.Parse(takeover.Body)!["lease"]!["leaseToken"]!.GetValue<Guid>();
            Assert.True((await service.ReadAsync(senior,id)).CanApprove);
            var approval=await service.ApproveAsync(senior,id,V(takeover.Etag!),seniorFence,prepared.ResourceId,preview.PreviewHash,"Independent senior accepts cancellation evidence",K(),Guid.NewGuid());
            Assert.Equal(approval.ResourceId,(await service.ReadAsync(senior,id)).ApprovalId);
            var setting=await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Scope==CancellationConfiguration.Scope);
            db.Add(new SettingVersion{Scope=setting.Scope,Version=setting.Version+1,EffectiveFrom=f.Clock.GetUtcNow(),Values=setting.Values,CreatedAt=f.Clock.GetUtcNow(),CreatedBy=senior.UserId});
            await db.SaveChangesAsync();db.ChangeTracker.Clear();
            var changed=await service.ReadAsync(senior,id);Assert.NotEqual(preview.PreviewHash,changed.PreviewHash);Assert.Null(changed.ApprovalId);
            Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.PrepareAsync(senior,id,V(approval.Etag!),seniorFence,preview.PreviewHash,K(),Guid.NewGuid()))).Status);
            Assert.Equal(1,await db.Set<CancellationApproval>().CountAsync());Assert.Equal(1,await db.Set<PolicyTransaction>().CountAsync());
        });
    }

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public async Task RealSqlCancellationReviewTestsPersistExactReviewInvalidateOnEditAndReauthorizeReplay(string product)
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password, product); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var issued = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var drafts = new ServicingDraftService(f.Factory, f.Clock); var service = new CancellationReviewService(f.Factory, f.Clock);
            static byte[] V(string etag) => Convert.FromBase64String(etag.Trim('"'));
            static string Key() => Guid.NewGuid().ToString();
            var listed = await drafts.ListAsync(f.Underwriter, issued.TermId);
            var created = await drafts.CreateAsync(f.Underwriter, issued.TermId, V(listed.Etag),
                new("cancellation", issued.Id, JsonSerializer.SerializeToElement(new { localDate = "2026-10-15", localTime = "00:00", timeZone = "Europe/London" }), "Fictional insured cancellation request"), Key(), Guid.NewGuid());
            var id = created.ResourceId;
            var lease = await drafts.LeaseAsync(f.Underwriter, id, V(created.Etag!), "acquire", null, null, Key(), Guid.NewGuid());
            var body = JsonNode.Parse(lease.Body)!;var fence = body["lease"]!["leaseToken"]!.GetValue<Guid>();
            var proposal = body["proposal"]!;proposal["cancellationReasonCode"] = "insured-request";
            var saved = await drafts.SaveAsync(f.Underwriter, id, V(lease.Etag!), fence, proposal.ToJsonString(), Key(), Guid.NewGuid());
            var missing = await service.ReadAsync(f.Underwriter, id);
            Assert.Contains("evidence-required:cancellation-request", missing.Blockers);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.PrepareAsync(f.Underwriter, id, V(saved.Etag!), fence, missing.PreviewHash, Key(), Guid.NewGuid()))).Status);
            var uploaded = await service.UploadAsync(f.Underwriter, id, V(saved.Etag!), fence, "cancellation-request", null,
                "request.txt", "text/plain", Encoding.UTF8.GetBytes("Fictional insured request to cancel the policy on 15 October."), Key(), Guid.NewGuid());
            var reviewed = await service.ReviewEvidenceAsync(f.Underwriter, id, uploaded.ResourceId, V(uploaded.Etag!), fence,
                "accepted", "Reviewed fictional cancellation request evidence", Key(), Guid.NewGuid());
            var preview = await service.ReadAsync(f.Underwriter, id);
            Assert.Empty(preview.Blockers); Assert.NotNull(preview.Amounts); Assert.True(preview.CanApprove);
            await VerifyCancellationHttp(db, f, password, id, reviewed.Etag!, fence, preview.PreviewHash);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.PrepareAsync(f.Underwriter, id, V(reviewed.Etag!), fence, missing.PreviewHash, Key(), Guid.NewGuid()))).Status);
            var prepared = await service.PrepareAsync(f.Underwriter, id, V(reviewed.Etag!), fence, preview.PreviewHash, Key(), Guid.NewGuid());
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT CancellationPreview(Id,DraftId,PolicyId,BaseTermId,BaseVersionId,RevisionId,RuleSettingVersionId,RuleVersion,ReasonCode,EffectiveAt,InputHash,InputJson,ResultJson,CreatedAt,CreatedBy) SELECT NEWID(),DraftId,PolicyId,BaseTermId,BaseVersionId,RevisionId,RuleSettingVersionId,RuleVersion,ReasonCode,EffectiveAt,InputHash,InputJson,N'{{}}',CreatedAt,CreatedBy FROM CancellationPreview WHERE Id={prepared.ResourceId}"));
            var approvalKey = Key(); var approvalVersion = V(prepared.Etag!);
            var approved = await service.ApproveAsync(f.Underwriter, id, approvalVersion, fence, prepared.ResourceId, preview.PreviewHash,
                "Explicit approval of the reviewed cancellation", approvalKey, Guid.NewGuid());
            Assert.True((await service.ApproveAsync(f.Underwriter, id, approvalVersion, fence, prepared.ResourceId, preview.PreviewHash,
                "Explicit approval of the reviewed cancellation", approvalKey, Guid.NewGuid())).Replayed);
            Assert.Equal(approved.ResourceId, (await service.ReadAsync(f.Underwriter, id)).ApprovalId);
            Assert.Equal(1, await db.Set<PolicyTransaction>().CountAsync());
            Assert.Equal(1, await db.Set<Journal>().CountAsync());
            Assert.Equal(0, await db.Set<ServicingCycle>().CountAsync(x => x.DraftId == id));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CancellationPreview SET ResultJson=N'{{}}' WHERE Id={prepared.ResourceId}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM CancellationApproval WHERE Id={approved.ResourceId}"));
            var authority = await db.Set<CancellationApproval>().AsNoTracking().SingleAsync(x => x.Id == approved.ResourceId);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason=N'Fictional revocation for cancellation test' WHERE Id={authority.AuthorityGrantId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ApproveAsync(f.Underwriter, id, approvalVersion, fence, prepared.ResourceId, preview.PreviewHash,
                "Explicit approval of the reviewed cancellation", approvalKey, Guid.NewGuid()))).Status);
            Assert.Null((await service.ReadAsync(f.Underwriter, id)).ApprovalId);
            proposal["reason"] = "Amended fictional cancellation request notes";
            await drafts.SaveAsync(f.Underwriter, id, V(approved.Etag!), fence, proposal.ToJsonString(), Key(), Guid.NewGuid());
            var amended = await service.ReadAsync(f.Underwriter, id);
            Assert.NotEqual(preview.PreviewHash, amended.PreviewHash); Assert.Null(amended.ApprovalId); Assert.Null(amended.PreviewId);
            Assert.Contains("evidence-required:cancellation-request", amended.Blockers);
            Assert.Equal(1, await db.Set<CancellationApproval>().CountAsync());
        });
    }
}
