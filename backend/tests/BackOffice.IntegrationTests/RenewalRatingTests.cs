using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public async Task RealSqlRenewalRatingRetainsPreparedUnchangedRiskAndUnknownExperience(string product)
    {
        await WithDatabase(async(db,password)=>
        {
            var setup=await AcceptedIssue(db,password,product);var f=setup.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var issued=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var drafts=new ServicingDraftService(f.Factory,f.Clock);var renewals=new RenewalPreparationService(f.Factory,f.Clock);
            static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
            var created=await drafts.CreateAsync(f.Servicing,issued.TermId,Version((await drafts.ListAsync(f.Servicing,issued.TermId)).Etag),
                new("renewal",issued.Id,JsonSerializer.SerializeToElement(new{localDate="2027-10-01",localTime="01:00",timeZone="Europe/London"}),
                    "Prepare unchanged fictional renewal"),Guid.NewGuid().ToString(),Guid.NewGuid());
            var leased=await drafts.LeaseAsync(f.Servicing,created.ResourceId,Version(created.Etag!),"acquire",null,null,Guid.NewGuid().ToString(),Guid.NewGuid());
            using var lease=JsonDocument.Parse(leased.Body);var fence=lease.RootElement.GetProperty("lease").GetProperty("leaseToken").GetGuid();
            var prepared=await renewals.PrepareAsync(f.Servicing,created.ResourceId,Version(leased.Etag!),fence,12,null,Guid.NewGuid().ToString(),Guid.NewGuid());
            var draft=await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x=>x.Id==created.ResourceId);
            var ratings=new ServicingRatingService(f.Factory,f.Clock);var key=Guid.NewGuid().ToString();
            var originalProposal=(await db.Set<ServicingRevision>().AsNoTracking().SingleAsync(x=>x.Id==draft.CurrentRevisionId)).ProposalJson;
            var lateProposal=JsonNode.Parse(originalProposal)!;
            lateProposal["commonEffectiveIntent"]!["localDate"]="2027-10-02";
            var lateSaved=await drafts.SaveAsync(f.Servicing,draft.Id,Version(prepared.Etag!),fence,lateProposal.ToJsonString(),Guid.NewGuid().ToString(),Guid.NewGuid());
            var lateDraft=await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x=>x.Id==draft.Id);
            Assert.Equal("renewal-effective-inception-required",(await Assert.ThrowsAsync<QuoteOperationException>(()=>ratings.RateAsync(f.Servicing,draft.Id,
                lateDraft.CurrentRevisionId!.Value,Version(lateSaved.Etag!),fence,"Reject a shifted unchanged renewal",Guid.NewGuid().ToString(),Guid.NewGuid()))).Code);
            var restored=await drafts.SaveAsync(f.Servicing,draft.Id,Version(lateSaved.Etag!),fence,originalProposal,Guid.NewGuid().ToString(),Guid.NewGuid());
            draft=await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x=>x.Id==draft.Id);
            var requested=await ratings.RateAsync(f.Servicing,draft.Id,draft.CurrentRevisionId!.Value,Version(restored.Etag!),fence,
                "Rate the full unchanged renewal risk",key,Guid.NewGuid());
            Assert.True((await ratings.RateAsync(f.Servicing,draft.Id,draft.CurrentRevisionId.Value,Version(restored.Etag!),fence,
                "Rate the full unchanged renewal risk",key,Guid.NewGuid())).Replayed);
            var cycle=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==requested.ResourceId);
            var input=ServicingRatingInput.Read(cycle.InputJson,cycle.InputHash);
            Assert.Equal(prepared.ResourceId,input.Renewal!.PreparationVersionId);Assert.Null(input.Renewal.ExperienceVersionId);
            Assert.Empty(Assert.Single(input.Slices).ChangeIds);
            foreach(var tampering in new[]{"preparation","evidence","term","fee"})
            {
                var altered=JsonNode.Parse(cycle.InputJson)!;
                switch(tampering)
                {
                    case "preparation":altered["renewal"]!["preparationVersionId"]=Guid.NewGuid();break;
                    case "evidence":altered["renewal"]!["evidenceAccepted"]=true;break;
                    case "term":altered["term"]!["endsAt"]=input.Term.EndsAt.AddDays(1);break;
                    case "fee":altered["fee"]=1m;break;
                }
                var forgedId=Guid.NewGuid();var forgedWork=new OutboxWork{Kind="servicing-rating",SubjectRecordId=forgedId,
                    OperationKey="servicing-rating/"+forgedId.ToString("N"),ScenarioVersionId=cycle.ScenarioVersionId,
                    NextAttemptAt=f.Clock.GetUtcNow(),CorrelationId=Guid.NewGuid(),CreatedBy=f.Servicing.UserId,CreatedAt=f.Clock.GetUtcNow(),UpdatedAt=f.Clock.GetUtcNow()};
                db.Add(forgedWork);await db.SaveChangesAsync();var json=altered.ToJsonString();var hash=SHA256.HashData(Encoding.UTF8.GetBytes(json));
                var rejected=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingCycle (Id,DraftId,PolicyId,BaseTermId,BaseVersionId,RevisionId,ProductId,ProductVersionId,AgencyTermsVersionId,RatingRuleVersionId,BinderVersionId,AuthorityVersionId,RuntimeVersionId,ScenarioVersionId,ServicingSettingVersionId,RenewalPreparationVersionId,Sequence,WorkId,InputHash,InputJson,RequestedBy,State,CreatedBy,CreatedAt,UpdatedAt) SELECT {forgedId},DraftId,PolicyId,BaseTermId,BaseVersionId,RevisionId,ProductId,ProductVersionId,AgencyTermsVersionId,RatingRuleVersionId,BinderVersionId,AuthorityVersionId,RuntimeVersionId,ScenarioVersionId,ServicingSettingVersionId,RenewalPreparationVersionId,2,{forgedWork.Id},{hash},{json},RequestedBy,'rating-pending',CreatedBy,CreatedAt,UpdatedAt FROM ServicingCycle WHERE Id={cycle.Id}"));
                Assert.Equal(tampering=="fee"?51240:51731,rejected.Number);
            }
            var jobs=new SqlJobLeases(f.Factory,f.Clock);var worker=new ServicingRatingWorker(f.Factory,f.Clock);
            var claimed=Assert.IsType<JobLease>(await jobs.ClaimWorkAsync("servicing-rating",cycle.WorkId));
            var calculated=await worker.ExecuteProviderAsync(claimed);
            Assert.True(await worker.ApplyAsync(claimed,calculated));
            Assert.True((await new ServicingRatingReadModel(f.Factory,f.Clock).ReadAsync(f.Servicing,draft.Id)).Current!.Applicable);
            Assert.True(calculated.Rating!.Premium>0);Assert.Equal(35m,calculated.Rating.Fee);
            Assert.Contains(await db.Set<ServicingReferral>().Where(x=>x.CycleId==cycle.Id).ToArrayAsync(),x=>x.RuleCode=="UW-31-information");
            await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCycle SET RenewalPreparationVersionId=NULL WHERE Id={cycle.Id}"));
            var referrals=new ServicingReferralService(f.Factory,f.Clock);
            var page=await referrals.ReadReferralsAsync(f.Underwriter,draft.Id);
            var information=Assert.Single(page.Items,x=>x.RuleCode=="UW-31-information");Assert.False(information.DecisionReady);
            var takeover=await drafts.LeaseAsync(f.Underwriter,draft.Id,Version(page.DraftEtag),"takeover",null,"Review supplied renewal experience",Guid.NewGuid().ToString(),Guid.NewGuid());
            using var takeoverBody=JsonDocument.Parse(takeover.Body);var reviewFence=takeoverBody.RootElement.GetProperty("lease").GetProperty("leaseToken").GetGuid();
            var denied=await Assert.ThrowsAsync<QuoteOperationException>(()=>referrals.DecideAsync(f.Underwriter,draft.Id,cycle.Id,
                Version(takeover.Etag!),reviewFence,[new(information.Id,Version(information.Etag),"approve","Attempt to bypass missing experience",[])],Guid.NewGuid().ToString(),Guid.NewGuid()));
            Assert.Equal("renewal-reviewed-experience-required",denied.Code);
            var upload=await renewals.UploadExperienceAsync(f.Underwriter,draft.Id,Version(takeover.Etag!),reviewFence,"claims.txt","text/plain",
                System.Text.Encoding.UTF8.GetBytes("Fictional claims statement: GBP600 paid / GBP1000 earned."),Guid.NewGuid().ToString(),Guid.NewGuid());
            var supplied=await renewals.SaveExperienceAsync(f.Underwriter,draft.Id,Version(upload.Etag!),reviewFence,
                new(new(2025,9,16),new(2026,9,16),1,600m,0m,1000m,"agency","Fictional supplied renewal statement",upload.ResourceId),Guid.NewGuid().ToString(),Guid.NewGuid());
            var reviewed=await renewals.ReviewExperienceAsync(f.Underwriter,draft.Id,supplied.ResourceId,Version(supplied.Etag!),reviewFence,"accepted",
                "Verified the supplied fictional claims statement",Guid.NewGuid().ToString(),Guid.NewGuid());
            Assert.Null((await new ServicingRatingReadModel(f.Factory,f.Clock).ReadAsync(f.Servicing,draft.Id)).Current);
            var rerated=await ratings.RateAsync(f.Underwriter,draft.Id,draft.CurrentRevisionId.Value,Version(reviewed.Etag!),reviewFence,
                "Rate renewal with reviewed experience",Guid.NewGuid().ToString(),Guid.NewGuid());
            var next=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==rerated.ResourceId);
            Assert.Equal(supplied.ResourceId,next.RenewalExperienceVersionId);Assert.Equal(reviewed.ResourceId,next.RenewalExperienceReviewId);
            var nextLease=Assert.IsType<JobLease>(await jobs.ClaimWorkAsync("servicing-rating",next.WorkId));
            var loaded=await worker.ExecuteProviderAsync(nextLease);Assert.True(await worker.ApplyAsync(nextLease,loaded));
            Assert.Equal(decimal.Round(calculated.Rating.Premium*1.08m,2,MidpointRounding.AwayFromZero),loaded.Rating!.Premium);
            var nextPage=await referrals.ReadReferralsAsync(f.Underwriter,draft.Id);
            var lossRatio=Assert.Single(nextPage.Items,x=>x.RuleCode=="UW-31");
            Assert.DoesNotContain(nextPage.Items,x=>x.RuleCode=="UW-31-information");
            var seniorOnly=await Assert.ThrowsAsync<QuoteOperationException>(()=>referrals.DecideAsync(f.Underwriter,draft.Id,next.Id,
                Version(nextPage.DraftEtag),reviewFence,[new(lossRatio.Id,Version(lossRatio.Etag),"approve","Attempt ordinary underwriter approval",[])],Guid.NewGuid().ToString(),Guid.NewGuid()));
            Assert.Equal("renewal-senior-decision-required",seniorOnly.Code);
            var seniorUser=await (from user in db.Set<StaffUser>() join membership in db.Set<UserRole>() on user.Id equals membership.UserId
                join role in db.Set<Role>() on membership.RoleId equals role.Id where role.Code=="senior-underwriter" && user.State=="active" select user).FirstAsync();
            var senior=new ActorContext(seniorUser.Id,seniorUser.TeamId,null,new HashSet<string>{"senior-underwriter"});
            var seniorLease=await drafts.LeaseAsync(senior,draft.Id,Version(nextPage.DraftEtag),"takeover",null,"Senior review of renewal loss ratio",Guid.NewGuid().ToString(),Guid.NewGuid());
            using var seniorBody=JsonDocument.Parse(seniorLease.Body);var seniorFence=seniorBody.RootElement.GetProperty("lease").GetProperty("leaseToken").GetGuid();
            var approvalKey=Guid.NewGuid().ToString();var decisions=new ReferralDecisionInput[]{new(lossRatio.Id,Version(lossRatio.Etag),"approve","Senior approval of reviewed renewal experience",[])};
            await referrals.DecideAsync(senior,draft.Id,next.Id,Version(seniorLease.Etag!),seniorFence,decisions,approvalKey,Guid.NewGuid());
            Assert.True(Assert.Single((await referrals.ReadReferralsAsync(senior,draft.Id)).Items,x=>x.RuleCode=="UW-31").DecisionReady);
            var adjustmentTemplate=await db.Set<TemplateVersion>().Where(x=>x.ProductId==next.ProductId && x.Kind=="servicing-terms" && x.State=="published").Select(x=>x.Id).FirstAsync();
            var approvedDraft=await drafts.ReadEditorAsync(senior,draft.Id);
            var approvedRating=await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x=>x.CycleId==next.Id);
            Assert.Equal("renewal-invitation-required",(await Assert.ThrowsAsync<QuoteOperationException>(()=>new ServicingTermsService(f.Factory,f.Clock).PrepareAsync(senior,draft.Id,next.Id,
                approvedRating.Id,adjustmentTemplate,
                Version(approvedDraft.Etag!),seniorFence,Guid.NewGuid().ToString(),Guid.NewGuid()))).Code);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason='Withdraw fictional renewal evidence authority' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
            Assert.False((await new ServicingRatingReadModel(f.Factory,f.Clock).ReadAsync(f.Servicing,draft.Id)).Current!.Applicable);
            Assert.Equal("renewal-experience-review-stale",(await Assert.ThrowsAsync<QuoteOperationException>(()=>referrals.DecideAsync(senior,draft.Id,next.Id,
                Version(seniorLease.Etag!),seniorFence,decisions,approvalKey,Guid.NewGuid()))).Code);
            var source=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            Assert.Equal(issued.ContentHash,source.ContentHash);Assert.Equal(issued.SnapshotJson,source.SnapshotJson);
        });
    }
}
