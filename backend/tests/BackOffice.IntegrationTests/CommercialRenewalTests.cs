using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Persistence;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlCommercialRenewalUsesOwnedExperienceAndSeparateFullTermRating()=>CommercialRenewalScenario(false);

    [Fact]
    public Task RealSqlCommercialRenewalEarlyIssuePreservesExpiringCoverAndStartsExposureAtInception()=>CommercialRenewalScenario(true);

    [Fact]
    public Task RealSqlCommercialRenewalRejectsExperienceFromPriorSavedRevision()=>CommercialRenewalScenario(false,true);

    [Fact]
    public Task RealSqlCommercialRenewalSelectsFinalScheduledExpiringRisk()=>CommercialServicingIssueScenario(true,verifyRenewal:true);

    [Fact]
    public Task RealSqlCommercialRenewalMissingExperienceRemainsUnresolved()=>CommercialRenewalScenario(false,missingExperience:true);

    [Fact]
    public Task RealSqlCommercialRenewalLapseReplaysWithoutCreatingTermOrExposure()=>CommercialRenewalScenario(false,lapseInstead:true);

    [Fact]
    public Task RealSqlCommercialRenewalConcurrentReadProjectionsRemainCurrent()=>CommercialRenewalScenario(false,profileReads:true);

    private Task CommercialRenewalScenario(bool issue,bool staleExperience=false,bool missingExperience=false,bool lapseInstead=false,bool profileReads=false,bool verifyDowngrade=false,bool verifyCancellation=false,
        Func<BackOfficeDbContext,IDbContextFactory<BackOfficeDbContext>,BackOffice.Application.ActorContext,ServicingCycle,ServicingTermsVersion,Task>? onAccepted=null)=>CommercialTermsScenario(async(db,cycle,acceptance,actorId,now)=>
    {
        var source=await CommercialIssueCommand(db,cycle,acceptance,actorId);
        await source.Service.IssueAsync(source.Actor,source.Quote.Id,source.Quote.RowVersion,source.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        await using(var tx=await db.Database.BeginTransactionAsync())
        {await RenewalPreparationSeed.SeedCommercialAsync(db);await RenewalLifecycleSeed.SeedAsync(db);await tx.CommitAsync();}
        var clock=new RatingClock();
        if(issue)
        {
            var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x=>x.Id==basis.TermId);
            clock.Current=term.StartsAt.AddDays(1);
            Assert.Equal("active",(await new PolicyReadService(source.Factory,clock).ReadAsync(source.Actor,basis.PolicyId))["coverageState"]);
        }
        var drafts=new ServicingDraftService(source.Factory,clock);var renewal=new RenewalPreparationService(source.Factory,clock);
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static JsonElement Body(string value)=>JsonSerializer.Deserialize<JsonElement>(value);
        static string Key()=>Guid.NewGuid().ToString();
        var preview=await renewal.PreviewAsync(source.Actor,basis.TermId);Assert.Equal(basis.Id,preview.BaseVersionId);Assert.True(preview.FairValueSatisfied);
        Assert.Equal(RenewalConfiguration.CommercialScope,(await db.Set<SettingVersion>().SingleAsync(x=>x.Id==preview.RuleSettingVersionId)).Scope);
        if(lapseInstead)
        {
            var lapseService=new RenewalLifecycleService(source.Factory,clock);var state=await lapseService.ReadAsync(source.Actor,basis.TermId);Assert.True(state.CanLapse);
            var key=Key();const string reason="Lapse the fictional commercial renewal without changing expiring cover";
            var lapsed=await lapseService.LapseAsync(source.Actor,basis.TermId,Version(state.Etag),reason,key,Guid.NewGuid());
            Assert.Equal(lapsed.Body,(await lapseService.LapseAsync(source.Actor,basis.TermId,Version(state.Etag),reason,key,Guid.NewGuid())).Body);
            Assert.Equal("lapsed",(await lapseService.ReadAsync(source.Actor,basis.TermId)).State);
            Assert.Equal("renewal-already-lapsed",(await Assert.ThrowsAsync<QuoteOperationException>(()=>renewal.PreviewAsync(source.Actor,basis.TermId))).Code);
            await VerifyCommercialRenewalLapseDowngrade(db);Assert.Equal(1,await db.Set<RenewalLapseEvent>().CountAsync());Assert.Equal(1,await db.Set<PolicyTerm>().CountAsync());Assert.Equal(1,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(1,await db.Set<CommercialExposureVersion>().CountAsync());return;
        }
        var listed=await drafts.ListAsync(source.Actor,basis.TermId);var intent=preview.TermIntent;
        var created=await drafts.CreateAsync(source.Actor,basis.TermId,Version(listed.Etag),new("renewal",basis.Id,
            JsonSerializer.SerializeToElement(new{localDate=intent.GetProperty("localStartDate").GetString(),localTime=intent.GetProperty("localStartTime").GetString(),timeZone="Europe/London",utcOffsetMinutes=intent.GetProperty("utcOffsetMinutes").GetInt32()}),
            "Fictional commercial renewal preparation"),Key(),Guid.NewGuid());
        var owned=await drafts.LeaseAsync(source.Actor,created.ResourceId,Version(created.Etag!),"acquire",null,null,Key(),Guid.NewGuid());
        var fence=Body(owned.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();
        var prepared=await renewal.PrepareAsync(source.Actor,created.ResourceId,Version(owned.Etag!),fence,12,null,Key(),Guid.NewGuid());
        if(missingExperience)
        {
            var draft=await db.Set<ServicingDraft>().AsNoTracking().SingleAsync();
            var requestedMissing=await new ServicingRatingService(source.Factory,clock).RateAsync(source.Actor,draft.Id,draft.CurrentRevisionId!.Value,Version(prepared.Etag!),fence,"Rate commercial renewal without invented experience",Key(),Guid.NewGuid());
            var missingCycle=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==requestedMissing.ResourceId);
            var missingInput=ServicingRatingInput.Read(missingCycle.InputJson,missingCycle.InputHash);
            Assert.Null(missingInput.Renewal!.Experience);Assert.False(missingInput.Renewal.EvidenceAccepted);
            var missingWorker=new ServicingRatingWorker(source.Factory,clock);
            var job=Assert.IsType<JobLease>(await new SqlJobLeases(source.Factory,clock).ClaimWorkAsync(ServicingRatingService.WorkKind,missingCycle.WorkId));
            Assert.True(await missingWorker.ApplyAsync(job,await missingWorker.ExecuteProviderAsync(job)));
            var referrals=new ServicingReferralService(source.Factory,clock);var page=await referrals.ReadReferralsAsync(source.Actor,draft.Id);
            var information=Assert.Single(page.Items,x=>x.RuleCode=="UW-31-information");Assert.False(information.DecisionReady);
            Assert.Equal("renewal-reviewed-experience-required",(await Assert.ThrowsAsync<QuoteOperationException>(()=>referrals.DecideAsync(source.Actor,draft.Id,missingCycle.Id,Version(page.DraftEtag),fence,
                [new(information.Id,Version(information.Etag),"approve","Reject bypass of missing commercial experience",[])],Key(),Guid.NewGuid()))).Code);
            Assert.Empty(await db.Set<RenewalExperienceVersion>().ToArrayAsync());Assert.Empty(await db.Set<ServicingTermsVersion>().ToArrayAsync());return;
        }
        var uploaded=await renewal.UploadExperienceAsync(source.Actor,created.ResourceId,Version(prepared.Etag!),fence,"fictional-commercial-experience.txt","text/plain",Encoding.UTF8.GetBytes("Fictional whole commercial risk zero-loss experience."),Key(),Guid.NewGuid());
        var experience=await renewal.ReadExperienceAsync(source.Actor,created.ResourceId);var subjects=Assert.IsType<CommercialRenewalSubjects>(experience.CurrentCommercialSubjects);
        var facts=new RenewalExperienceFacts(new(2025,1,1),new(2026,1,1),0,0,0,1000,"agency","Fictional commercial experience",uploaded.ResourceId,subjects);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>renewal.SaveExperienceAsync(source.Actor,created.ResourceId,Version(experience.Etag),fence,
            facts with {CommercialSubjects=subjects with {PropertyLocationIds=[Guid.NewGuid()]}},Key(),Guid.NewGuid()))).Status);
        Assert.Equal(0,await db.Set<RenewalExperienceVersion>().CountAsync());
        var saved=await renewal.SaveExperienceAsync(source.Actor,created.ResourceId,Version(experience.Etag),fence,facts,Key(),Guid.NewGuid());
        var reviewed=await renewal.ReviewExperienceAsync(source.Actor,created.ResourceId,saved.ResourceId,Version(saved.Etag!),fence,"accepted","Reviewed fictional experience for this exact commercial risk",Key(),Guid.NewGuid());
        var current=await db.Set<ServicingDraft>().AsNoTracking().SingleAsync();var ratings=new ServicingRatingService(source.Factory,clock);
        if(staleExperience)
        {
            var proposal=System.Text.Json.Nodes.JsonNode.Parse((await db.Set<ServicingRevision>().AsNoTracking().SingleAsync(x=>x.Id==current.CurrentRevisionId)).ProposalJson)!;
            proposal["reason"]="Changed saved commercial renewal revision requires fresh experience confirmation";
            var changed=await drafts.SaveAsync(source.Actor,created.ResourceId,Version(reviewed.Etag!),fence,proposal.ToJsonString(),Key(),Guid.NewGuid());
            var changedDraft=await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x=>x.Id==created.ResourceId);
            Assert.NotEqual(subjects.RevisionId,changedDraft.CurrentRevisionId);
            Assert.Equal("commercial-renewal-experience-stale",(await Assert.ThrowsAsync<QuoteOperationException>(()=>renewal.ReviewExperienceAsync(source.Actor,created.ResourceId,saved.ResourceId,Version(changed.Etag!),fence,"accepted","Reject review of prior commercial revision",Key(),Guid.NewGuid()))).Code);
            Assert.Equal("commercial-renewal-experience-stale",(await Assert.ThrowsAsync<QuoteOperationException>(()=>renewal.SaveExperienceAsync(source.Actor,created.ResourceId,Version(changed.Etag!),fence,facts,Key(),Guid.NewGuid()))).Code);
            await Assert.ThrowsAsync<QuoteOperationException>(()=>ratings.RateAsync(source.Actor,created.ResourceId,changedDraft.CurrentRevisionId!.Value,Version(changed.Etag!),fence,"Reject obsolete reviewed commercial experience",Key(),Guid.NewGuid()));
            Assert.False(await db.Set<ServicingCycle>().AnyAsync());Assert.Equal(1,await db.Set<RenewalExperienceVersion>().CountAsync());Assert.Equal(1,await db.Set<RenewalExperienceReview>().CountAsync());
            Assert.Equal(1,await db.Set<PolicyVersion>().CountAsync());return;
        }
        var requested=await ratings.RateAsync(source.Actor,created.ResourceId,current.CurrentRevisionId!.Value,Version(reviewed.Etag!),fence,"Rate the fictional full commercial renewal term",Key(),Guid.NewGuid());
        Assert.Equal(202,requested.Status);var stored=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync();
        var input=ServicingRatingInput.Read(stored.InputJson,stored.InputHash);Assert.Equal("commercial-servicing-rating-input-2",input.Format);Assert.Equal(45m,input.Fee);
        Assert.True(CommercialRenewalExperienceRules.Matches(subjects,input.Renewal!.Experience!.CommercialSubjects!));
        var worker=new ServicingRatingWorker(source.Factory,clock);var jobs=new SqlJobLeases(source.Factory,clock);
        var lease=Assert.IsType<JobLease>(await jobs.ClaimWorkAsync(ServicingRatingService.WorkKind,stored.WorkId));
        var outcome=await worker.ExecuteProviderAsync(lease);Assert.True(await worker.ApplyAsync(lease,outcome));
        var read=await new ServicingRatingReadModel(source.Factory,clock).ReadAsync(source.Actor,created.ResourceId);
        Assert.True(Assert.Single(read.Items).Applicable);Assert.Equal(45m,outcome.Rating!.Fee);Assert.Equal(outcome.Rating.Slices[0].AnnualPremium,outcome.Rating.Premium);
        Assert.Equal(1,await db.Set<PolicyTerm>().CountAsync());Assert.Equal(1,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(1,await db.Set<CommercialExposureVersion>().CountAsync());
        if(verifyDowngrade){await VerifyCommercialRenewalDowngrade(db);return;}
        if(profileReads)
        {
            await VerifyCommercialRenewalConcurrentReads(source.Factory,source.Actor,clock,created.ResourceId);
            return;
        }
        if(!issue)return;
        var accepted=await VerifyCommercialServicingDecisions(db,source.Factory,source.Actor,clock,stored,fence,read.DraftEtag);
        if(onAccepted is not null){await onAccepted(db,source.Factory,source.Actor,stored,await db.Set<ServicingTermsVersion>().AsNoTracking().SingleAsync(x=>x.Id==accepted.Acceptance.TermsVersionId));return;}
        var issuer=new ServicingIssueService(source.Factory,clock);
        var command=new ServicingIssueInput(stored.Id,accepted.Acceptance.RatingId,accepted.Acceptance.TermsVersionId,accepted.Acceptance.Id,
            accepted.Acceptance.TermsHash,accepted.Acceptance.AssuranceHash,"Issue fictional commercial renewal with inception exposure");
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>issuer.IssueAsync(source.Actor,created.ResourceId,new byte[8],fence,command,Key(),Guid.NewGuid()))).Status);
        await Assert.ThrowsAsync<QuoteOperationException>(()=>issuer.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),Guid.NewGuid(),command,Key(),Guid.NewGuid()));
        await Assert.ThrowsAsync<QuoteOperationException>(()=>issuer.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,command with{TermsHash=new string('f',64)},Key(),Guid.NewGuid()));
        var lifecycle=new RenewalLifecycleService(source.Factory,clock);var timeline=await lifecycle.ReadAsync(source.Actor,basis.TermId);
        Assert.Equal("accepted",timeline.State);Assert.False(timeline.CanLapse);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>lifecycle.LapseAsync(source.Actor,basis.TermId,Version(timeline.Etag),"Do not lapse an accepted commercial renewal",Key(),Guid.NewGuid()))).Status);
        var originalNow=clock.Current;
        try
        {
            clock.Current=timeline.Timeline.ExpiringEnd.AddTicks(1);
            Assert.Equal("renewal-late-issue-unsupported",(await Assert.ThrowsAsync<QuoteOperationException>(()=>issuer.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,command,Key(),Guid.NewGuid()))).Code);
            clock.Current=timeline.Timeline.AutoLapseAt;Assert.Null(await lifecycle.LapseDueAsync(basis.TermId));
        }
        finally{clock.Current=originalNow;}
        Assert.Equal(1,await db.Set<PolicyTerm>().CountAsync());Assert.Equal(1,await db.Set<CommercialExposureVersion>().CountAsync());
        var issueKey=Key();var issued=await issuer.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,command,issueKey,Guid.NewGuid());
        Assert.Equal(201,issued.Status);
        Assert.Equal(issued.Body,(await issuer.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,command,issueKey,Guid.NewGuid())).Body);
        Assert.Equal("servicing-already-issued",(await Assert.ThrowsAsync<QuoteOperationException>(()=>issuer.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,command,Key(),Guid.NewGuid()))).Code);
        var receipt=Body(issued.Body);var nextId=receipt.GetProperty("termId").GetGuid();var nextVersion=receipt.GetProperty("versionId").GetGuid();
        var expiring=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x=>x.Id==basis.TermId);
        var next=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x=>x.Id==nextId);
        Assert.NotEqual(basis.TermId,nextId);Assert.Equal(expiring.EndsAt,next.StartsAt);Assert.Equal(expiring.Number+1,next.Number);
        Assert.Equal(2,await db.Set<PolicyTerm>().CountAsync());Assert.Equal(2,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(2,await db.Set<CommercialExposureVersion>().CountAsync());
        Assert.Equal("renewal-term-overlap",(await Assert.ThrowsAsync<QuoteOperationException>(()=>renewal.PreviewAsync(source.Actor,basis.TermId))).Code);
        Assert.Equal(2,await db.Set<Journal>().CountAsync());Assert.Empty(receipt.GetProperty("midIntentIds").EnumerateArray());
        var policies=new PolicyReadService(source.Factory,clock);var exposure=new CommercialExposureReadModel(source.Factory,clock);
        Assert.Equal(basis.Id,(await policies.ReadAsync(source.Actor,basis.PolicyId))["versionId"]);
        Assert.Equal("active",(await policies.ReadAsync(source.Actor,basis.PolicyId))["coverageState"]);
        var currentExposure=JsonSerializer.SerializeToElement(await exposure.ReadAsync(source.Actor,"policies",basis.PolicyId,clock.Current));
        Assert.Equal(basis.Id,currentExposure.GetProperty("source").GetProperty("id").GetGuid());
        Assert.Equal(nextVersion,(await policies.ReadAtAsync(source.Actor,basis.PolicyId,next.StartsAt,clock.GetUtcNow()))["versionId"]);
        foreach(var point in new[]{(At:next.StartsAt.AddTicks(-1),Expected:basis.Id),(At:next.StartsAt,Expected:nextVersion)})
        {
            var observed=JsonSerializer.SerializeToElement(await exposure.ReadAsync(source.Actor,"policies",basis.PolicyId,point.At));
            Assert.Equal(point.Expected,observed.GetProperty("source").GetProperty("id").GetGuid());
        }
        Assert.Equal(basis.SnapshotJson,(await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==basis.Id)).SnapshotJson);
        var decision=await db.Set<ServicingIssueDecision>().AsNoTracking().SingleAsync();
        if(verifyCancellation)
        {
            await using(var tx=await db.Database.BeginTransactionAsync()){await CommercialUnderwritingCancellationSeed.SeedAsync(db);await tx.CommitAsync();}
            var list=await drafts.ListAsync(source.Actor,basis.TermId);
            var cancel=await drafts.CreateAsync(source.Actor,basis.TermId,Version(list.Etag),new("cancellation",basis.Id,
                JsonSerializer.SerializeToElement(new{localDate="2026-10-15",localTime="00:00",timeZone="Europe/London"}),"Do not cancel across an issued commercial renewal"),Key(),Guid.NewGuid());
            var cancelLease=await drafts.LeaseAsync(source.Actor,cancel.ResourceId,Version(cancel.Etag!),"acquire",null,null,Key(),Guid.NewGuid());
            var cancelBody=System.Text.Json.Nodes.JsonNode.Parse(cancelLease.Body)!;
            cancelBody["proposal"]!["cancellationReasonCode"]="insured-request";
            await drafts.SaveAsync(source.Actor,cancel.ResourceId,Version(cancelLease.Etag!),cancelBody["lease"]!["leaseToken"]!.GetValue<Guid>(),cancelBody["proposal"]!.ToJsonString(),Key(),Guid.NewGuid());
            var cancellation=new CancellationReviewService(source.Factory,clock);var view=await cancellation.ReadAsync(source.Actor,cancel.ResourceId);
            Assert.Contains("later-term-issued",view.Blockers);Assert.False(view.CanApprove);
            Assert.Equal(2,await db.Set<PolicyTerm>().CountAsync());Assert.Equal(2,await db.Set<CommercialExposureVersion>().CountAsync());Assert.Empty(await db.Set<CancellationIssueDecision>().ToArrayAsync());
            return;
        }
        var actualGrant=await db.Set<UserAuthorityGrant>().AsNoTracking().SingleAsync(x=>x.Id==decision.GrantId);
        try
        {
            clock.Current=actualGrant.EffectiveTo;
            Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>issuer.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,command,issueKey,Guid.NewGuid()))).Status);
        }
        finally{clock.Current=originalNow;}
        var admin=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={originalNow},RevokedBy={admin.Id},RevocationReason=N'Owned commercial renewal replay authorization test' WHERE Id={actualGrant.Id}");
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>issuer.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,command,issueKey,Guid.NewGuid()))).Status);
        Assert.Equal(2,await db.Set<PolicyTerm>().CountAsync());Assert.Equal(2,await db.Set<PolicyVersion>().CountAsync());
        Assert.Equal(2,await db.Set<CommercialExposureVersion>().CountAsync());Assert.Equal(2,await db.Set<Journal>().CountAsync());
    },stopAfterAccepted:true);

    [Fact]
    public Task RealSqlCommercialRenewalConfigurationIsIndependentMissingOnlyAndPreservesPriorVersions()=>WithDatabase(async(db,password)=>
    {
        async Task Seed()=>await DemoDatabase.SeedAsync(db,password,includeQuoteCapture:true,includeUnderwriting:true,
            includeRenewalLifecycle:true,includeCommercialCapture:true,includeCommercialUnderwriting:true);
        await Seed();
        var commercial=await db.Set<Product>().SingleAsync(x=>x.Code=="commercial-combined");
        var motor=await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Scope==RenewalConfiguration.Scope);
        var settings=await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope==RenewalConfiguration.CommercialScope).ToArrayAsync();
        var configured=Assert.Single(settings);Assert.Equal(45m,RenewalConfiguration.Parse(configured.Values,RenewalConfiguration.CommercialScope)!.RenewalFee);
        Assert.Equal(35m,RenewalConfiguration.Parse(motor.Values)!.RenewalFee);
        var template=Assert.Single(await db.Set<TemplateVersion>().AsNoTracking().Where(x=>x.ProductId==commercial.Id&&x.Kind=="renewal-invitation").ToArrayAsync());
        var assessment=Assert.Single(await db.Set<FairValueAssessmentVersion>().AsNoTracking().Where(x=>x.ProductId==commercial.Id).ToArrayAsync());
        Assert.True(await db.Set<ProductEvidenceFileVersion>().AnyAsync(x=>x.Id==assessment.EvidenceFileVersionId&&x.ProductId==commercial.Id));
        await Seed();
        await VerifyUnusedCommercialRenewalMigration(db);
        Assert.Equal(configured.Id,Assert.Single(await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope==RenewalConfiguration.CommercialScope).ToArrayAsync()).Id);
        Assert.Equal(motor.Values,(await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Id==motor.Id)).Values);
        Assert.Equal(template.Id,Assert.Single(await db.Set<TemplateVersion>().AsNoTracking().Where(x=>x.ProductId==commercial.Id&&x.Kind=="renewal-invitation").ToArrayAsync()).Id);
        Assert.Equal(assessment.Id,Assert.Single(await db.Set<FairValueAssessmentVersion>().AsNoTracking().Where(x=>x.ProductId==commercial.Id).ToArrayAsync()).Id);
    });
}
