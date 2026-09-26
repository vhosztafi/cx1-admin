using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Infrastructure.Reporting;

public static class ReportingScope
{
    public static async Task<ActorContext> Current(BackOfficeDbContext db, ActorContext actor, CancellationToken token)
    {
        if (actor.AgencyId is not null) throw new QuoteOperationException(403, "reporting-access-denied");
        var identity = await IdentitySnapshot.Lock(db, new(actor.UserId, null), token);
        if (identity is null || !actor.Roles.SetEquals(identity.Roles.Select(x => x.Code)))
            throw new QuoteOperationException(403, "reporting-access-denied");
        return new(identity.User.Id, identity.User.TeamId, null, identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal));
    }
    public static void Require(ActorContext actor, string capability)
    {
        if (!actor.HasCapability(capability)) throw new QuoteOperationException(403, "reporting-access-denied");
    }
}
