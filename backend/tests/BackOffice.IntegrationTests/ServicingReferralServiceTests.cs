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
        var conditioned=await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(result.Etag!),fence,[conditional],Key(),Guid.NewGuid());
        var condition=await db.Set<ServicingCondition>().AsNoTracking().SingleAsync(x=>x.ReferralId==fresh.Id);
        Assert.Equal("provide-trading-history",condition.Code);Assert.Equal(2,JsonSerializer.Deserialize<DateTimeOffset[]>(condition.EffectiveDatesJson)!.Length);
        fresh=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==fresh.Id);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(conditioned.Etag!),fence,[selected[0] with{Version=fresh.RowVersion}],Key(),Guid.NewGuid()))).Status);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason='Withdraw fictional decision authority' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,current,fence,selected,key,Guid.NewGuid()))).Status);
    }
}
