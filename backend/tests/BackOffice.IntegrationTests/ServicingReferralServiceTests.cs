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
    private static async Task VerifyServicingReferralAuthority(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,string etag)
    {
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        var drafts=new ServicingDraftService(f.Factory,f.Clock);var service=new ServicingReferralService(f.Factory,f.Clock);
        var takeover=await drafts.LeaseAsync(f.Underwriter,cycle.DraftId,Version(etag),"takeover",null,"Review above-binder temporary cover",Guid.NewGuid().ToString(),Guid.NewGuid());
        var fence=JsonSerializer.Deserialize<JsonElement>(takeover.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();
        var row=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.CycleId==cycle.Id && x.RuleCode=="cover-tools-equipment");
        var decision=new ReferralDecisionInput(row.Id,row.RowVersion,"approve","Review fictional temporary cover",[]);
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(takeover.Etag!),fence,[decision],Guid.NewGuid().ToString(),Guid.NewGuid()))).Status);
        Assert.Empty(await db.Set<ServicingReferralDecision>().Where(x=>x.CycleId==cycle.Id).ToArrayAsync());
        var declined=await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(takeover.Etag!),fence,[decision with{Outcome="decline"}],Guid.NewGuid().ToString(),Guid.NewGuid());
        row=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==row.Id);Assert.Equal("declined",row.State);
        await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(declined.Etag!),fence,[decision with{Version=row.RowVersion,Outcome="reopen"}],Guid.NewGuid().ToString(),Guid.NewGuid());
        Assert.Equal("open",(await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==row.Id)).State);
    }

    private static async Task VerifyServicingReferralService(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,string etag)
    {
        var service=new ServicingReferralService(f.Factory,f.Clock);var drafts=new ServicingDraftService(f.Factory,f.Clock);
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string Key()=>Guid.NewGuid().ToString();
        using var input=JsonDocument.Parse(cycle.InputJson);var slice=input.RootElement.GetProperty("slices")[0];
        var target=slice.GetProperty("input").GetProperty("drivers")[0].GetProperty("id").GetGuid();
        var sequence=await db.Set<ServicingReferral>().Where(x=>x.CycleId==cycle.Id).Select(x=>(int?)x.Sequence).MaxAsync()??0;
        var rows=new[]{"driver-age","licence-years"}.Select(code=>new ServicingReferral{DraftId=cycle.DraftId,CycleId=cycle.Id,RevisionId=cycle.RevisionId,RatingId=cycle.CurrentRatingId!.Value,
            Sequence=++sequence,RuleCode=code,Dimension=code,RiskItemId=target,TargetKey=target,Reason="Synthetic decision boundary referral",
            RequiredAuthorityJson=JsonSerializer.Serialize(new{triggers=new[]{new{effectiveAt=slice.GetProperty("effectiveAt").GetDateTimeOffset(),source="authority",requirement=new{ruleCode=code,dimension=code,targetId=target}}}}),
            CreatedBy=f.Servicing.UserId,CreatedAt=f.Clock.GetUtcNow(),UpdatedAt=f.Clock.GetUtcNow()}).ToArray();
        db.AddRange(rows);await db.SaveChangesAsync();
        var takeover=await drafts.LeaseAsync(f.Underwriter,cycle.DraftId,Version(etag),"takeover",null,"Review fictional servicing referrals",Key(),Guid.NewGuid());
        var fence=JsonSerializer.Deserialize<JsonElement>(takeover.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();var current=Version(takeover.Etag!);
        var selected=rows.Select(x=>new ReferralDecisionInput(x.Id,x.RowVersion,"approve","Approve fictional servicing risk",[])).ToArray();
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,current,Guid.NewGuid(),selected,Key(),Guid.NewGuid()))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Servicing,cycle.DraftId,cycle.Id,current,fence,selected,Key(),Guid.NewGuid()))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,current,fence,[selected[0],selected[1] with{ReferralId=Guid.NewGuid()}],Key(),Guid.NewGuid()))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,current,fence,[selected[0],selected[1] with{Version=new byte[8]}],Key(),Guid.NewGuid()))).Status);
        Assert.Empty(await db.Set<ServicingReferralDecision>().Where(x=>x.CycleId==cycle.Id).ToArrayAsync());
        var key=Key();var result=await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,current,fence,selected,key,Guid.NewGuid());
        Assert.Equal(result.ResourceId,(await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,current,fence,selected,key,Guid.NewGuid())).ResourceId);
        Assert.Equal(2,await db.Set<ServicingReferralDecision>().CountAsync(x=>x.CycleId==cycle.Id));
        Assert.All(await db.Set<ServicingReferral>().AsNoTracking().Where(x=>rows.Select(r=>r.Id).Contains(x.Id)).ToArrayAsync(),x=>Assert.Equal("approved",x.State));
        var fresh=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==rows[0].Id);
        var conditional=new ReferralDecisionInput(fresh.Id,fresh.RowVersion,"approve-with-conditions","Review additional trading proof",[JsonSerializer.Deserialize<JsonElement>("{ \"code\" : \"provide-trading-history\" }")]);
        foreach(var invalid in new[]{conditional with{Conditions=null!},conditional with{Conditions=[default]},conditional with{Conditions=[JsonSerializer.SerializeToElement("invalid")]},conditional with{Version=null!},conditional with{Reason="short"},conditional with{Outcome="query",Question="short"}})
            Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(result.Etag!),fence,[invalid],Key(),Guid.NewGuid()))).Status);
        var oversized=conditional with{Conditions=[JsonSerializer.SerializeToElement(new{code="provide-trading-history",invalid=new string('x',65536)})]};
        Assert.Equal("servicing-condition-payload-too-large",(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(result.Etag!),fence,[oversized],Key(),Guid.NewGuid()))).Code);
        var conditioned=await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(result.Etag!),fence,[conditional],Key(),Guid.NewGuid());
        var condition=await db.Set<ServicingCondition>().AsNoTracking().SingleAsync(x=>x.ReferralId==fresh.Id);
        Assert.Equal("provide-trading-history",condition.Code);Assert.Equal(2,JsonSerializer.Deserialize<DateTimeOffset[]>(condition.EffectiveDatesJson)!.Length);
        var evidence=new ServicingEvidenceService(f.Factory,f.Clock);
        var required=Assert.Single((await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements,x=>x.Requirement.Code=="trading-history");
        Assert.False(required.Satisfied);
        var uploaded=await evidence.UploadAsync(f.Underwriter,cycle.DraftId,Version(conditioned.Etag!),fence,"history.txt","text/plain",System.Text.Encoding.UTF8.GetBytes("Fictional requested trading history"),Key(),Guid.NewGuid());
        var attached=await evidence.AttachAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(uploaded.Etag!),fence,uploaded.ResourceId,"trading-history",null,required.Requirement.InputFingerprint,"Attach requested business history",Key(),Guid.NewGuid());
        var association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        var reviewed=await evidence.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,association.Id,Version(attached.Etag!),fence,association.RowVersion,"accepted",required.Requirement.InputFingerprint,"Accept requested business history",Key(),Guid.NewGuid());
        Assert.True(Assert.Single((await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements,x=>x.Requirement.Code=="trading-history").Satisfied);
        Assert.Empty(await db.Set<ServicingConditionResolution>().Where(x=>x.ConditionId==condition.Id).ToArrayAsync());
        Assert.False(await service.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));
        var resolutionKey=Key();var reason="Resolve reviewed business history";
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ResolveAsync(f.Servicing,cycle.DraftId,cycle.Id,condition.Id,Version(reviewed.Etag!),fence,condition.RowVersion,association.Id,"satisfied",reason,Key(),Guid.NewGuid()))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,condition.Id,Version(reviewed.Etag!),fence,new byte[8],association.Id,"satisfied",reason,Key(),Guid.NewGuid()))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,condition.Id,Version(reviewed.Etag!),fence,condition.RowVersion,Guid.NewGuid(),"satisfied",reason,Key(),Guid.NewGuid()))).Status);
        var resolved=await service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,condition.Id,Version(reviewed.Etag!),fence,condition.RowVersion,association.Id,"satisfied",reason,resolutionKey,Guid.NewGuid());
        Assert.Equal(resolved.ResourceId,(await service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,condition.Id,Version(reviewed.Etag!),fence,condition.RowVersion,association.Id,"satisfied",reason,resolutionKey,Guid.NewGuid())).ResourceId);
        Assert.True(await service.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));
        Assert.Single(await db.Set<ServicingConditionResolution>().Where(x=>x.ConditionId==condition.Id).ToArrayAsync());
        var rejectedResolution=await service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,condition.Id,Version(resolved.Etag!),fence,condition.RowVersion,association.Id,"rejected","Reject earlier condition resolution",Key(),Guid.NewGuid());
        Assert.False(await service.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));
        resolved=await service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,condition.Id,Version(rejectedResolution.Etag!),fence,condition.RowVersion,association.Id,"satisfied","Resolve rechecked business history",Key(),Guid.NewGuid());
        Assert.True(await service.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));
        Assert.Equal(3,await db.Set<ServicingConditionResolution>().CountAsync(x=>x.ConditionId==condition.Id));
        var referralView=Assert.Single((await service.ReadReferralsAsync(f.Underwriter,cycle.DraftId)).Items,x=>x.Id==condition.ReferralId);
        Assert.Equal("conditional",referralView.State);Assert.True(referralView.DecisionReady);Assert.True(Assert.Single(referralView.Conditions).Satisfied);
        var firstPage=await service.ReadReferralsAsync(f.Underwriter,cycle.DraftId,pageSize:1);
        Assert.Single(firstPage.Items);Assert.NotNull(firstPage.NextAfterSequence);
        var secondPage=await service.ReadReferralsAsync(f.Underwriter,cycle.DraftId,firstPage.NextAfterSequence!.Value,1);
        Assert.Single(secondPage.Items);Assert.NotEqual(firstPage.Items[0].Id,secondPage.Items[0].Id);
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReadReferralsAsync(f.Underwriter,cycle.DraftId,pageSize:0))).Status);
        var beforeExpiry=f.Clock.Current;
        f.Clock.Current=(await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x=>x.Id==cycle.CurrentRatingId)).ExpiresAt;
        var expiredPage=await service.ReadReferralsAsync(f.Underwriter,cycle.DraftId);
        Assert.False(expiredPage.Applicable);Assert.All(expiredPage.Items,x=>Assert.False(x.DecisionReady));
        f.Clock.Current=beforeExpiry;
        association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==association.Id);
        var withdrawn=await evidence.WithdrawAsync(f.Underwriter,cycle.DraftId,cycle.Id,association.Id,Version(resolved.Etag!),fence,association.RowVersion,"Withdraw resolved business proof",Key(),Guid.NewGuid());
        Assert.False(await service.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));
        referralView=Assert.Single((await service.ReadReferralsAsync(f.Underwriter,cycle.DraftId)).Items,x=>x.Id==condition.ReferralId);
        Assert.Equal("conditional",referralView.State);Assert.False(referralView.DecisionReady);Assert.False(Assert.Single(referralView.Conditions).Satisfied);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,condition.Id,Version(withdrawn.Etag!),fence,condition.RowVersion,association.Id,"satisfied",reason,Key(),Guid.NewGuid()))).Status);
        fresh=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==fresh.Id);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(withdrawn.Etag!),fence,[selected[0] with{Version=fresh.RowVersion}],Key(),Guid.NewGuid()))).Status);
        var query=conditional with{Version=fresh.RowVersion,Outcome="query",Question="Please provide the trading history evidence"};
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(conditioned.Etag!),fence,[query with{Question=null}],Key(),Guid.NewGuid()))).Status);
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(conditioned.Etag!),fence,[query with{Conditions=[JsonSerializer.SerializeToElement(new{code="any-driver-minimum-licence",minimumYears=2,wordingVersion="1"})]}],Key(),Guid.NewGuid()))).Status);
        var queried=await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(withdrawn.Etag!),fence,[query],Key(),Guid.NewGuid());
        var queryDecision=await db.Set<ServicingReferralDecision>().AsNoTracking().SingleAsync(x=>x.Id==queried.ResourceId);
        Assert.Equal("query",queryDecision.Outcome);Assert.Equal(query.Question,queryDecision.Question);
        Assert.Equal("queried",(await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==fresh.Id)).State);
        var queriedRow=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==fresh.Id);
        await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(queried.Etag!),fence,[selected[0] with{Version=queriedRow.RowVersion,Outcome="reopen"}],Key(),Guid.NewGuid());
        Assert.DoesNotContain((await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements,x=>x.Requirement.Code=="trading-history");
        Assert.Equal(2,await db.Set<ServicingCondition>().CountAsync(x=>x.ReferralId==fresh.Id));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason='Withdraw fictional decision authority' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,current,fence,selected,key,Guid.NewGuid()))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,condition.Id,Version(reviewed.Etag!),fence,condition.RowVersion,association.Id,"satisfied",reason,resolutionKey,Guid.NewGuid()))).Status);
        Assert.False(Assert.Single((await service.ReadReferralsAsync(f.Underwriter,cycle.DraftId)).Items,x=>x.Id==rows[1].Id).DecisionReady);
    }
}
