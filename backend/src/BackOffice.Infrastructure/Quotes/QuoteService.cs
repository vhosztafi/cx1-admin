using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

// Internal snapshot, materialized while quote authority is held. Capture
// availability is independent of proposal readiness and does not authorize a write.
public sealed record StoredQuote(Quote Quote, QuoteRevision Revision, string ClientName, string AgencyName,
    string ProductCode, bool CanSave, string? CaptureUnavailableCode, QuoteTermAssessment TermAssessment, QuoteVersionPins VersionPins,
    IReadOnlyDictionary<Guid, string> VehicleCaptureModes, IReadOnlySet<(string Code, Guid? RiskItemId)> CurrentEvidence, string? MatchingCode, Guid? MatchReviewId,
    bool CanClone = false, bool CanWithdraw = false);

// Internal audited command service; HTTP validation remains at the API boundary.
public sealed class QuoteService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);

    public Task<CommandOutcome> CreateAsync(ActorContext actor, Guid relationshipId, Guid productVersionId,
        string? proposal, string key, Guid correlationId, CancellationToken token = default, Guid? matchSubmissionId = null)
    {
        QuoteRelationshipScope? scope = null; EligibleQuoteCapture? selection = null; MatchSubmission? intake = null;
        object request = matchSubmissionId is null ? new { relationshipId, productVersionId, proposal } : new { relationshipId, productVersionId, proposal, matchSubmissionId };
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, "/api/v1/quotes", key, correlationId),
            request, "quote.create-command",
            async (db, ct) =>
            {
                intake = await QuoteMatching.HoldIntakeAsync(db, actor, relationshipId, matchSubmissionId, ct);
                scope = await QuoteScope.ForRelationshipAsync(db, actor, relationshipId, QuoteAccess.Capture, ct);
                selection = await QuoteCaptureEligibility.ResolveAsync(db, scope, productVersionId, time.GetUtcNow(), token: ct);
            },
            async (db, ct) =>
            {
                var prepared = QuoteRules.Prepare(proposal, selection!.Product.Code, selection.Pins);
                var now = time.GetUtcNow();
                var quote = new Quote { AgencyId = scope!.Agency.Id, ClientId = scope.Client.Id, RelationshipId = scope.Relationship.Id,
                    ProductId = selection.Product.Id, CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
                db.Add(quote); await db.SaveChangesAsync(ct);
                await Append(db, quote, selection, prepared, 1, null, actor.UserId, now, ct);
                await QuoteMatching.AttachOrReviewAsync(db, quote, scope, intake, actor.UserId, now, ct);
                return Outcome(quote, 201);
            }, token);
    }

    public Task<CommandOutcome> SaveAsync(ActorContext actor, Guid quoteId, byte[] expectedVersion, string proposal,
        string? reason, string key, Guid correlationId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(proposal); // Omission initializes creates only; a save must never clear a draft implicitly.
        if (expectedVersion.Length != 8) throw new QuoteOperationException(400, "invalid-quote-version");
        if (reason is not null && (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000))
            throw new QuoteValidationException([new("invalid-reason", "/reason")]);
        OwnedQuoteScope? owned = null; QuoteRevision? current = null; EligibleQuoteCapture? selection = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/proposal", key, correlationId),
            new { quoteId, expectedVersion = Convert.ToBase64String(expectedVersion), proposal, reason }, "quote.save-command",
            async (db, ct) =>
            {
                owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Capture, ct);
                current = await CurrentRevision(db, owned.Quote, ct);
                selection = await QuoteCaptureEligibility.ResolveAsync(db, owned.Scope, current.ProductVersionId, time.GetUtcNow(), current.AgencyTermsVersionId, ct);
                QuoteRules.EnsureRetainedPins(Pins(current), selection.Pins);
            },
            async (db, ct) =>
            {
                var quote = owned!.Quote;
                QuoteRules.EnsureEditable(quote.State, quote.CaptureClosedAt);
                if (!CryptographicOperations.FixedTimeEquals(quote.RowVersion, expectedVersion)) throw new QuoteOperationException(412, "stale-quote");
                var prepared = QuoteRules.Prepare(proposal, selection!.Product.Code, selection.Pins);
                db.Attach(quote);
                if (!await db.Set<MatchSubmission>().AnyAsync(x => x.QuoteId == quote.Id, ct))
                {
                    await QuoteMatching.AttachOrReviewAsync(db, quote, owned.Scope, null, actor.UserId, time.GetUtcNow(), ct);
                    if (await db.Set<MatchSubmission>().AnyAsync(x => x.QuoteId == quote.Id, ct))
                    {
                        quote.UpdatedAt = time.GetUtcNow(); db.Entry(quote).Property(x => x.UpdatedAt).IsModified = true;
                        await db.SaveChangesAsync(ct);
                    }
                }
                if (QuoteRules.IsUnchanged(current!.ContentHash, prepared)) return Outcome(quote, 200);
                await Append(db, quote, selection, prepared, checked(current.Number + 1), reason, actor.UserId, time.GetUtcNow(), ct);
                return Outcome(quote, 200);
            }, token);
    }

    public async Task<StoredQuote> GetAsync(ActorContext actor, Guid quoteId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        var revision = await CurrentRevision(db, owned.Quote, token);
        var availability = await CaptureAvailability(db, owned, revision, time.GetUtcNow(), token);
        // Eligibility already holds the product when available. Historical reads
        // still resolve its name/code when current capture configuration is revoked.
        var product = availability.Product ?? await db.Set<Product>()
            .FromSqlInterpolated($"SELECT * FROM Product WITH(HOLDLOCK) WHERE Id={owned.Quote.ProductId}")
            .AsNoTracking().SingleAsync(token);
        using var intent = JsonDocument.Parse(revision.TermIntentJson);
        var evidence = await QuoteEvidenceReadModel.AssessAsync(db, revision, token);
        var matching = await QuoteMatching.AssessAsync(db, owned.Quote, time.GetUtcNow(), token);
        var result = new StoredQuote(owned.Quote, revision, owned.Scope.Client.LegalName, owned.Scope.Agency.LegalName,
            product.Code, availability.Code is null, availability.Code, QuoteTerm.Assess(intent.RootElement), Pins(revision),
            await QuoteLookupProvenance.VehicleModesAsync(db, revision, token),
            evidence.Requirements.Where(x => x.State == "current").Select(x => (x.Code, x.RiskItemId)).ToHashSet(), matching.Code,
            actor.HasCapability("match-read") ? matching.ReviewId : null,
            availability.Code is null || availability.Code == "quote-capture-closed" && owned.Quote.State is "rating-pending" or "rated" or "referred" or "approved" or "sent" or "accepted" or "declined" or "bound",
            availability.Code is null || availability.Code == "quote-capture-closed" && owned.Quote.CurrentUnderwritingCycleId is not null && owned.Quote.State is "rating-pending" or "rated" or "referred" or "approved" or "sent" or "accepted" or "declined");
        await transaction.CommitAsync(token);
        return result;
    }

    private static async Task<(Product? Product, string? Code)> CaptureAvailability(BackOfficeDbContext db,
        OwnedQuoteScope owned, QuoteRevision revision, DateTimeOffset now, CancellationToken token)
    {
        var scope = owned.Scope;
        if (!scope.Actor.HasCapability("quote-capture")) return (null, "quote-access-denied");
        if (scope.Agency.State != "active") return (null, "agency-unavailable");
        if (scope.Client.IdentityState != "active" || scope.Relationship.State != "active") return (null, "quote-relationship-unavailable");
        try
        {
            var selection = await QuoteCaptureEligibility.ResolveAsync(db, scope, revision.ProductVersionId, now, revision.AgencyTermsVersionId, token);
            if (Pins(revision) == selection.Pins && (owned.Quote.State != "draft" || owned.Quote.CaptureClosedAt is not null)) return (selection.Product, "quote-capture-closed");
            return (selection.Product, Pins(revision) == selection.Pins ? null : "quote-pinned-configuration-unavailable");
        }
        catch (QuoteOperationException error) when (error.Code is "quote-product-unavailable" or "quote-capture-configuration-unavailable" or "quote-pinned-configuration-unavailable")
        {
            // Expected capture denials must not hide already saved history.
            // Authorization, cancellation and database failures still propagate.
            return (null, error.Code);
        }
    }

    internal static Task<QuoteRevision> CurrentRevision(BackOfficeDbContext db, Quote quote, CancellationToken token) =>
        db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == quote.CurrentRevisionId && x.QuoteId == quote.Id, token);

    internal static QuoteVersionPins Pins(QuoteRevision revision)
    {
        using var references = JsonDocument.Parse(revision.ReferenceVersionsJson);
        if (!references.RootElement.TryGetProperty("referenceVersion", out var version) || version.ValueKind != JsonValueKind.String)
            throw new QuoteOperationException(409, "quote-pinned-configuration-unavailable");
        return new(revision.ProductVersionId, revision.AgencyTermsVersionId, revision.SchemaVersion, revision.QuestionSetVersion, version.GetString()!);
    }

    internal static async Task Append(BackOfficeDbContext db, Quote quote, EligibleQuoteCapture selection, PreparedQuoteCapture prepared,
        int number, string? reason, Guid actor, DateTimeOffset now, CancellationToken token)
    {
        var revision = new QuoteRevision { ClientId = quote.ClientId, RelationshipId = quote.RelationshipId, QuoteId = quote.Id, AgencyId = quote.AgencyId, ProductId = quote.ProductId, Number = number,
            ProductVersionId = selection.Pins.ProductVersionId, AgencyTermsVersionId = selection.Pins.AgencyTermsVersionId,
            SchemaVersion = selection.Pins.SchemaVersion, QuestionSetVersion = selection.Pins.QuestionSetVersion,
            ReferenceVersionsJson = JsonSerializer.Serialize(new { referenceVersion = selection.Pins.ReferenceVersion,
                captureSettingId = selection.CaptureSettingId, distributionSettingId = selection.DistributionSettingId }),
            ProposalJson = prepared.Input.Json, TermIntentJson = prepared.TermIntentJson, ContentHash = Convert.FromHexString(prepared.Input.ContentHash),
            Reason = reason, CreatedBy = actor, SavedBy = actor, CreatedAt = now, SavedAt = now };
        db.Add(revision); await db.SaveChangesAsync(token);
        await db.Set<QuoteRegistration>().Where(x => x.QuoteId == quote.Id).ExecuteDeleteAsync(token);
        db.AddRange(prepared.Registrations.Select(x => new QuoteRegistration { QuoteId = quote.Id, VehicleId = x.VehicleId, NormalizedRegistration = x.NormalizedRegistration }));
        quote.CurrentRevisionId = revision.Id;
        var eventType = number == 1 ? "quote.created" : "quote.saved";
        db.Add(new QuoteActivity { QuoteId = quote.Id, RevisionId = revision.Id, ActorId = actor, CreatedBy = actor, CreatedAt = now, OccurredAt = now, EventType = eventType });
        db.Add(new ClientActivity { ClientId = quote.ClientId, RelationshipId = quote.RelationshipId, ActorId = actor, CreatedBy = actor,
            CreatedAt = now, OccurredAt = now, EventType = eventType, RecordId = quote.Id, RecordKind = "quote" });
        await db.SaveChangesAsync(token);
    }

    private static CommandOutcome Outcome(Quote quote, int status) => new(quote.Id, status, JsonSerializer.Serialize(new { id = quote.Id }),
        Etag: "\"" + Convert.ToBase64String(quote.RowVersion) + "\"");
}
