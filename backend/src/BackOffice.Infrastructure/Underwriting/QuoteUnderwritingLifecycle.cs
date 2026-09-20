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

public sealed class QuoteUnderwritingLifecycle(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);

    public Task<CommandOutcome> ReturnToDraftAsync(ActorContext actor, Guid quoteId, Guid cycleId, byte[] version, string reason,
        string key, Guid correlationId, CancellationToken token = default)
    {
        Validate(version, cycleId); reason = QuoteRatingService.Reason(reason); OwnedQuoteScope? owned = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/return-to-draft", key, correlationId),
            new { quoteId, cycleId, version = Convert.ToBase64String(version), reason }, "quote.returned-to-draft",
            async (db, ct) => { owned = await QuoteUnderwritingScope.HoldAsync(db, actor, quoteId, "quote-revise", ct); },
            async (db, ct) =>
            {
                var quote = owned!.Quote; Current(quote, version);
                if (!UnderwritingLifecycleRules.CanReturnToDraft(quote.State) || quote.CurrentUnderwritingCycleId != cycleId) throw new QuoteOperationException(409, "quote-revision-state");
                var now = time.GetUtcNow(); await SupersedeAsync(db, quote, reason, now, ct);
                db.Attach(quote); quote.State = "draft"; quote.CurrentUnderwritingCycleId = null; quote.CaptureClosedAt = null; quote.CaptureClosedReason = null; quote.UpdatedAt = now;
                Activity(db, quote, actor.UserId, now, "quote.returned-to-draft"); await db.SaveChangesAsync(ct); return Receipt(quote, quote.Id);
            }, token);
    }

    public Task<CommandOutcome> RefreshAsync(ActorContext actor, Guid quoteId, Guid revisionId, Guid productVersionId, Guid confirmedTermsId,
        byte[] version, string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        Validate(version, revisionId); if (productVersionId == Guid.Empty || confirmedTermsId == Guid.Empty) throw new QuoteOperationException(400, "refresh-context-required");
        reason = QuoteRatingService.Reason(reason, 1000);
        OwnedQuoteScope? owned = null; QuoteRevision? revision = null; EligibleQuoteRating? selection = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/underwriting/refresh", key, correlationId),
            new { quoteId, revisionId, productVersionId, confirmedTermsId, version = Convert.ToBase64String(version), reason }, "quote.versions-refreshed",
            async (db, ct) =>
            {
                owned = await QuoteUnderwritingScope.HoldAsync(db, actor, quoteId, "quote-revise", ct);
                revision = await QuoteService.CurrentRevision(db, owned.Quote, ct);
                using var intent = JsonDocument.Parse(revision.TermIntentJson); var term = QuoteTerm.Assess(intent.RootElement);
                if (term.Term is null) throw new QuoteValidationException(term.Issues);
                selection = await QuoteRatingEligibility.ResolveAsync(db, owned, productVersionId, confirmedTermsId, term.Term, time.GetUtcNow(), ct);
            },
            async (db, ct) =>
            {
                var quote = owned!.Quote; Current(quote, version); QuoteRules.EnsureEditable(quote.State, quote.CaptureClosedAt);
                if (revision!.Id != revisionId) throw new QuoteOperationException(412, "stale-quote");
                if (revision.ProductVersionId == productVersionId && revision.AgencyTermsVersionId == confirmedTermsId) throw new QuoteOperationException(409, "quote-versions-unchanged");
                var prepared = QuoteRules.Prepare(revision.ProposalJson, selection!.Capture.Product.Code, selection.Capture.Pins);
                var now = time.GetUtcNow(); db.Attach(quote);
                await QuoteService.Append(db, quote, selection.Capture, prepared, checked(revision.Number + 1), reason, actor.UserId, now, ct);
                await QuoteMatching.AttachOrReviewAsync(db, quote, owned.Scope, null, actor.UserId, now, ct);
                Activity(db, quote, actor.UserId, now, "quote.versions-refreshed"); await db.SaveChangesAsync(ct); return Receipt(quote, quote.CurrentRevisionId!.Value);
            }, token);
    }

    public Task<CommandOutcome> SubmitAsync(ActorContext actor, Guid quoteId, Guid cycleId, byte[] version, string reason,
        string key, Guid correlationId, CancellationToken token = default)
    {
        Validate(version, cycleId); reason = QuoteRatingService.Reason(reason);
        OwnedQuoteScope? owned = null; UnderwritingCycle? cycle = null; EligibleQuoteRating? eligible = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/submit", key, correlationId),
            new { quoteId, cycleId, version = Convert.ToBase64String(version), reason }, "quote.submitted",
            async (db, ct) =>
            {
                owned = await QuoteUnderwritingScope.HoldAsync(db, actor, quoteId, "quote-submit", ct);
                cycle = await db.Set<UnderwritingCycle>().FromSqlInterpolated($"SELECT * FROM UnderwritingCycle WITH(HOLDLOCK) WHERE Id={cycleId} AND QuoteId={quoteId}").AsNoTracking().SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "underwriting-cycle-not-found");
                var input = StoredRatingInput.Read(cycle);
                eligible = await QuoteRatingEligibility.ResolveAsync(db, owned, cycle.ProductVersionId, cycle.AgencyTermsVersionId, input.Term, time.GetUtcNow(), ct);
                if (input.IsCommercial && (owned.Quote.CurrentUnderwritingCycleId != cycle.Id || owned.Quote.CurrentRevisionId != cycle.QuoteRevisionId || cycle.State != "rated" ||
                    input.RuntimeVersionId != eligible.RuntimeVersion.Id || input.ScenarioVersionId != eligible.ScenarioVersion.Id ||
                    input.CommissionBasisPoints != eligible.CommissionBasisPoints || input.MinimumPremium != eligible.MinimumPremium))
                    throw new QuoteOperationException(409, "underwriting-cycle-stale");
                if (eligible.RatingVersion.Id != cycle.RatingRuleVersionId || eligible.BinderVersion.Id != cycle.BinderVersionId || eligible.AuthorityVersion.Id != cycle.AuthorityVersionId)
                    throw new QuoteOperationException(409, "underwriting-cycle-stale");
            },
            async (db, ct) =>
            {
                var quote = owned!.Quote; Current(quote, version); var now = time.GetUtcNow();
                if (quote.State is not ("rated" or "referred") || quote.CurrentUnderwritingCycleId != cycleId || quote.CurrentRevisionId != cycle!.QuoteRevisionId || cycle.State != "rated")
                    throw new QuoteOperationException(409, "quote-submission-state");
                if (!await db.Set<QuoteRatingResult>().AnyAsync(x => x.Id == cycle.CurrentRatingId && x.CycleId == cycleId && x.Outcome == "rated" && x.ExpiresAt > now, ct))
                    throw new QuoteOperationException(409, "quote-rating-expired");
                if ((await QuoteMatching.AssessAsync(db, quote, now, ct)).Code is { } blocker) throw new QuoteOperationException(409, blocker);
                var submission = new QuoteSubmission { QuoteId = quoteId, CycleId = cycleId,
                    Sequence = checked((await db.Set<QuoteSubmission>().Where(x => x.CycleId == cycleId).MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1),
                    OperationKey = key, SubmittedBy = actor.UserId, RoutingVersionId = eligible!.RuntimeVersion.Id, AssignedTeamId = eligible.Runtime.RoutingTeamId,
                    Reason = reason, CreatedBy = actor.UserId, CreatedAt = now };
                db.Add(submission); db.Attach(quote); quote.UpdatedAt = now;
                db.Entry(quote).Property(x => x.UpdatedAt).IsModified = true;
                Activity(db, quote, actor.UserId, now, "quote.submitted"); await db.SaveChangesAsync(ct); return Receipt(quote, submission.Id);
            }, token);
    }

    internal static async Task SupersedeAsync(BackOfficeDbContext db, Quote quote, string reason, DateTimeOffset now, CancellationToken token)
    {
        if (quote.CurrentUnderwritingCycleId is not Guid id) return;
        var cycle = await db.Set<UnderwritingCycle>().FromSqlInterpolated($"SELECT * FROM UnderwritingCycle WITH(UPDLOCK,HOLDLOCK) WHERE Id={id} AND QuoteId={quote.Id}").SingleAsync(token);
        if (cycle.State == "bound" || quote.State == "bound") throw new QuoteOperationException(409, "quote-bound");
        if (cycle.State != "superseded") { cycle.State = "superseded"; cycle.SupersededAt = now; cycle.SupersededReason = reason; cycle.UpdatedAt = now; }
        foreach (var referral in await db.Set<QuoteReferral>().Where(x => x.CycleId == id && x.State != "superseded").ToArrayAsync(token))
        { referral.State = "superseded"; referral.UpdatedAt = now; }
    }
    private static void Validate(byte[] version, Guid id) { if (version.Length != 8 || id == Guid.Empty) throw new QuoteOperationException(400, "invalid-quote-version"); }
    private static void Current(Quote quote, byte[] version) { if (!CryptographicOperations.FixedTimeEquals(quote.RowVersion, version)) throw new QuoteOperationException(412, "stale-quote"); }
    private static void Activity(BackOfficeDbContext db, Quote quote, Guid actor, DateTimeOffset now, string kind) =>
        db.Add(new QuoteActivity { QuoteId = quote.Id, RevisionId = quote.CurrentRevisionId!.Value, ActorId = actor, CreatedBy = actor, CreatedAt = now, OccurredAt = now, EventType = kind });
    private static CommandOutcome Receipt(Quote quote, Guid id)
    {
        var etag = "\"" + Convert.ToBase64String(quote.RowVersion) + "\"";
        return new(id, 200, JsonSerializer.Serialize(new { id, quoteId = quote.Id, quoteEtag = etag }), Etag: etag);
    }
}
