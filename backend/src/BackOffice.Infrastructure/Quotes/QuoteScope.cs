using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public enum QuoteAccess { Read, Capture }
public sealed class QuoteOperationException(int status, string code) : Exception("The quote operation cannot be completed.")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed record QuoteRelationshipScope(ActorContext Actor, Agency Agency, ClientAccount Client, ClientAgencyRelationship Relationship);
public sealed record OwnedQuoteScope(QuoteRelationshipScope Scope, Quote Quote);

// Keep the caller's transaction open through materialization/command commit.
// These methods authorize current access before receipt resolution. Quote state,
// ETags and product eligibility belong to the command, not this scope boundary.
public static class QuoteScope
{
    public static async Task<ActorContext> AuthorizeAgencyAsync(BackOfficeDbContext db, ActorContext actor, Guid agencyId, QuoteAccess access, CancellationToken token = default)
    {
        DemandTransactionAndCapability(db, actor, access);
        var agency = await AgencyLock(db, agencyId, access, token);
        var current = await ActorLock(db, actor, access, token);
        DemandAgencyState(agency, access);
        return current;
    }

    public static async Task<QuoteRelationshipScope> ForRelationshipAsync(BackOfficeDbContext db, ActorContext actor, Guid relationshipId, QuoteAccess access, CancellationToken token = default)
    {
        DemandTransactionAndCapability(db, actor, access);
        var hint = await db.Set<ClientAgencyRelationship>().AsNoTracking().Where(x => x.Id == relationshipId)
            .Select(x => new { x.AgencyId, x.ClientId }).SingleOrDefaultAsync(token) ?? throw Missing();
        var agency = await AgencyLock(db, hint.AgencyId, access, token);
        var current = await ActorLock(db, actor, access, token);
        DemandAgencyState(agency, access);
        return await RelationshipLock(db, current, agency, relationshipId, hint.ClientId, access, token);
    }

    public static async Task<OwnedQuoteScope> ForQuoteAsync(BackOfficeDbContext db, ActorContext actor, Guid quoteId, QuoteAccess access, CancellationToken token = default)
    {
        DemandTransactionAndCapability(db, actor, access);
        var hint = await db.Set<Quote>().AsNoTracking().Where(x => x.Id == quoteId && x.CurrentRevisionId != null)
            .Select(x => new { x.AgencyId }).SingleOrDefaultAsync(token) ?? throw Missing();
        var agency = await AgencyLock(db, hint.AgencyId, access, token);
        var current = await ActorLock(db, actor, access, token);
        DemandAgencyState(agency, access);
        var query = access == QuoteAccess.Capture
            ? db.Set<Quote>().FromSqlInterpolated($"SELECT * FROM Quote WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={quoteId}")
            : db.Set<Quote>().FromSqlInterpolated($"SELECT * FROM Quote WITH(HOLDLOCK,ROWLOCK) WHERE Id={quoteId}");
        var quote = await query.AsNoTracking().SingleOrDefaultAsync(token);
        if (quote is null || quote.CurrentRevisionId is null || quote.AgencyId != agency.Id) throw Missing();
        var scope = await RelationshipLock(db, current, agency, quote.RelationshipId, quote.ClientId, access, token);
        return new(scope, quote);
    }

    private static void DemandTransactionAndCapability(BackOfficeDbContext db, ActorContext actor, QuoteAccess access)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Quote scope requires a held transaction.");
        if (access is not (QuoteAccess.Read or QuoteAccess.Capture)) throw new ArgumentOutOfRangeException(nameof(access));
        if (!actor.HasCapability(access == QuoteAccess.Read ? "quote-read" : "quote-capture")) throw Denied();
    }

    private static async Task<Agency> AgencyLock(BackOfficeDbContext db, Guid id, QuoteAccess access, CancellationToken token)
    {
        var query = access == QuoteAccess.Capture
            ? db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={id}")
            : db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(HOLDLOCK,ROWLOCK) WHERE Id={id}");
        var agency = await query.AsNoTracking().SingleOrDefaultAsync(token) ?? throw Missing();
        return agency;
    }

    private static void DemandAgencyState(Agency agency, QuoteAccess access)
    {
        if (access == QuoteAccess.Capture && agency.State != "active") throw new QuoteOperationException(409, "agency-unavailable");
    }

    private static async Task<ActorContext> ActorLock(BackOfficeDbContext db, ActorContext actor, QuoteAccess access, CancellationToken token)
    {
        var identity = await IdentitySnapshot.Lock(db, new IdentityReference(actor.UserId, null), token);
        if (identity is null || !actor.Roles.SetEquals(identity.Roles.Select(x => x.Code))) throw Denied();
        var current = new ActorContext(identity.User.Id, identity.User.TeamId, identity.User.AgencyId,
            identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal));
        if (!current.HasCapability(access == QuoteAccess.Read ? "quote-read" : "quote-capture")) throw Denied();
        return current;
    }

    private static async Task<QuoteRelationshipScope> RelationshipLock(BackOfficeDbContext db, ActorContext actor, Agency agency, Guid id, Guid clientId, QuoteAccess access, CancellationToken token)
    {
        var client = await db.Set<ClientAccount>().FromSqlInterpolated($"SELECT * FROM ClientAccount WITH(HOLDLOCK,ROWLOCK) WHERE Id={clientId}")
            .AsNoTracking().SingleOrDefaultAsync(token) ?? throw Missing();
        var relationship = await db.Set<ClientAgencyRelationship>().FromSqlInterpolated($"SELECT * FROM ClientAgencyRelationship WITH(HOLDLOCK,ROWLOCK) WHERE Id={id}")
            .AsNoTracking().SingleOrDefaultAsync(token);
        if (relationship is null || relationship.ClientId != clientId || relationship.AgencyId != agency.Id) throw Missing();
        if (access == QuoteAccess.Capture && (client.IdentityState != "active" || relationship.State != "active"))
            throw new QuoteOperationException(409, "quote-relationship-unavailable");
        return new(actor, agency, client, relationship);
    }

    private static QuoteOperationException Missing() => new(404, "quote-context-not-found");
    private static QuoteOperationException Denied() => new(403, "quote-access-denied");
}
