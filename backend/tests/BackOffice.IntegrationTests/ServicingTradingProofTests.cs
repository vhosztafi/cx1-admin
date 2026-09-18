using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingTradingProof(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,string etag)
    {
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string Key()=>Guid.NewGuid().ToString();
        var drafts=new ServicingDraftService(f.Factory,f.Clock);
        var service=new ServicingReferralService(f.Factory,f.Clock);
        var evidence=new ServicingEvidenceService(f.Factory,f.Clock);
        var takeover=await drafts.LeaseAsync(f.Underwriter,cycle.DraftId,Version(etag),"takeover",null,"Review corrected business history",Key(),Guid.NewGuid());
        var fence=JsonSerializer.Deserialize<JsonElement>(takeover.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();
        var row=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.CycleId==cycle.Id && x.RuleCode=="UW-22");
        var decision=new ReferralDecisionInput(row.Id,row.RowVersion,"approve","Approve reviewed business history",[]);
        var current=Version(takeover.Etag!);
        async Task MissingProof()=>Assert.Equal("servicing-trading-history-review-required",
            (await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,current,fence,[decision],Key(),Guid.NewGuid()))).Code);
        await MissingProof();
        var required=Assert.Single((await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements,x=>x.Requirement.Code=="trading-history");
        Assert.False(required.Satisfied);
        var uploaded=await evidence.UploadAsync(f.Underwriter,cycle.DraftId,current,fence,"history.txt","text/plain",System.Text.Encoding.UTF8.GetBytes("Fictional corrected business history"),Key(),Guid.NewGuid());
        var attached=await evidence.AttachAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(uploaded.Etag!),fence,uploaded.ResourceId,"trading-history",null,required.Requirement.InputFingerprint,"Attach corrected business history",Key(),Guid.NewGuid());
        current=Version(attached.Etag!);await MissingProof();
        var association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        var rejected=await evidence.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,association.Id,current,fence,association.RowVersion,"rejected",required.Requirement.InputFingerprint,"Reject incomplete business history",Key(),Guid.NewGuid());
        current=Version(rejected.Etag!);await MissingProof();
        association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        var reviewed=await evidence.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,association.Id,current,fence,association.RowVersion,"accepted",required.Requirement.InputFingerprint,"Accept corrected business history",Key(),Guid.NewGuid());
        current=Version(reviewed.Etag!);var key=Key();
        var approved=await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,current,fence,[decision],key,Guid.NewGuid());
        Assert.True((await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,current,fence,[decision],key,Guid.NewGuid())).Replayed);
        Assert.True(Assert.Single((await service.ReadReferralsAsync(f.Underwriter,cycle.DraftId)).Items,x=>x.Id==row.Id).DecisionReady);
        association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        var withdrawn=await evidence.WithdrawAsync(f.Underwriter,cycle.DraftId,cycle.Id,association.Id,Version(approved.Etag!),fence,association.RowVersion,"Withdraw incorrect business history",Key(),Guid.NewGuid());
        var view=Assert.Single((await service.ReadReferralsAsync(f.Underwriter,cycle.DraftId)).Items,x=>x.Id==row.Id);
        Assert.Equal("approved",view.State);Assert.False(view.DecisionReady);
        Assert.Equal("servicing-trading-history-review-required",
            (await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,current,fence,[decision],key,Guid.NewGuid()))).Code);
        row=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==row.Id);
        decision=decision with {Version=row.RowVersion};current=Version(withdrawn.Etag!);await MissingProof();
        Assert.Single(await db.Set<ServicingReferralDecision>().Where(x=>x.ReferralId==row.Id).ToArrayAsync());
    }
}
