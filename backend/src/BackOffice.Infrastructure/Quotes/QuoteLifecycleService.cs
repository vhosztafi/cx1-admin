using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public sealed record QuoteRevisionView(Guid Id, Guid QuoteId, Guid ClientId, Guid RelationshipId, int Number, Guid ProductVersionId, Guid AgencyTermsVersionId,
    string QuestionSetVersion, string ReferenceDataVersion, JsonElement Proposal, string ProposalHash, DateTimeOffset SavedAt,
    string SavedByLabel, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason);
public sealed record QuoteRevisionPage(QuoteRevisionView[] Items, int TotalCount, Guid CurrentRevisionId);
public sealed record QuoteCloneTerms(Guid SourceRevisionId, Guid RelationshipId, Guid AgencyTermsVersionId, int Version,
    DateOnly EffectiveFrom, bool ConfirmationRequired);

public sealed class QuoteLifecycleService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);
    private sealed record CloneScope(OwnedQuoteScope Source, QuoteRevision Revision, QuoteRelationshipScope Target, EligibleQuoteCapture Destination);

    public async Task<QuoteRevisionPage> RevisionsAsync(ActorContext actor, Guid quoteId, int offset, int size, CancellationToken token = default)
    {
        if (offset < 0 || size is < 1 or > 100) throw new QuoteOperationException(400, "invalid-query");
        await using var db = await factory.CreateDbContextAsync(token); await using var transaction = await db.Database.BeginTransactionAsync(token);
        var owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        var query = db.Set<QuoteRevision>().AsNoTracking().Where(x => x.QuoteId == quoteId);
        var count = await query.CountAsync(token);
        var rows = await query.OrderByDescending(x => x.Number).Skip(offset).Take(size).ToArrayAsync(token);
        var result = new QuoteRevisionPage(await Views(db, rows, token), count, owned.Quote.CurrentRevisionId!.Value);
        await transaction.CommitAsync(token); return result;
    }

    public async Task<QuoteRevisionView> RevisionAsync(ActorContext actor, Guid quoteId, Guid revisionId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var transaction = await db.Database.BeginTransactionAsync(token);
        await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        var result = (await Views(db, [await Revision(db, quoteId, revisionId, token)], token)).Single();
        await transaction.CommitAsync(token); return result;
    }

    public async Task<IReadOnlyList<QuoteRevisionChange>> CompareAsync(ActorContext actor, Guid quoteId, Guid leftId, Guid rightId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var transaction = await db.Database.BeginTransactionAsync(token);
        await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        var left = await Revision(db, quoteId, leftId, token); var right = await Revision(db, quoteId, rightId, token);
        using var l = JsonDocument.Parse(left.ProposalJson); using var r = JsonDocument.Parse(right.ProposalJson);
        var changes = QuoteRevisionDiff.Compare(l.RootElement, r.RootElement).ToList();
        foreach (var (path, before, after) in new[] { ("/clientId", left.ClientId, right.ClientId), ("/relationshipId", left.RelationshipId, right.RelationshipId) })
            if (before != after) changes.Add(new("changed", path, null, new(path, JsonSerializer.Serialize(before)), new(path, JsonSerializer.Serialize(after))));
        await transaction.CommitAsync(token); return changes;
    }

    public async Task<QuoteCloneTerms> CloneTermsAsync(ActorContext actor, Guid quoteId, Guid sourceRevisionId, Guid relationshipId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var transaction = await db.Database.BeginTransactionAsync(token);
        var context = await CloneContext(db, actor, quoteId, sourceRevisionId, relationshipId, token);
        Cloneable(context.Source.Quote);
        var terms = context.Destination.Terms;
        var result = new QuoteCloneTerms(sourceRevisionId, relationshipId, terms.Id, terms.Version, terms.EffectiveFrom, terms.Id != context.Revision.AgencyTermsVersionId);
        await transaction.CommitAsync(token); return result;
    }

    public Task<CommandOutcome> CloneAsync(ActorContext actor, Guid quoteId, byte[] version, Guid sourceRevisionId, Guid relationshipId,
        Guid? confirmedTermsId, string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        Version(version); reason = QuoteEvidenceRules.Reason(reason);
        CloneScope? authorized = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/clone", key, correlationId),
            new { quoteId, version = Convert.ToBase64String(version), sourceRevisionId, relationshipId, confirmedTermsId, reason }, "quote.cloned",
            async (db, ct) => { authorized = await CloneContext(db, actor, quoteId, sourceRevisionId, relationshipId, ct); },
            async (db, ct) =>
            {
                var context = authorized ?? throw new InvalidOperationException("Clone requires held authority.");
                Cloneable(context.Source.Quote); Current(context.Source.Quote, version, progressed: true);
                if ((context.Revision.AgencyTermsVersionId != context.Destination.Terms.Id || confirmedTermsId is not null) && confirmedTermsId != context.Destination.Terms.Id)
                    throw new QuoteOperationException(409, "quote-clone-terms-confirmation-required");
                var prepared = QuoteLifecycleRules.Clone(context.Revision.ProposalJson, context.Destination.Product.Code,
                    QuoteService.Pins(context.Revision), context.Destination.Pins);
                var now = time.GetUtcNow();
                var clone = new Quote { AgencyId = context.Target.Agency.Id, ClientId = context.Target.Client.Id, RelationshipId = context.Target.Relationship.Id,
                    ProductId = context.Destination.Product.Id, ClonedFromQuoteRevisionId = sourceRevisionId,
                    CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
                db.Add(clone); await db.SaveChangesAsync(ct);
                await QuoteService.Append(db, clone, context.Destination, prepared.Capture, 1, reason, actor.UserId, now, ct);
                await QuoteMatching.AttachOrReviewAsync(db, clone, context.Target, null, actor.UserId, now, ct);
                db.Add(new QuoteActivity { QuoteId = clone.Id, RevisionId = clone.CurrentRevisionId!.Value, ActorId = actor.UserId, CreatedBy = actor.UserId,
                    CreatedAt = now, OccurredAt = now, EventType = "quote.cloned" });
                await db.SaveChangesAsync(ct); return Receipt(clone, 201);
            }, token);
    }

    public Task<CommandOutcome> WithdrawAsync(ActorContext actor, Guid quoteId, byte[] version, string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        Version(version); reason = QuoteEvidenceRules.Reason(reason); OwnedQuoteScope? owned = null; QuoteRevision? revision = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/withdraw", key, correlationId),
            new { quoteId, version = Convert.ToBase64String(version), reason }, "quote.withdrawn",
            async (db, ct) =>
            {
                owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Capture, ct);
                revision = await QuoteService.CurrentRevision(db, owned.Quote, ct);
                var eligible = await QuoteCaptureEligibility.ResolveAsync(db, owned.Scope, revision.ProductVersionId, time.GetUtcNow(), revision.AgencyTermsVersionId, ct);
                QuoteRules.EnsureRetainedPins(QuoteService.Pins(revision), eligible.Pins);
            },
            async (db, ct) =>
            {
                var quote = owned!.Quote;
                if (quote.State is "bound" or "withdrawn") throw new QuoteInputException("quote-capture-closed");
                Current(quote, version, progressed: quote.CurrentUnderwritingCycleId is not null); var now = time.GetUtcNow();
                await QuoteUnderwritingLifecycle.SupersedeAsync(db, quote, reason, now, ct); db.Attach(quote); quote.CurrentUnderwritingCycleId = null;
                quote.State = "withdrawn"; quote.CaptureClosedAt = now; quote.CaptureClosedReason = reason;
                db.Add(new QuoteActivity { QuoteId = quoteId, RevisionId = revision!.Id, ActorId = actor.UserId, CreatedBy = actor.UserId,
                    CreatedAt = now, OccurredAt = now, EventType = "quote.withdrawn" });
                db.Add(new ClientActivity { ClientId = quote.ClientId, RelationshipId = quote.RelationshipId, ActorId = actor.UserId, CreatedBy = actor.UserId,
                    CreatedAt = now, OccurredAt = now, EventType = "quote.withdrawn", RecordId = quoteId, RecordKind = "quote" });
                await db.SaveChangesAsync(ct); return Receipt(quote, 200);
            }, token);
    }

    private async Task<CloneScope> CloneContext(
        BackOfficeDbContext db, ActorContext actor, Guid quoteId, Guid revisionId, Guid relationshipId, CancellationToken token)
    {
        // Acquire both agencies in a deterministic order before quote/client locks.
        // Hints are rechecked by the normal held scope readers below.
        var sourceAgency = await db.Set<Quote>().Where(x => x.Id == quoteId).Select(x => (Guid?)x.AgencyId).SingleOrDefaultAsync(token);
        var targetAgency = await db.Set<ClientAgencyRelationship>().Where(x => x.Id == relationshipId).Select(x => (Guid?)x.AgencyId).SingleOrDefaultAsync(token);
        if (sourceAgency is null || targetAgency is null) throw new QuoteOperationException(404, "quote-context-not-found");
        foreach (var id in new[] { sourceAgency.Value, targetAgency.Value }.Distinct().Order())
            await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={id}").AsNoTracking().SingleAsync(token);
        var source = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Capture, token);
        var target = await QuoteScope.ForRelationshipAsync(db, actor, relationshipId, QuoteAccess.Capture, token);
        if (source.Quote.AgencyId != sourceAgency || target.Agency.Id != targetAgency) throw new QuoteOperationException(409, "quote-context-changed");
        var revision = await Revision(db, quoteId, revisionId, token);
        var eligibleSource = await QuoteCaptureEligibility.ResolveAsync(db, source.Scope, revision.ProductVersionId, time.GetUtcNow(), revision.AgencyTermsVersionId, token);
        QuoteRules.EnsureRetainedPins(QuoteService.Pins(revision), eligibleSource.Pins);
        var destination = await QuoteCaptureEligibility.ResolveAsync(db, target, revision.ProductVersionId, time.GetUtcNow(), token: token);
        return new(source, revision, target, destination);
    }

    private static async Task<QuoteRevision> Revision(BackOfficeDbContext db, Guid quoteId, Guid revisionId, CancellationToken token) =>
        await db.Set<QuoteRevision>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == revisionId && x.QuoteId == quoteId, token)
            ?? throw new QuoteOperationException(404, "quote-revision-not-found");
    private static async Task<QuoteRevisionView[]> Views(BackOfficeDbContext db, QuoteRevision[] rows, CancellationToken token)
    {
        var users = rows.Select(x => x.SavedBy).Distinct().ToArray();
        var labels = await db.Set<StaffUser>().Where(x => users.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, token);
        return rows.Select(x =>
        {
            using var proposal = JsonDocument.Parse(x.ProposalJson);
            return new QuoteRevisionView(x.Id, x.QuoteId, x.ClientId, x.RelationshipId, x.Number, x.ProductVersionId, x.AgencyTermsVersionId, x.QuestionSetVersion,
                QuoteService.Pins(x).ReferenceVersion, proposal.RootElement.Clone(), Convert.ToHexStringLower(x.ContentHash), x.SavedAt, labels[x.SavedBy], x.Reason);
        }).ToArray();
    }
    private static void Version(byte[] version) { if (version.Length != 8) throw new QuoteOperationException(400, "invalid-quote-version"); }
    private static void Cloneable(Quote quote)
    {
        if (quote.State == "draft") QuoteRules.EnsureEditable(quote.State, quote.CaptureClosedAt);
        else if (quote.State is not ("rating-pending" or "rated" or "referred" or "approved" or "sent" or "accepted" or "declined" or "bound"))
            throw new QuoteInputException("quote-capture-closed");
    }
    private static void Current(Quote quote, byte[] version, bool progressed = false)
    {
        if (!progressed) QuoteRules.EnsureEditable(quote.State, quote.CaptureClosedAt);
        if (!CryptographicOperations.FixedTimeEquals(quote.RowVersion, version)) throw new QuoteOperationException(412, "stale-quote");
    }
    private static CommandOutcome Receipt(Quote quote, int status) => new(quote.Id, status, JsonSerializer.Serialize(new { id = quote.Id }), Etag: "\"" + Convert.ToBase64String(quote.RowVersion) + "\"");
}
