using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingCapacityReadModel(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public async Task<ServicingCapacityList> ListAsync(ActorContext actor,Guid draftId,Guid? beforeId=null,int pageSize=25,CancellationToken token=default,Guid? referralId=null)
    {
        if(beforeId==Guid.Empty || referralId==Guid.Empty || pageSize is <1 or >50) throw new QuoteOperationException(422,"servicing-capacity-page-invalid");
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var draft=await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);
        var query=db.Set<ServicingCapacityCase>().AsNoTracking().Where(x=>x.DraftId==draftId);
        if(referralId is Guid referral)query=query.Where(x=>x.ReferralId==referral);
        if(beforeId is Guid id)
        {
            var cursor=await query.SingleOrDefaultAsync(x=>x.Id==id,token)??throw new QuoteOperationException(404,"servicing-capacity-cursor-not-found");
            query=query.Where(x=>x.CreatedAt<cursor.CreatedAt || x.CreatedAt==cursor.CreatedAt && x.Id.CompareTo(id)<0);
        }
        var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(pageSize+1).ToArrayAsync(token);
        var page=rows.Take(pageSize).ToArray();var items=await Summaries(db,page,token);
        await tx.CommitAsync(token);return new(draftId,draft.PolicyId,Etag(draft.RowVersion),items,rows.Length>pageSize?page[^1].Id:null);
    }

    public async Task<ServicingCapacityDetail> GetAsync(ActorContext actor,Guid draftId,Guid caseId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var draft=await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);var row=await Case(db,draftId,caseId,token);
        var now=time.GetUtcNow();var blockers=new List<string>();ServicingDecisionContext? held=null;
        if(draft.State=="draft" && draft.CurrentCycleId==row.CycleId && draft.CurrentRevisionId==row.RevisionId && row.State!="superseded")
        {
            try
            {
                held=await ServicingDecisionContext.Hold(db,actor,draftId,"policy-read",now,token,row.CycleId,write:false);
                if(held.Rating.ExpiresAt<=now) {blockers.Add("servicing-rating-expired");held=null;}
            }
            catch(QuoteOperationException e) {blockers.Add(e.Code);}
        }
        else blockers.Add("servicing-capacity-case-stale");
        var current=held is not null;var canWrite=false;var ready=false;
        if(held is not null)
        {
            var remaining=held.Input.Term with{Kind="short-period",StartsAt=held.Input.Slices[0].EffectiveAt};
            var grants=await QuoteUnderwritingScope.GrantsAsync(db,held.Scope.Source,held.Cycle.ProductVersionId,held.Scope.Eligible.BinderVersion,
                held.Scope.Eligible.Capture.Product.Code,remaining,now,token);
            var provider=await db.Set<CapacityProvider>().AnyAsync(x=>x.Id==row.ProviderId && x.State=="active",token);
            var lease=await db.Set<ServicingLease>().AsNoTracking().SingleOrDefaultAsync(x=>x.DraftId==draftId,token);
            var leased=lease is not null && lease.Active && lease.HolderId==held.Scope.Source.Scope.Actor.UserId && lease.ExpiresAt>now;
            canWrite=provider && grants.Count>0 && leased && held.Scope.Source.Scope.Actor.HasCapability("underwriting-escalate");
            if(!provider) blockers.Add("servicing-capacity-provider-unavailable");
            if(!leased) blockers.Add("servicing-lease-required");
            if(grants.Count==0) blockers.Add("servicing-capacity-authority-required");
            ready=!await ServicingCapacityAuthority.HasBlockingRequest(db,held,row.ReferralId,now,token);
            if(!ready) blockers.Add("servicing-capacity-outstanding");
        }
        var submission=row.CurrentSubmissionId is Guid submissionId?await db.Set<ServicingCapacitySubmission>().AsNoTracking().SingleAsync(x=>x.Id==submissionId,token):null;
        var response=row.CurrentResponseId is Guid responseId?await db.Set<ServicingCapacityResponseRecord>().AsNoTracking().SingleAsync(x=>x.Id==responseId,token):null;
        var conditions=response is null?[]:await db.Set<ServicingCapacityCondition>().AsNoTracking().Where(x=>x.ResponseId==response.Id).OrderBy(x=>x.Sequence).ToArrayAsync(token);
        var conditionViews=new List<ServicingCarrierConditionView>();
        foreach(var condition in conditions)
            conditionViews.Add(new(condition.Id,condition.Sequence,condition.Code,condition.Kind,Etag(condition.RowVersion),ConditionWording(condition.Code),
                JsonSerializer.Deserialize<JsonElement>(condition.DefinitionJson),JsonSerializer.Deserialize<DateTimeOffset[]>(condition.EffectiveDatesJson)!,
                held is not null && await ServicingCapacityService.CarrierConditionSatisfied(db,held,condition,token)));
        var settings=await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope.StartsWith("capacity-escalation/") && x.EffectiveFrom<=now)
            .GroupBy(x=>x.Scope).Select(x=>x.OrderByDescending(v=>v.Version).First()).Take(51).ToArrayAsync(token);
        if(settings.Length>50) throw new QuoteOperationException(409,"servicing-capacity-scenario-limit");
        var scenarios=settings.GroupBy(x=>x.Scope).Select(x=>x.OrderByDescending(v=>v.Version).First()).Where(x=>held is not null && CapacitySeed.Parse(x) is {} setting && CapacitySeed.ForProduct(setting.Scenario,held.Input.IsCommercial))
            .Select(x=>new ServicingCapacityOption(x.Id,CapacitySeed.Parse(x)!.Value.Scenario)).OrderBy(x=>x.Label).ToArray();
        var seniors=await db.Set<StaffUser>().AsNoTracking().Where(u=>u.State=="active" && u.AgencyId==null &&
            (from membership in db.Set<UserRole>() join role in db.Set<Role>() on membership.RoleId equals role.Id
             where membership.UserId==u.Id && role.Code=="senior-underwriter" && role.Scope=="internal" select role.Id).Any())
            .OrderBy(x=>x.DisplayName).ThenBy(x=>x.Id).Take(50).Select(x=>new ServicingCapacityOption(x.Id,x.DisplayName)).ToArrayAsync(token);
        var summary=(await Summaries(db,[row],token))[0];
        // Similar means prior cases on this already-authorized policy, with the
        // same binder/provider/rule. Their outcomes never grant current authority.
        var similarRows=await (from c in db.Set<ServicingCapacityCase>().AsNoTracking() join d in db.Set<ServicingDraft>() on c.DraftId equals d.Id
            join r in db.Set<ServicingReferral>() on c.ReferralId equals r.Id
            where d.PolicyId==draft.PolicyId && c.Id!=caseId && c.ProviderId==row.ProviderId && c.BinderVersionId==row.BinderVersionId && r.RuleCode==summary.RuleCode
            orderby c.CreatedAt descending,c.Id descending select c).Take(10).ToArrayAsync(token);
        var result=new ServicingCapacityDetail(summary,draft.PolicyId,Etag(draft.RowVersion),current,canWrite,ready,blockers.Distinct().ToArray(),
            submission is null?null:(await SubmissionViews(db,[submission],token))[0],response is null?null:ResponseView(response),conditionViews,
            scenarios,seniors,await Summaries(db,similarRows,token));
        await tx.CommitAsync(token);return result;
    }

    // Explicit route ownership also applies to retained, non-current history.
    private static async Task<ServicingCapacityCase> Case(BackOfficeDbContext db,Guid draftId,Guid caseId,CancellationToken token)=>
        await db.Set<ServicingCapacityCase>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==caseId && x.DraftId==draftId,token)
        ??throw new QuoteOperationException(404,"servicing-capacity-case-not-found");

    private static async Task<ServicingCapacitySummary[]> Summaries(BackOfficeDbContext db,ServicingCapacityCase[] rows,CancellationToken token)
    {
        var referralIds=rows.Select(x=>x.ReferralId).ToArray();var providerIds=rows.Select(x=>x.ProviderId).Distinct().ToArray();
        var referrals=await db.Set<ServicingReferral>().AsNoTracking().Where(x=>referralIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,token);
        var providers=await db.Set<CapacityProvider>().AsNoTracking().Where(x=>providerIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.Name,token);
        return rows.Select(x=>new ServicingCapacitySummary(x.Id,x.DraftId,x.CycleId,x.RevisionId,x.RatingId,x.ReferralId,x.ProviderId,providers[x.ProviderId],
            referrals[x.ReferralId].RuleCode,referrals[x.ReferralId].Dimension,referrals[x.ReferralId].RiskItemId,x.State,Etag(x.RowVersion),x.Reason,x.CreatedAt,
            x.CurrentSubmissionId,x.CurrentResponseId,referrals[x.ReferralId].AssignedUserId)).ToArray();
    }

    private static string Etag(byte[] bytes)=>"\""+Convert.ToBase64String(bytes)+"\"";
    private static string ConditionWording(string code)=>code switch{
        "provide-trading-history"=>"Provide reviewed trading history.","provide-driver-proof"=>"Provide reviewed proof for the specified driver.",
        "provide-premises-security"=>"Provide reviewed security evidence for the specified premises.","provide-signed-statement"=>"Provide the signed statement.",
        "overnight-security"=>"Vehicles kept at the declared secured premises overnight.","named-drivers-only"=>"Driving restricted to the specified named drivers.",
        "any-driver-minimum-licence"=>"Drivers must meet the specified minimum licence experience.","revise-stock-limit"=>"Revise the requested stock limit and rate again.",
        "revise-vehicle-limit"=>"Revise the specified vehicle limit and rate again.",_=>throw new QuoteOperationException(409,"servicing-condition-unavailable")};
}
