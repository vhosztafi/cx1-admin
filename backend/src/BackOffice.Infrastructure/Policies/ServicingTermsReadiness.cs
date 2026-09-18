using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingReferralService
{
    internal async Task RequireTermsReady(BackOfficeDbContext db,ServicingDecisionContext held,DateTimeOffset now,bool signing,CancellationToken token)
    {
        var conditions=await ActiveConditions(db,held.Cycle.Id,token);
        foreach(var condition in conditions)
            if((signing || condition.Code!="provide-signed-statement") && !await ConditionSatisfied(db,held,condition,token))
                throw new QuoteOperationException(409,"servicing-condition-outstanding");
        var referrals=await db.Set<ServicingReferral>().AsNoTracking().Where(x=>x.CycleId==held.Cycle.Id).Take(101).ToArrayAsync(token);
        if(referrals.Length>100)throw new QuoteOperationException(409,"servicing-referral-limit");
        foreach(var referral in referrals)
        {
            if(referral.State is not("approved" or "conditional") || referral.LatestDecisionId is null)
                throw new QuoteOperationException(409,"servicing-referral-outstanding");
            var decision=await db.Set<ServicingReferralDecision>().AsNoTracking().SingleAsync(x=>x.Id==referral.LatestDecisionId,token);
            if(decision.Outcome is not("approve" or "approve-with-conditions"))throw new QuoteOperationException(409,"servicing-referral-outstanding");
            var authority=await db.Set<AuthorityVersion>().FromSqlInterpolated($"SELECT * FROM AuthorityVersion WITH(HOLDLOCK) WHERE Id={decision.AuthorityVersionId}").AsNoTracking().SingleAsync(token);
            var start=held.Input.Slices[0].EffectiveAt;var end=held.Input.Term.EndsAt;
            if(authority.State!="published" || authority.EffectiveFrom>now || authority.EffectiveTo<=now || authority.EffectiveFrom>start || authority.EffectiveTo<end ||
                !await db.Set<UserAuthorityGrant>().AnyAsync(x=>x.Id==decision.GrantId && x.UserId==decision.ActorId && x.AuthorityVersionId==authority.Id &&
                    x.RevokedAt==null && x.EffectiveFrom<=now && x.EffectiveTo>now && x.EffectiveFrom<=start && x.EffectiveTo>=end,token) ||
                !await db.Set<StaffUser>().AnyAsync(x=>x.Id==decision.ActorId && x.State=="active" && x.AgencyId==null,token) ||
                !await (from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id
                    where link.UserId==decision.ActorId && (role.Code=="underwriter" || role.Code=="senior-underwriter") select link.Id).AnyAsync(token) ||
                !await ResolutionAuthority(db,held,JsonSerializer.Deserialize<JsonElement>(authority.DefinitionJson),now,token))
                throw new QuoteOperationException(409,"servicing-decision-authority-stale");
            if(await ServicingCapacityAuthority.HasBlockingRequest(db,held,referral.Id,now,token))
                throw new QuoteOperationException(409,"servicing-capacity-outstanding");
        }
    }
}
