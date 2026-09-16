using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Parties;

public static partial class MatchService
{
    internal sealed record HeldMatch(Agency Agency, MatchSubmission Intake, MatchReview Review, Quote? Quote);

    // Call from ExecuteAuthorizedAsync before resolving receipts, and keep its
    // transaction open. Fresh version/state checks belong in DecideAsync.
    public static async Task AuthorizeHeldAsync(BackOfficeDbContext db, ActorContext actor, Guid id,
        ValidatedMatchDecision input, CancellationToken token = default)
        => _ = await HoldAuthorityAsync(db, actor, id, input, token);

    private static async Task<HeldMatch> HoldAuthorityAsync(BackOfficeDbContext db, ActorContext actor, Guid id,
        ValidatedMatchDecision input, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Match authority requires a held transaction.");
        var hint = await AuthorizeAsync(db, actor, id, input, token);
        var agencyId = await db.Set<MatchSubmission>().Where(x => x.Id == hint.SubmissionId).Select(x => x.AgencyId).SingleAsync(token);
        var agency = await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={agencyId}").SingleAsync(token);
        var identity = await IdentitySnapshot.Lock(db, new IdentityReference(actor.UserId, null), token);
        if (identity is null || !actor.Roles.SetEquals(identity.Roles.Select(x => x.Code))) throw new MatchOperationException(403, "match-decision-forbidden");
        var current = new ActorContext(identity.User.Id, identity.User.TeamId, identity.User.AgencyId, identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal));
        if (!current.HasCapability("match-review")) throw new MatchOperationException(403, "match-decision-forbidden");
        var intake = await db.Set<MatchSubmission>().FromSqlInterpolated($"SELECT * FROM MatchSubmission WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={hint.SubmissionId}").SingleAsync(token);
        var review = await db.Set<MatchReview>().FromSqlInterpolated($"SELECT * FROM MatchReview WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={id}").SingleAsync(token);
        if (intake.AgencyId != agencyId || review.SubmissionId != intake.Id) throw new MatchOperationException(409, "match-source-changed");
        if (input.CandidateClientId is Guid candidate && candidate != review.CandidateClientId) throw new MatchOperationException(422, "invalid-candidate");
        Quote? quote = null;
        if (intake.QuoteId is Guid quoteId)
        {
            if (agency.State != "active") throw new MatchOperationException(409, "agency-unavailable");
            quote = await db.Set<Quote>().FromSqlInterpolated($"SELECT * FROM Quote WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={quoteId}").SingleOrDefaultAsync(token);
            if (quote is null || quote.CurrentRevisionId is null || quote.AgencyId != intake.AgencyId ||
                quote.ClientId != intake.LinkedClientId || quote.RelationshipId != intake.LinkedRelationshipId)
                throw new MatchOperationException(409, "match-source-changed");
            var client = await db.Set<ClientAccount>().FromSqlInterpolated($"SELECT * FROM ClientAccount WITH(HOLDLOCK,ROWLOCK) WHERE Id={quote.ClientId}").SingleAsync(token);
            var relationship = await db.Set<ClientAgencyRelationship>().FromSqlInterpolated($"SELECT * FROM ClientAgencyRelationship WITH(HOLDLOCK,ROWLOCK) WHERE Id={quote.RelationshipId}").SingleAsync(token);
            if (client.IdentityState != "active" || relationship.State != "active" || relationship.ClientId != client.Id || relationship.AgencyId != agency.Id)
                throw new MatchOperationException(409, "relationship-unavailable");
        }
        return new(agency, intake, review, quote);
    }
}
