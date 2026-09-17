using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

internal static class PolicyScope
{
    internal static async Task<Policy> Hold(BackOfficeDbContext db, ActorContext actor, Guid policyId, CancellationToken token)
    {
        if (!actor.HasCapability("policy-read")) throw new QuoteOperationException(403, "policy-access-denied");
        var hint = await db.Set<Policy>().AsNoTracking().Where(x => x.Id == policyId).Select(x => new { x.SourceQuoteId }).SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "policy-not-found");
        var owned = await QuoteScope.ForQuoteAsync(db, actor, hint.SourceQuoteId, QuoteAccess.Read, token);
        if (!owned.Scope.Actor.HasCapability("policy-read")) throw new QuoteOperationException(403, "policy-access-denied");
        var policy = await db.Set<Policy>().FromSqlInterpolated($"SELECT * FROM Policy WITH(HOLDLOCK) WHERE Id={policyId}").AsNoTracking().SingleAsync(token);
        if (policy.SourceQuoteId != owned.Quote.Id || policy.AgencyId != owned.Quote.AgencyId || policy.ClientId != owned.Quote.ClientId || policy.RelationshipId != owned.Quote.RelationshipId || owned.Quote.BoundPolicyId != policy.Id)
            throw new QuoteOperationException(404, "policy-not-found");
        return policy;
    }
}
