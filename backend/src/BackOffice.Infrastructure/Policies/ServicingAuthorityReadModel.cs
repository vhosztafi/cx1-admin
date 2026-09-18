using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingDatedAuthority(DateTimeOffset EffectiveAt,UnderwritingAuthorityRow Limit);
public sealed record ServicingCurrentGrant(Guid GrantId,Guid AuthorityVersionId,string Version,DateTimeOffset EffectiveFrom,DateTimeOffset EffectiveTo,
    bool ScheduleWithinAuthority,IReadOnlyList<ServicingDatedAuthority> Rows);
public sealed record ServicingCurrentAuthorityPage(Guid DraftId,Guid CycleId,Guid ReferralId,string DraftEtag,DateTimeOffset AssessedAt,bool Applicable,bool CanDecide,
    IReadOnlyList<ServicingDatedAuthority> Binder,IReadOnlyList<ServicingCurrentGrant> Items,Guid? NextAfterId);

public sealed partial class ServicingReferralService
{
    public async Task<ServicingCurrentAuthorityPage> CurrentAuthorityAsync(ActorContext actor,Guid draftId,Guid referralId,Guid? afterId=null,int pageSize=5,CancellationToken token=default)
    {
        if(referralId==Guid.Empty || afterId==Guid.Empty || pageSize is <1 or >5)throw new QuoteOperationException(422,"servicing-authority-page-invalid");
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var now=time.GetUtcNow();var held=await ServicingDecisionContext.Hold(db,actor,draftId,"policy-read",now,token);
        var referral=await db.Set<ServicingReferral>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==referralId && x.DraftId==draftId && x.CycleId==held.Cycle.Id,token)
            ??throw new QuoteOperationException(404,"servicing-referral-not-found");
        var remaining=held.Input.Term with{Kind="short-period",StartsAt=held.Input.Slices[0].EffectiveAt};
        var grants=await QuoteUnderwritingScope.GrantsAsync(db,held.Scope.Source,held.Cycle.ProductVersionId,held.Scope.Eligible.BinderVersion,
            held.Scope.Eligible.Capture.Product.Code,remaining,now,token);
        if(afterId is {} cursor && !grants.Any(x=>x.Grant.Id==cursor))throw new QuoteOperationException(409,"servicing-authority-cursor-stale");
        var selected=grants.OrderBy(x=>x.Grant.Id).Where(x=>afterId is null || x.Grant.Id.CompareTo(afterId.Value)>0).Take(pageSize+1).ToArray();
        var outcome=JsonSerializer.Deserialize<ServicingRatingOutcome>(held.Rating.ResultJson,ServicingRatingService.Json);
        if(outcome?.Rating is not {} rating || rating.Slices.Count!=held.Input.Slices.Count)throw new QuoteOperationException(409,"servicing-rating-input-unavailable");
        var risks=held.Input.Slices.Select((slice,index)=>{
            if(slice.EffectiveAt!=rating.Slices[index].EffectiveAt || !slice.ChangeIds.Order().SequenceEqual(rating.Slices[index].ChangeIds.Order()))throw new QuoteOperationException(409,"servicing-rating-input-unavailable");
            return new ServicingAuthoritySlice(slice.EffectiveAt,slice.Input.RiskForPremium(rating.Slices[index].AnnualPremium));
        }).ToArray();
        var code=referral.RuleCode+(referral.RiskItemId is {} target?":"+target.ToString("D"):"");
        IReadOnlyList<ServicingDatedAuthority> Rows(JsonElement? grant)=>risks.SelectMany(slice=>UnderwritingAuthorityView.Rows(slice.Risk,held.Scope.Eligible.Binder,grant,
            held.Input.RatingDefinition.GetProperty("minimumTradingYears").GetInt32()).Where(row=>row.Code==code).Select(row=>new ServicingDatedAuthority(slice.EffectiveAt,row))).ToArray();
        var views=new List<ServicingCurrentGrant>();
        foreach(var grant in selected.Take(pageSize))
            views.Add(new(grant.Grant.Id,grant.Version.Id,grant.Version.Version,grant.Grant.EffectiveFrom,grant.Grant.EffectiveTo,
                await ResolutionAuthority(db,held,grant.Definition,now,token),Rows(grant.Definition)));
        var applicable=held.Rating.ExpiresAt>now;
        var result=new ServicingCurrentAuthorityPage(draftId,held.Cycle.Id,referralId,Etag(held.Scope.Draft.RowVersion),now,applicable,
            applicable && held.Scope.Source.Scope.Actor.HasCapability("underwriting-decide-within-authority") && grants.Count>0,Rows(null),views,
            selected.Length>pageSize?views[^1].GrantId:null);
        await tx.CommitAsync(token);return result;
    }
}
