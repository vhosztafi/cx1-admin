using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

internal sealed record UnderwritingDecisionContext(OwnedQuoteScope Owned, UnderwritingCycle Cycle, QuoteRevision Revision,
    QuoteRatingResult? Rating, StoredRatingInput Input, EligibleQuoteRating Eligible, IReadOnlyList<EffectiveUnderwritingGrant> Grants)
{
    internal static async Task<UnderwritingDecisionContext> Hold(BackOfficeDbContext db, ActorContext actor, Guid quoteId,
        Guid? cycleId, string action, DateTimeOffset now, bool grantRequired, CancellationToken token)
    {
        var owned = await QuoteUnderwritingScope.HoldAsync(db, actor, quoteId, action, token);
        var id = cycleId ?? owned.Quote.CurrentUnderwritingCycleId ?? throw new QuoteOperationException(409, "underwriting-cycle-required");
        var cycle = await db.Set<UnderwritingCycle>().FromSqlInterpolated($"SELECT * FROM UnderwritingCycle WITH(HOLDLOCK) WHERE Id={id} AND QuoteId={quoteId}").AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "underwriting-cycle-not-found");
        var revision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == cycle.QuoteRevisionId && x.QuoteId == quoteId, token);
        var input = action is "underwriting-decide-within-authority" or "underwriting-evidence-write" or "underwriting-evidence-review"
            or "underwriting-escalate" or "underwriting-record-capacity" or "quote-terms" or "quote-acceptance" or "policy-issue-within-authority"
            ? StoredRatingInput.Read(cycle) : StoredRatingInput.ReadMotorTrade(cycle);
        var eligible = await QuoteRatingEligibility.ResolveAsync(db, owned, cycle.ProductVersionId, cycle.AgencyTermsVersionId, input.Term, now, token);
        if (eligible.RatingVersion.Id != cycle.RatingRuleVersionId || eligible.BinderVersion.Id != cycle.BinderVersionId || eligible.AuthorityVersion.Id != cycle.AuthorityVersionId)
            throw new QuoteOperationException(409, "underwriting-cycle-stale");
        if (input.IsCommercial && (owned.Quote.CurrentUnderwritingCycleId != cycle.Id || owned.Quote.CurrentRevisionId != revision.Id ||
            cycle.State != "rated" || input.RuntimeVersionId != eligible.RuntimeVersion.Id || input.ScenarioVersionId != eligible.ScenarioVersion.Id ||
            input.CommissionBasisPoints != eligible.CommissionBasisPoints || input.MinimumPremium != eligible.MinimumPremium))
            throw new QuoteOperationException(409, "underwriting-cycle-stale");
        var grants = await QuoteUnderwritingScope.GrantsAsync(db, owned, cycle.ProductVersionId, eligible.BinderVersion, eligible.Capture.Product.Code, input.Term, now, token);
        if (grantRequired && grants.Count == 0) throw new QuoteOperationException(403, "underwriting-authority-required");
        var rating = cycle.CurrentRatingId is Guid ratingId ? await db.Set<QuoteRatingResult>().AsNoTracking().SingleAsync(x => x.Id == ratingId && x.CycleId == cycle.Id && x.QuoteId == quoteId, token) : null;
        return new(owned, cycle, revision, rating, input, eligible, grants);
    }

    internal void Current(byte[] version, DateTimeOffset now, bool currentPrice = false)
    {
        CheckVersion(Owned.Quote.RowVersion, version, "stale-quote");
        if (Owned.Quote.CurrentUnderwritingCycleId != Cycle.Id || Owned.Quote.CurrentRevisionId != Revision.Id || Cycle.State != "rated" ||
            Owned.Quote.State is "bound" or "withdrawn" or "draft" || Cycle.AgencyId != Owned.Quote.AgencyId || Cycle.ClientId != Owned.Quote.ClientId || Cycle.RelationshipId != Owned.Quote.RelationshipId)
            throw new QuoteOperationException(409, "underwriting-cycle-stale");
        if (currentPrice && (Rating is not { Outcome: "rated" } || Rating.ExpiresAt <= now)) throw new QuoteOperationException(409, "quote-rating-expired");
    }
    internal UnderwritingRisk Risk => Input.Input.RiskForPremium(Rating?.AnnualPremium ?? throw new QuoteOperationException(409, "quote-rating-required"));
    internal static void CheckVersion(byte[] actual, byte[] expected, string code)
    { if (expected.Length != 8) throw new QuoteOperationException(400, "invalid-underwriting-version"); if (!CryptographicOperations.FixedTimeEquals(actual, expected)) throw new QuoteOperationException(412, code); }
    internal async Task<CommandOutcome> Receipt(BackOfficeDbContext db, Guid id, int status, string activity, DateTimeOffset now, CancellationToken token)
    {
        var quote = Owned.Quote; if (db.Entry(quote).State == EntityState.Detached) db.Attach(quote);
        quote.UpdatedAt = now; db.Entry(quote).Property(x => x.UpdatedAt).IsModified = true;
        db.Add(new QuoteActivity { QuoteId = quote.Id, RevisionId = Revision.Id, ActorId = Owned.Scope.Actor.UserId, CreatedBy = Owned.Scope.Actor.UserId, CreatedAt = now, OccurredAt = now, EventType = activity });
        await db.SaveChangesAsync(token); var etag = Etag(quote.RowVersion);
        return new(id, status, JsonSerializer.Serialize(new { id, quoteId = quote.Id, quoteEtag = etag }), Etag: etag);
    }
    internal static string Etag(byte[] version) => "\"" + Convert.ToBase64String(version) + "\"";
}
