using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    public sealed class CancellationSqlMovement
    {
        public Guid OriginalComponentId { get; set; }
        public string Code { get; set; }="";
        public int Ordinal { get; set; }
        public decimal? Amount { get; set; }
        public DateTimeOffset CoverageStartsAt { get; set; }
        public DateTimeOffset CoverageEndsAt { get; set; }
    }
    [Theory]
    [InlineData(false,"motor-trade-combined")]
    [InlineData(true,"motor-trade-combined")]
    [InlineData(true,"motor-trade-road-risks")]
    public async Task RealSqlCancellationIssueStorageRequiresExactApprovalAndImmutableUniqueDecision(bool issue,string product)
    {
        await WithDatabase(async(db,password)=>
        {
            var setup=await AcceptedIssue(db,password,product);var f=setup.Source;
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
            if(issue)
            {
                var competing=await drafts.CreateAsync(f.Underwriter,basis.TermId,V((await drafts.ListAsync(f.Underwriter,basis.TermId)).Etag),
                    new("adjustment",basis.Id,JsonSerializer.SerializeToElement(new{localDate="2026-10-14",localTime="00:00",timeZone="Europe/London"}),"Competing saved adjustment before cancellation"),K(),Guid.NewGuid());
                var competingLease=await drafts.LeaseAsync(f.Underwriter,competing.ResourceId,V(competing.Etag!),"acquire",null,null,K(),Guid.NewGuid());
                var competingBody=JsonNode.Parse(competingLease.Body)!;
                var competingFence=competingBody["lease"]!["leaseToken"]!.GetValue<Guid>();
                var risk=JsonNode.Parse(basis.SnapshotJson)!;
                competingBody["proposal"]!["changes"]=JsonSerializer.SerializeToNode(new[]{new{changeId=Guid.NewGuid(),
                    riskItemId=risk["risk"]!["drivers"]![0]!["id"]!.GetValue<Guid>(),kind="driver",operation="update",
                    payload=new{fullName="Fictional Correction",firstName="Fictional",surname="Correction"}}});
                var competingSaved=await drafts.SaveAsync(f.Underwriter,competing.ResourceId,V(competingLease.Etag!),competingFence,competingBody["proposal"]!.ToJsonString(),K(),Guid.NewGuid());
                var competingRevision=await db.Set<ServicingDraft>().Where(x=>x.Id==competing.ResourceId).Select(x=>x.CurrentRevisionId).SingleAsync();
                var ratingRequested=await new ServicingRatingService(f.Factory,f.Clock).RateAsync(f.Underwriter,competing.ResourceId,competingRevision!.Value,
                    V(competingSaved.Etag!),competingFence,"Rate competing correction before cancellation",K(),Guid.NewGuid());
                var ratingCycle=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.DraftId==competing.ResourceId);
                var ratingLease=Assert.IsType<JobLease>(await new SqlJobLeases(f.Factory,f.Clock).ClaimWorkAsync("servicing-rating",ratingCycle.WorkId));
                var ratingWorker=new ServicingRatingWorker(f.Factory,f.Clock);var lateRating=await ratingWorker.ExecuteProviderAsync(ratingLease);
                var input=new CancellationIssueInput(prepared.ResourceId,approval.Id,view.PreviewHash,"Issue the reviewed cancellation request");
                var key=K();
                Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>review.IssueAsync(f.Servicing,created.ResourceId,V(approved.Etag!),fence,input,K(),Guid.NewGuid()))).Status);
                Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>review.IssueAsync(f.Underwriter,created.ResourceId,V(approved.Etag!),fence,input with{PreviewHash=new string('0',64)},K(),Guid.NewGuid()))).Status);
                Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>review.IssueAsync(f.Underwriter,created.ResourceId,V(approved.Etag!),fence,input with{ApprovalId=Guid.NewGuid()},K(),Guid.NewGuid()))).Status);
                Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>review.IssueAsync(f.Underwriter,created.ResourceId,new byte[8],fence,input,K(),Guid.NewGuid()))).Status);
                Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>review.IssueAsync(f.Underwriter,created.ResourceId,V(approved.Etag!),Guid.NewGuid(),input,K(),Guid.NewGuid()))).Status);
                foreach(var mutation in new[]{"party","amount","interval","foreign-original","duplicate-original"})
                {
                    var fault=new CancellationPostingFault(mutation);
                    var invalid=new CancellationReviewService(new CancellationFaultFactory(db.Database.GetConnectionString()!,fault),f.Clock);
                    var failure=await Assert.ThrowsAsync<DbUpdateException>(()=>invalid.IssueAsync(f.Underwriter,created.ResourceId,V(approved.Etag!),fence,input,K(),Guid.NewGuid()));
                    Assert.True(fault.Applied);Assert.IsType<Microsoft.Data.SqlClient.SqlException>(failure.GetBaseException());
                    Assert.Equal(1,await db.Set<PolicyTransaction>().CountAsync());Assert.Equal(1,await db.Set<Journal>().CountAsync());
                    Assert.Equal(0,await db.Set<CancellationIssueDecision>().CountAsync());
                }
                await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_Test_CancellationRollback ON OutboxWork AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Kind='cancellation-task-close') THROW 51990,'Test final intent failure',1; END;");
                await Assert.ThrowsAsync<DbUpdateException>(()=>review.IssueAsync(f.Underwriter,created.ResourceId,V(approved.Etag!),fence,input,key,Guid.NewGuid()));
                Assert.Equal(1,await db.Set<PolicyTransaction>().CountAsync());Assert.Equal(1,await db.Set<PolicyVersion>().CountAsync());
                Assert.Equal(1,await db.Set<Journal>().CountAsync());Assert.Equal(0,await db.Set<CancellationConsequence>().CountAsync());Assert.Equal(0,await db.Set<CancellationIssueDecision>().CountAsync());
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_Test_CancellationRollback;");
                async Task SaveCompeting()
                {
                    try{await drafts.SaveAsync(f.Underwriter,competing.ResourceId,V(ratingRequested.Etag!),competingFence,competingBody["proposal"]!.ToJsonString(),K(),Guid.NewGuid());}
                    catch(QuoteOperationException error){Assert.Contains(error.Status,new[]{409,412});}
                }
                var firstIssue=review.IssueAsync(f.Underwriter,created.ResourceId,V(approved.Etag!),fence,input,key,Guid.NewGuid());
                var duplicateIssue=review.IssueAsync(f.Underwriter,created.ResourceId,V(approved.Etag!),fence,input,key,Guid.NewGuid());
                await Task.WhenAll(firstIssue,duplicateIssue,SaveCompeting());
                var issued=await firstIssue;Assert.Equal(issued.Body,(await duplicateIssue).Body);
                Assert.Equal("abandoned",await db.Set<ServicingDraft>().Where(x=>x.Id==competing.ResourceId).Select(x=>x.State).SingleAsync());
                Assert.False(await db.Set<ServicingLease>().AnyAsync(x=>x.DraftId==competing.ResourceId&&x.Active));
                Assert.True(await ratingWorker.ApplyAsync(ratingLease,lateRating));
                Assert.False(await ratingWorker.ApplyAsync(ratingLease,lateRating));
                Assert.Null(await db.Set<ServicingCycle>().Where(x=>x.Id==ratingCycle.Id).Select(x=>x.CurrentRatingId).SingleAsync());
                Assert.Null(await db.Set<ServicingDraft>().Where(x=>x.Id==competing.ResourceId).Select(x=>x.CurrentCycleId).SingleAsync());
                Assert.Equal("superseded",await db.Set<AdapterAttempt>().Where(x=>x.WorkId==ratingLease.WorkId&&x.AttemptNumber==ratingLease.Attempt).Select(x=>x.Outcome).SingleAsync());
                Assert.Equal("superseded",await db.Set<ServicingCycle>().Where(x=>x.Id==ratingCycle.Id).Select(x=>x.State).SingleAsync());
                Assert.Equal(201,issued.Status);
                Assert.Equal(issued.Body,(await review.IssueAsync(f.Underwriter,created.ResourceId,V(approved.Etag!),fence,input,key,Guid.NewGuid())).Body);
                Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>review.IssueAsync(f.Underwriter,created.ResourceId,V(approved.Etag!),fence,input,K(),Guid.NewGuid()))).Status);
                Assert.Equal(2,await db.Set<PolicyTransaction>().CountAsync());Assert.Equal(2,await db.Set<PolicyVersion>().CountAsync());
                Assert.Equal(2,await db.Set<Journal>().CountAsync(x=>x.PostedAt!=null));Assert.Equal(4,await db.Set<CancellationConsequence>().CountAsync());
                Assert.Equal("issued",await db.Set<ServicingDraft>().Where(x=>x.Id==created.ResourceId).Select(x=>x.State).SingleAsync());
                Assert.Equal(view.Amounts!.Posting.InvoiceDue,await db.Set<IssueFinancialObligation>().Where(x=>x.Purpose=="cancellation").Select(x=>x.InvoiceDue).SingleAsync());
                Assert.Equal(basis.ContentHash,await db.Set<PolicyVersion>().Where(x=>x.Id==basis.Id).Select(x=>x.ContentHash).SingleAsync());
                var reads=new PolicyReadService(f.Factory,f.Clock);
                Assert.Equal(basis.Id,(await reads.ReadAsync(f.Underwriter,basis.PolicyId))["versionId"]);
                var cancelled=await reads.ReadAtAsync(f.Underwriter,basis.PolicyId,view.EffectiveAt,f.Clock.GetUtcNow());
                Assert.Equal("cancelled",cancelled["coverageState"]);Assert.False(cancelled.ContainsKey("ratingId"));
                Assert.Equal(approval.Id,cancelled["cancellationApprovalId"]);
                var notice=await db.Set<CancellationConsequence>().AsNoTracking().SingleAsync(x=>x.Kind=="notice");
                var jobs=new SqlJobLeases(f.Factory,f.Clock);var noticeLease=await jobs.ClaimWorkAsync("cancellation-notice",notice.WorkId);Assert.NotNull(noticeLease);
                var worker=new CancellationNoticeWorker(f.Factory,f.Clock);var delivered=await worker.Deliver(noticeLease);Assert.NotNull(delivered);
                f.Clock.Current=f.Clock.Current.AddMinutes(10);
                var restartedLease=await new SqlJobLeases(f.Factory,f.Clock).ClaimWorkAsync("cancellation-notice",notice.WorkId);Assert.NotNull(restartedLease);
                var restarted=new CancellationNoticeWorker(f.Factory,f.Clock);
                Assert.Equal(delivered,await restarted.Deliver(restartedLease));Assert.False(await restarted.Apply(noticeLease,delivered.Value));
                Assert.True(await restarted.Apply(restartedLease,delivered.Value));Assert.False(await restarted.Apply(restartedLease,delivered.Value));
                Assert.Equal(1,await db.Set<CancellationNoticeReceipt>().CountAsync());
                Assert.Equal("succeeded",await db.Set<OutboxWork>().Where(x=>x.Id==notice.WorkId).Select(x=>x.State).SingleAsync());
                var savedIssue=await review.ReadIssueAsync(f.Underwriter,created.ResourceId);
                Assert.Equal(issued.ResourceId,savedIssue.TransactionId);Assert.Equal(4,savedIssue.Consequences.Count);
                Assert.Equal("succeeded",savedIssue.Consequences.Single(x=>x.Kind=="cancellation-notice").State);
                Assert.Equal("0.00",savedIssue.CashPaid);
                var decision=await db.Set<CancellationIssueDecision>().AsNoTracking().SingleAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason=N'Cancellation issue replay revocation test' WHERE Id={decision.AuthorityGrantId}");
                Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>review.IssueAsync(f.Underwriter,created.ResourceId,V(approved.Etag!),fence,input,key,Guid.NewGuid()))).Status);
                return;
            }
            CancellationIssueDecision Decision()=>new(){DraftId=created.ResourceId,PolicyId=basis.PolicyId,BaseTermId=basis.TermId,BaseVersionId=basis.Id,
                RevisionId=view.RevisionId,PreviewId=prepared.ResourceId,ApprovalId=approval.Id,PreviewHash=Convert.FromHexString(view.PreviewHash),
                AuthorityGrantId=approval.AuthorityGrantId,AuthorityVersionId=approval.AuthorityVersionId,ActorId=f.Underwriter.UserId,
                EffectiveAt=view.EffectiveAt,Reason="Issue the reviewed cancellation request",CreatedBy=f.Underwriter.UserId,CreatedAt=f.Clock.GetUtcNow()};
            var wrong=Decision();wrong.ApprovalId=Guid.NewGuid();db.Add(wrong);
            await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            var valid=Decision();db.Add(valid);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            var movements=await db.Database.SqlQuery<CancellationSqlMovement>($"SELECT OriginalComponentId,Code,Ordinal,Amount,CoverageStartsAt,CoverageEndsAt FROM CancellationExpectedReturnMovement WHERE DecisionId={valid.Id}").ToArrayAsync();
            Assert.Equal(view.Amounts!.Posting.Movements.Count,movements.Length);
            foreach(var expected in view.Amounts.Posting.Movements)
            {
                var actual=Assert.Single(movements,x=>x.OriginalComponentId==expected.OriginalComponentId);
                Assert.Equal(expected.Code,actual.Code);Assert.Equal(expected.Ordinal,actual.Ordinal);Assert.Equal(expected.Amount,actual.Amount);
                Assert.Equal(expected.StartsAt,actual.CoverageStartsAt);Assert.Equal(expected.EndsAt,actual.CoverageEndsAt);
            }
            var retained=await db.Set<CancellationIssueDecision>().SingleAsync(x=>x.Id==valid.Id);retained.Reason="Attempt to rewrite an issued decision";
            await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            db.Add(Decision());await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            Assert.Equal(1,await db.Set<CancellationIssueDecision>().CountAsync());
            Assert.Equal(1,await db.Set<PolicyTransaction>().CountAsync());
        });
    }
}
