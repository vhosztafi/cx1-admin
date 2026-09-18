using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingWarrantyProof(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,string etag,string password)
    {
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string Key()=>Guid.NewGuid().ToString();
        var service=new ServicingReferralService(f.Factory,f.Clock);var evidence=new ServicingEvidenceService(f.Factory,f.Clock);
        var drafts=new ServicingDraftService(f.Factory,f.Clock);
        // Reapply the additive migration over an existing issued/rated graph.
        await db.GetService<IMigrator>().MigrateAsync("20260917220333_ServicingConditionResolutions");
        await db.Database.MigrateAsync();db.ChangeTracker.Clear();
        using var input=JsonDocument.Parse(cycle.InputJson);var slice=input.RootElement.GetProperty("slices")[0];
        var target=slice.GetProperty("input").GetProperty("drivers")[0].GetProperty("id").GetGuid();
        var sequence=await db.Set<ServicingReferral>().Where(x=>x.CycleId==cycle.Id).Select(x=>(int?)x.Sequence).MaxAsync()??0;
        var referral=new ServicingReferral {DraftId=cycle.DraftId,CycleId=cycle.Id,RevisionId=cycle.RevisionId,RatingId=cycle.CurrentRatingId!.Value,
            Sequence=sequence+1,RuleCode="driver-age",Dimension="driver-age",RiskItemId=target,TargetKey=target,Reason="Synthetic warranty boundary referral",
            RequiredAuthorityJson=JsonSerializer.Serialize(new {triggers=new[]{new {effectiveAt=slice.GetProperty("effectiveAt").GetDateTimeOffset(),source="authority",requirement=new {ruleCode="driver-age",dimension="driver-age",targetId=target}}}}),
            CreatedBy=f.Servicing.UserId,CreatedAt=f.Clock.GetUtcNow(),UpdatedAt=f.Clock.GetUtcNow()};
        db.Add(referral);await db.SaveChangesAsync();
        var takeover=await drafts.LeaseAsync(f.Underwriter,cycle.DraftId,Version(etag),"takeover",null,"Review fictional warranty conditions",Key(),Guid.NewGuid());
        var fence=JsonSerializer.Deserialize<JsonElement>(takeover.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();
        Assert.DoesNotContain((await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements,x=>x.Requirement.Code=="warranty-acknowledgement");
        var earlierUpload=await evidence.UploadAsync(f.Underwriter,cycle.DraftId,Version(takeover.Etag!),fence,"early.txt","text/plain",System.Text.Encoding.UTF8.GetBytes("Fictional earlier warranty file"),Key(),Guid.NewGuid());
        db.Add(new ServicingEvidenceAssociation {DraftId=cycle.DraftId,CycleId=cycle.Id,RevisionId=cycle.RevisionId,RatingId=cycle.CurrentRatingId.Value,
            FileId=earlierUpload.ResourceId,RequirementCode="warranty-acknowledgement",InputFingerprint=new string('a',64),Reason="No current warranty must be rejected",
            CreatedBy=f.Underwriter.UserId,CreatedAt=f.Clock.GetUtcNow(),UpdatedAt=f.Clock.GetUtcNow()});
        var storageError=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
        Assert.Contains("current owned conditional warranty",storageError.InnerException!.Message);db.ChangeTracker.Clear();
        var definition=JsonSerializer.SerializeToElement(new {code="named-drivers-only",driverIds=new[]{target},wordingVersion="1"});
        var decision=new ReferralDecisionInput(referral.Id,referral.RowVersion,"approve-with-conditions","Restrict fictional driving to named drivers",[definition]);
        var conditioned=await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(earlierUpload.Etag!),fence,[decision],Key(),Guid.NewGuid());
        var condition=await db.Set<ServicingCondition>().AsNoTracking().SingleAsync(x=>x.ReferralId==referral.Id);
        var referralView=Assert.Single((await service.ReadReferralsAsync(f.Underwriter,cycle.DraftId)).Items,x=>x.Id==referral.Id);
        Assert.Equal("approve-with-conditions",referralView.Decision!.Outcome);
        Assert.Equal("driver-age",referralView.RequiredAuthority.GetProperty("triggers")[0].GetProperty("requirement").GetProperty("ruleCode").GetString());
        var wording=Assert.Single(referralView.Conditions);Assert.Equal(2,wording.Clauses.Count);
        Assert.Equal("named-drivers-only",wording.Definition.GetProperty("code").GetString());
        Assert.All(wording.Clauses,x=>{Assert.Contains("Fictional Correction",x.Wording);Assert.Contains(target,x.TargetIds);});
        var required=Assert.Single((await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements,x=>x.Requirement.Code=="warranty-acknowledgement").Requirement;
        Assert.Equal(2,required.EffectiveDates.Count);Assert.Null(required.RiskItemId);
        var uploaded=await evidence.UploadAsync(f.Underwriter,cycle.DraftId,Version(conditioned.Etag!),fence,"acknowledgement.txt","text/plain",System.Text.Encoding.UTF8.GetBytes("Fictional acknowledgement of all dated warranties"),Key(),Guid.NewGuid());
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>evidence.AttachAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(uploaded.Etag!),fence,uploaded.ResourceId,required.Code,target,required.InputFingerprint,"Wrong item acknowledgement",Key(),Guid.NewGuid()))).Status);
        var attached=await evidence.AttachAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(uploaded.Etag!),fence,uploaded.ResourceId,required.Code,null,required.InputFingerprint,"Attach complete warranty acknowledgement",Key(),Guid.NewGuid());
        var association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,condition.Id,Version(attached.Etag!),fence,condition.RowVersion,association.Id,"satisfied","Unreviewed acknowledgement must fail",Key(),Guid.NewGuid()))).Status);
        var reviewed=await evidence.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,association.Id,Version(attached.Etag!),fence,association.RowVersion,"accepted",required.InputFingerprint,"Accept complete warranty acknowledgement",Key(),Guid.NewGuid());
        var resolved=await service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,condition.Id,Version(reviewed.Etag!),fence,condition.RowVersion,association.Id,"satisfied","Resolve acknowledged warranty conditions",Key(),Guid.NewGuid());
        Assert.True(await service.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));
        Assert.True(Assert.Single((await service.ReadReferralsAsync(f.Underwriter,cycle.DraftId)).Items,x=>x.Id==referral.Id).DecisionReady);
        association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        var withdrawn=await evidence.WithdrawAsync(f.Underwriter,cycle.DraftId,cycle.Id,association.Id,Version(resolved.Etag!),fence,association.RowVersion,"Withdraw disputed warranty acknowledgement",Key(),Guid.NewGuid());
        Assert.False(await service.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));
        Assert.False(Assert.Single((await service.ReadReferralsAsync(f.Underwriter,cycle.DraftId)).Items,x=>x.Id==referral.Id).DecisionReady);
        var files=await evidence.FilesAsync(f.Underwriter,cycle.DraftId,pageSize:1);
        Assert.Single(files.Items);Assert.NotNull(files.NextBeforeId);
        var filesNext=await evidence.FilesAsync(f.Underwriter,cycle.DraftId,files.NextBeforeId,1);
        Assert.Single(filesNext.Items);Assert.Null(filesNext.NextBeforeId);Assert.NotEqual(files.Items[0].Id,filesNext.Items[0].Id);
        var associations=await evidence.AssociationsAsync(f.Underwriter,cycle.DraftId,cycle.Id);
        var recorded=Assert.Single(associations.Items);Assert.Equal(association.Id,recorded.Id);Assert.True(recorded.Withdrawn);Assert.Equal("accepted",recorded.ReviewOutcome);
        var reviews=await evidence.ReviewsAsync(f.Underwriter,cycle.DraftId,association.Id,pageSize:1);
        Assert.Equal("review",Assert.Single(reviews.Items).Kind);Assert.Equal(1,reviews.NextAfterSequence);
        Assert.Equal("withdrawal",Assert.Single((await evidence.ReviewsAsync(f.Underwriter,cycle.DraftId,association.Id,reviews.NextAfterSequence!.Value,1)).Items).Kind);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>evidence.FilesAsync(f.Underwriter,cycle.DraftId,Guid.NewGuid()))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>evidence.AssociationsAsync(f.Underwriter,cycle.DraftId,Guid.NewGuid()))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>evidence.ReviewsAsync(f.Underwriter,cycle.DraftId,Guid.NewGuid()))).Status);
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>evidence.FilesAsync(f.Underwriter,cycle.DraftId,pageSize:0))).Status);
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>evidence.AssociationsAsync(f.Underwriter,cycle.DraftId,cycle.Id,pageSize:51))).Status);
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>evidence.ReviewsAsync(f.Underwriter,cycle.DraftId,association.Id,afterSequence:-1))).Status);
        referral=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==referral.Id);
        var replaced=await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(withdrawn.Etag!),fence,[decision with {Version=referral.RowVersion}],Key(),Guid.NewGuid());
        Assert.False(await service.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));
        var next=Assert.Single((await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements,x=>x.Requirement.Code==required.Code);
        Assert.False(next.Satisfied);Assert.NotEqual(required.InputFingerprint,next.Requirement.InputFingerprint);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>evidence.AttachAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(replaced.Etag!),fence,uploaded.ResourceId,required.Code,null,required.InputFingerprint,"Old acknowledgement cannot transfer",Key(),Guid.NewGuid()))).Status);
        var newCondition=await db.Set<ServicingCondition>().AsNoTracking().SingleAsync(x=>x.ReferralId==referral.Id && x.Id!=condition.Id);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,newCondition.Id,Version(replaced.Etag!),fence,newCondition.RowVersion,association.Id,"satisfied","Old reviewed proof cannot transfer",Key(),Guid.NewGuid()))).Status);
        Assert.False(Assert.Single((await service.ReadReferralsAsync(f.Underwriter,cycle.DraftId)).Items,x=>x.Id==referral.Id).DecisionReady);
        var history=await service.DecisionsAsync(f.Underwriter,cycle.DraftId,referral.Id,pageSize:1);
        Assert.Equal(cycle.Id,history.CycleId);Assert.Equal(1,history.NextAfterSequence);
        Assert.Equal("approve-with-conditions",Assert.Single(history.Items).Outcome);
        Assert.Equal("named-drivers-only",history.Items[0].Conditions[0].GetProperty("code").GetString());
        var later=await service.DecisionsAsync(f.Underwriter,cycle.DraftId,referral.Id,history.NextAfterSequence!.Value,1);
        Assert.Single(later.Items);Assert.Null(later.NextAfterSequence);Assert.NotEqual(history.Items[0].Id,later.Items[0].Id);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecisionsAsync(f.Underwriter,cycle.DraftId,Guid.NewGuid()))).Status);
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecisionsAsync(f.Underwriter,cycle.DraftId,referral.Id,pageSize:51))).Status);
        await VerifyServicingProofReadsHttp(db,f,password,cycle,association.Id,referral.Id,uploaded.ResourceId);
        var beforeExpiry=f.Clock.GetUtcNow();f.Clock.Current=beforeExpiry.AddDays(1);
        Assert.Equal(2,(await evidence.ReviewsAsync(f.Underwriter,cycle.DraftId,association.Id)).Items.Count);
        Assert.Equal(2,(await service.DecisionsAsync(f.Underwriter,cycle.DraftId,referral.Id)).Items.Count);
        f.Clock.Current=beforeExpiry;
        await VerifyServicingProofCommandsHttp(db,f,password,cycle,referral.Id);
    }
}
