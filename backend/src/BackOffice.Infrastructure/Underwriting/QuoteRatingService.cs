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

public sealed record StoredRatingInput(string Format, ProjectedUnderwritingInput Input, int CommissionBasisPoints,
    decimal? MinimumPremium, Guid RuntimeVersionId, Guid ScenarioVersionId)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ProjectedCommercialUnderwritingInput? Commercial { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public ResolvedQuoteTerm Term => Commercial?.Term ?? Input.Term;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCommercial => Format == "commercial-underwriting-input-1" && Commercial is not null && Input is null;

    public static StoredRatingInput Read(UnderwritingCycle cycle)
    {
        var value = JsonSerializer.Deserialize<StoredRatingInput>(cycle.InputJson, QuoteRatingService.Json);
        if (value is null || !(value.IsCommercial || value.Format == "underwriting-input-1" && value.Input is not null && value.Commercial is null))
            throw new QuoteOperationException(409, "underwriting-input-invalid");
        return value;
    }

    // Product-specific downstream consumers are enabled in their owning slice.
    // They must reject the new format before reading Motor Trade-only facts.
    public static StoredRatingInput ReadMotorTrade(UnderwritingCycle cycle)
    {
        var value = JsonSerializer.Deserialize<StoredRatingInput>(cycle.InputJson, QuoteRatingService.Json);
        if (value is null || value.Format != "underwriting-input-1" || value.Input is null || value.Commercial is not null)
            throw new QuoteOperationException(409, "commercial-underwriting-progression-unavailable");
        return value;
    }
}

public sealed class QuoteRatingService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public const string WorkKind = "quote-rating";
    private readonly SqlCommandBoundary commands = new(factory, time);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<CommandOutcome> RateAsync(ActorContext actor, Guid quoteId, Guid revisionId, byte[] quoteVersion,
        string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        if (quoteVersion.Length != 8 || revisionId == Guid.Empty) throw new QuoteOperationException(400, "invalid-quote-version");
        reason = Reason(reason, 1000);
        OwnedQuoteScope? owned = null; QuoteRevision? revision = null; EligibleQuoteRating? eligible = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/rate", key, correlationId),
            new { quoteId, revisionId, reason, version = Convert.ToBase64String(quoteVersion) }, "quote.rating-requested-command",
            async (db, ct) =>
            {
                owned = await QuoteUnderwritingScope.HoldAsync(db, actor, quoteId, "quote-rate", ct);
                revision = await QuoteService.CurrentRevision(db, owned.Quote, ct);
                using var intent = JsonDocument.Parse(revision.TermIntentJson);
                var term = QuoteTerm.Assess(intent.RootElement);
                if (term.Term is null) throw new QuoteValidationException(term.Issues);
                eligible = await QuoteRatingEligibility.ResolveAsync(db, owned, revision.ProductVersionId, revision.AgencyTermsVersionId, term.Term, time.GetUtcNow(), ct);
                QuoteRules.EnsureRetainedPins(QuoteService.Pins(revision), eligible.Capture.Pins);
            },
            async (db, ct) =>
            {
                var quote = owned!.Quote; var now = time.GetUtcNow();
                if (!CryptographicOperations.FixedTimeEquals(quote.RowVersion, quoteVersion) || revision!.Id != revisionId)
                    throw new QuoteOperationException(412, "stale-quote");
                if (quote.State is not ("draft" or "rated" or "referred")) throw new QuoteOperationException(409, "quote-rating-state");
                var prior = quote.CurrentUnderwritingCycleId is null ? null : await db.Set<UnderwritingCycle>()
                    .FromSqlInterpolated($"SELECT * FROM UnderwritingCycle WITH(UPDLOCK,HOLDLOCK) WHERE Id={quote.CurrentUnderwritingCycleId} AND QuoteId={quoteId}").SingleAsync(ct);
                if (quote.CaptureClosedAt is not null && (prior is null || prior.QuoteRevisionId != revisionId || prior.State != "rated"))
                    throw new QuoteOperationException(409, "quote-capture-closed");
                using var proposal = JsonDocument.Parse(revision.ProposalJson);
                var matching = await QuoteMatching.AssessAsync(db, quote, now, ct);
                var modes = await QuoteLookupProvenance.VehicleModesAsync(db, revision, ct);
                var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
                var commercial = eligible!.Capture.Product.Code == CommercialCaptureRules.ProductCode
                    ? CommercialUnderwritingInput.Project(proposal.RootElement, eligible.Capture.Pins, eligible.Rating, day) : null;
                var input = commercial is null ? QuoteUnderwritingInput.Project(proposal.RootElement, eligible.Rating) : null;
                var term = commercial?.Term ?? input!.Term;
                var readiness = QuoteReadiness.Assess(quoteId, revisionId, proposal.RootElement, new(term, []), null, day,
                    modes, matchingCode: matching.Code, includeEvidence: false);
                if (!readiness.Ready) throw new QuoteValidationException(readiness.Issues.Where(x => x.Severity == "error").Select(x => new QuoteFieldIssue(x.Code, x.Path)).ToArray());
                var hash = UnderwritingHashes.Pricing(new(quoteId, revisionId, quote.AgencyId, quote.ClientId, quote.RelationshipId,
                    eligible.RatingVersion.Id, eligible.BinderVersion.Id, eligible.AuthorityVersion.Id), eligible.Capture.Pins, commercial?.Pricing ?? input!.Pricing);
                var cycle = new UnderwritingCycle { QuoteId = quoteId, QuoteRevisionId = revisionId, AgencyId = quote.AgencyId, ClientId = quote.ClientId,
                    RelationshipId = quote.RelationshipId, ProductId = quote.ProductId, ProductVersionId = revision.ProductVersionId, AgencyTermsVersionId = revision.AgencyTermsVersionId,
                    RatingRuleVersionId = eligible.RatingVersion.Id, BinderVersionId = eligible.BinderVersion.Id, AuthorityVersionId = eligible.AuthorityVersion.Id,
                    Sequence = checked((await db.Set<UnderwritingCycle>().Where(x => x.QuoteId == quoteId).MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1),
                    PricingInputHash = Convert.FromHexString(hash.ContentHash), StartsAt = term.StartsAt, EndsAt = term.EndsAt,
                    InputJson = JsonSerializer.Serialize(new StoredRatingInput(commercial is null ? "underwriting-input-1" : "commercial-underwriting-input-1", input!, eligible.CommissionBasisPoints, eligible.MinimumPremium, eligible.RuntimeVersion.Id, eligible.ScenarioVersion.Id) { Commercial = commercial }, Json),
                    RequestedBy = actor.UserId, CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
                var work = new OutboxWork { Kind = WorkKind, SubjectRecordId = cycle.Id, OperationKey = $"quote-rating/{cycle.Id:N}", ScenarioVersionId = eligible.ScenarioVersion.Id,
                    Payload = JsonSerializer.Serialize(new { cycleId = cycle.Id, quoteId }), NextAttemptAt = now, CorrelationId = correlationId, CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
                db.Add(work); await db.SaveChangesAsync(ct); cycle.WorkId = work.Id; db.Add(cycle); await db.SaveChangesAsync(ct);
                if (prior is not null)
                {
                    prior.State = "superseded"; prior.SupersededAt = now; prior.SupersededReason = reason; prior.UpdatedAt = now;
                    var referrals = await db.Set<QuoteReferral>().Where(x => x.CycleId == prior.Id && x.State != "superseded").ToArrayAsync(ct);
                    foreach (var referral in referrals) { referral.State = "superseded"; referral.UpdatedAt = now; }
                }
                db.Attach(quote); quote.State = "rating-pending"; quote.CurrentUnderwritingCycleId = cycle.Id;
                quote.CaptureClosedAt = now; quote.CaptureClosedReason = reason; quote.UpdatedAt = now;
                db.Add(new QuoteActivity { QuoteId = quoteId, RevisionId = revisionId, ActorId = actor.UserId, CreatedBy = actor.UserId, CreatedAt = now, OccurredAt = now, EventType = "quote.rating-requested" });
                await db.SaveChangesAsync(ct);
                var etag = "\"" + Convert.ToBase64String(quote.RowVersion) + "\"";
                return new CommandOutcome(cycle.Id, 202, JsonSerializer.Serialize(new { id = cycle.Id, quoteId, quoteEtag = etag, jobId = work.Id, state = "queued" }), Etag: etag);
            }, token);
    }

    internal static string Reason(string reason, int maximum = 2000)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > maximum || reason.Any(char.IsControl)) throw new QuoteOperationException(422, "underwriting-reason-required");
        return reason.Trim();
    }
}
