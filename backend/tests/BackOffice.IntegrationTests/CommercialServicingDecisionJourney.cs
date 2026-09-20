using System.Text;
using BackOffice.Application;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    private static async Task<(ServicingAcceptance Acceptance,string Etag)> VerifyCommercialServicingDecisions(BackOfficeDbContext db, IDbContextFactory<BackOfficeDbContext> factory,
        ActorContext actor, RatingClock clock, ServicingCycle cycle, Guid lease, string etag)
    {
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string Key()=>Guid.NewGuid().ToString();
        var evidence=new ServicingEvidenceService(factory,clock);var selected=new List<Guid>();
        foreach(var view in (await evidence.RequirementsAsync(actor,cycle.DraftId)).Requirements)
        {
            var purpose=view.Requirement;
            var uploaded=await evidence.UploadAsync(actor,cycle.DraftId,Version(etag),lease,"fictional-commercial-proof.txt","text/plain",
                Encoding.UTF8.GetBytes("Fictional commercial adjustment evidence: "+purpose.Code),Key(),Guid.NewGuid());
            var attached=await evidence.AttachAsync(actor,cycle.DraftId,cycle.Id,Version(uploaded.Etag!),lease,uploaded.ResourceId,purpose.Code,
                purpose.RiskItemId,purpose.InputFingerprint,"Attach exact fictional commercial proof",Key(),Guid.NewGuid());
            var association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
            var reviewed=await evidence.ReviewAsync(actor,cycle.DraftId,cycle.Id,association.Id,Version(attached.Etag!),lease,association.RowVersion,
                "accepted",purpose.InputFingerprint,"Review exact fictional commercial proof",Key(),Guid.NewGuid());
            etag=reviewed.Etag!;selected.Add(association.Id);
        }
        Assert.All((await evidence.RequirementsAsync(actor,cycle.DraftId)).Requirements,x=>Assert.True(x.Satisfied));
        var service=new ServicingReferralService(factory,clock);
        var referrals=await db.Set<ServicingReferral>().AsNoTracking().Where(x=>x.CycleId==cycle.Id).ToArrayAsync();
        var first=referrals.First(x=>x.RuleCode=="AU-05");
        var before=await service.CurrentAuthorityAsync(actor,cycle.DraftId,first.Id);
        Assert.NotEmpty(before.Items);Assert.All(before.Items,x=>Assert.False(x.ScheduleWithinAuthority));
        var capacity=new ServicingCapacityService(factory,clock);
        var scenario=await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope=="capacity-escalation/cc-approve-requested").OrderByDescending(x=>x.Version).FirstAsync();
        foreach(var referral in referrals.Where(x=>x.RuleCode is "AU-05" or "AU-06"))
        {
            var created=await capacity.CreateAsync(actor,cycle.DraftId,cycle.Id,referral.Id,Version(etag),referral.RowVersion,lease,
                "Escalate exact fictional commercial limit",Key(),Guid.NewGuid());
            var row=await db.Set<ServicingCapacityCase>().AsNoTracking().SingleAsync(x=>x.Id==created.ResourceId);
            var chosen=referral.RuleCode=="AU-05" ? await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope=="capacity-escalation/cc-conditional-proof").OrderByDescending(x=>x.Version).FirstAsync() : scenario;
            var sent=await capacity.SubmitAsync(actor,cycle.DraftId,cycle.Id,row.Id,Version(created.Etag!),row.RowVersion,lease,
                "Review the fictional dated location exposure","Request exact commercial authority extent",selected.Take(20).ToArray(),chosen.Id,Key(),Guid.NewGuid());
            var submission=await db.Set<ServicingCapacitySubmission>().AsNoTracking().SingleAsync(x=>x.CaseId==row.Id);
            var job=Assert.IsType<JobLease>(await new SqlJobLeases(factory,clock).ClaimWorkAsync(ServicingCapacityService.WorkKind,submission.WorkId));
            var worker=new ServicingCapacityWorker(factory,clock);var response=await worker.ExecuteProviderAsync(job);
            Assert.Equal(referral.RuleCode=="AU-05"?"approve-with-conditions":"approve",response.Outcome);Assert.True(await worker.ApplyAsync(job,response));
            Assert.Equal(referral.RuleCode=="AU-05"?"conditional":"approved",(await db.Set<ServicingCapacityCase>().AsNoTracking().SingleAsync(x=>x.Id==row.Id)).State);
            etag="\""+Convert.ToBase64String(await db.Set<ServicingDraft>().AsNoTracking().Where(x=>x.Id==cycle.DraftId).Select(x=>x.RowVersion).SingleAsync())+"\"";
        }
        foreach(var condition in await db.Set<ServicingCapacityCondition>().AsNoTracking().Where(x=>x.CycleId==cycle.Id).ToArrayAsync())
        {
            using var definition=System.Text.Json.JsonDocument.Parse(condition.DefinitionJson);
            var target=definition.RootElement.GetProperty("riskItemId").GetGuid();
            var proof=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.CycleId==cycle.Id && x.RequirementCode=="cc-location-proof" && x.RiskItemId==target);
            var resolved=await capacity.ResolveConditionAsync(actor,cycle.DraftId,cycle.Id,condition.Id,Version(etag),condition.RowVersion,lease,proof.Id,
                "satisfied","Resolve exact fictional commercial location proof",Key(),Guid.NewGuid());
            etag=resolved.Etag!;
        }
        Assert.Equal(1,await db.Set<ServicingCapacityConditionResolution>().CountAsync(x=>x.CycleId==cycle.Id));
        var after=await service.CurrentAuthorityAsync(actor,cycle.DraftId,first.Id);
        Assert.Contains(after.Items,x=>x.ScheduleWithinAuthority);
        referrals=await db.Set<ServicingReferral>().AsNoTracking().Where(x=>x.CycleId==cycle.Id).ToArrayAsync();
        var inputs=referrals.Select(x=>new ReferralDecisionInput(x.Id,x.RowVersion,"approve","Approve exact fictional commercial schedule",[],null)).ToArray();
        var decided=await service.DecideAsync(actor,cycle.DraftId,cycle.Id,Version(etag),lease,inputs,Key(),Guid.NewGuid());
        Assert.All(await db.Set<ServicingReferral>().AsNoTracking().Where(x=>x.CycleId==cycle.Id).ToArrayAsync(),x=>Assert.Equal("approved",x.State));
        return await VerifyCommercialServicingTerms(db,factory,actor,clock,cycle,lease,decided.Etag!);
    }
}
