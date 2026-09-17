using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record QuoteIssueInput(Guid CycleId, Guid RatingId, Guid AcceptanceId, string TermsHash, string AssuranceHash, string Reason);

public sealed class QuoteIssueService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);
    public Task<CommandOutcome> IssueAsync(ActorContext actor, Guid quoteId, byte[] version, QuoteIssueInput input, string key, Guid correlationId, CancellationToken token = default)
    {
        if (input.CycleId == Guid.Empty || input.RatingId == Guid.Empty || input.AcceptanceId == Guid.Empty || !ReferralRules.Hash(input.TermsHash) || !ReferralRules.Hash(input.AssuranceHash))
            throw new QuoteOperationException(422, "policy-issue-input-invalid");
        input = input with { Reason = QuoteRatingService.Reason(input.Reason, 1000) };
        UnderwritingDecisionContext? held = null; EffectiveUnderwritingGrant? grant = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/issue", key, correlationId),
            new { quoteId, version = Convert.ToBase64String(version), input }, "policy.issued",
            async (db, ct) => {
                held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, input.CycleId, "policy-issue-within-authority", time.GetUtcNow(), true, ct);
                // Current multidimensional authority precedes receipt lookup, including
                // replay after the quote has already been bound.
                grant = await Authority(db, held, time.GetUtcNow(), ct);
            },
            async (db, ct) => {
                var now = time.GetUtcNow(); held!.Current(version, now, currentPrice: true);
                if (held.Owned.Quote.State != "accepted" || held.Owned.Quote.BoundPolicyId is not null ||
                    await db.Set<Policy>().AnyAsync(x => x.SourceQuoteId == quoteId, ct)) throw new QuoteOperationException(409, "quote-issue-state");
                await Accepted(db, held, input, now, ct);
                var templates = await PolicyIssueWriter.Templates(db, held.Cycle.ProductId, now, ct);
                var result = await PolicyIssueWriter.Write(db, held, grant!, input.Reason, templates, now, correlationId, ct);
                var quote = held.Owned.Quote; if (db.Entry(quote).State == EntityState.Detached) db.Attach(quote);
                quote.BoundPolicyId = result.Policy.Id; quote.State = "bound";
                quote.CaptureClosedAt ??= now; quote.CaptureClosedReason = "policy-issued";
                db.Add(new ClientActivity { ClientId = quote.ClientId, RelationshipId = quote.RelationshipId, RecordKind = "quote", RecordId = quote.Id,
                    EventType = "policy.issued", ActorId = actor.UserId, CreatedBy = actor.UserId, CreatedAt = now, OccurredAt = now });
                var receipt = await held.Receipt(db, result.Policy.Id, 201, "policy.issued", now, ct);
                return receipt with { Body = JsonSerializer.Serialize(new { policyId = result.Policy.Id, policyReference = result.Policy.Reference, quoteId,
                    quoteEtag = receipt.Etag, termId = result.Term.Id, versionId = result.Version.Id, transactionId = result.Transaction.Id,
                    obligationId = result.Obligation.Id, documentRequestIds = result.Documents }, QuoteRatingService.Json) };
            }, token);
    }

    internal static async Task<EffectiveUnderwritingGrant> Authority(BackOfficeDbContext db, UnderwritingDecisionContext held, DateTimeOffset now, CancellationToken token)
    {
        using var proposal = JsonDocument.Parse(held.Revision.ProposalJson);
        var conditions = (await UnderwritingEvidenceService.ActiveConditions(db, held.Cycle.Id, token)).Select(x => QuoteReferralService.Parse(x.DefinitionJson, proposal.RootElement)).ToArray();
        foreach (var grant in held.Grants.OrderBy(x => x.Version.Id))
            if (await CapacityAuthority.Allows(db, held, grant.Definition, conditions, now, token)) return grant;
        throw new QuoteOperationException(403, "policy-issue-authority-required");
    }

    internal static async Task Accepted(BackOfficeDbContext db, UnderwritingDecisionContext held, QuoteIssueInput input, DateTimeOffset now, CancellationToken token)
    {
        if (held.Rating?.Id != input.RatingId || held.Cycle.CurrentAcceptanceId != input.AcceptanceId ||
            held.Cycle.CurrentTermsVersionId is not Guid termsId) throw new QuoteOperationException(412, "policy-acceptance-stale");
        await QuoteTermsService.Ready(db, held, now, true, token);
        var terms = await QuoteTermsService.CurrentTerms(db, held, termsId, now, token);
        var assurance = await UnderwritingEvidenceService.Assurance(db, held.Cycle, held.Revision, token);
        var accepted = await db.Set<QuoteAcceptance>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.AcceptanceId && x.CycleId == held.Cycle.Id && x.QuoteId == held.Cycle.QuoteId, token);
        if (accepted is null || accepted.RatingId != input.RatingId || accepted.TermsVersionId != terms.Id || accepted.TermsHash != terms.TermsHash || input.TermsHash != terms.TermsHash ||
            accepted.AssuranceHash != assurance || input.AssuranceHash != assurance || accepted.DeliveryId != held.Cycle.CurrentDeliveryId)
            throw new QuoteOperationException(412, "policy-acceptance-stale");
        var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleAsync(x => x.Id == accepted.DeliveryId && x.TermsVersionId == terms.Id, token);
        if (!QuoteTermsRules.AcceptanceWindow(delivery.State, delivery.CompletedAt, accepted.AcceptedAt, now, held.Rating.ExpiresAt)) throw new QuoteOperationException(409, "policy-delivered-acceptance-required");
        var recipients = JsonSerializer.Deserialize<QuoteTermsRecipient[]>(delivery.RecipientSnapshotJson, QuoteRatingService.Json)!;
        var actualRecipients = await QuoteTermsService.Recipients(db, held, recipients.Select(x => x.Id).ToArray(), token);
        if (!recipients.SequenceEqual(actualRecipients)) throw new QuoteOperationException(409, "quote-recipient-changed");
        var proof = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == accepted.EvidenceAssociationId && x.CycleId == held.Cycle.Id && x.QuoteId == held.Cycle.QuoteId, token);
        var requirement = (await UnderwritingEvidenceService.Requirements(db, held.Cycle, held.Revision, held.Input, token)).Single(x => x.Code == "acceptance-proof" && x.ConditionId == null && x.TermsVersionId == terms.Id);
        if (proof is null || proof.WithdrawnEventId is not null || proof.LatestReviewId != accepted.EvidenceReviewId || proof.InputFingerprint != requirement.InputFingerprint ||
            proof.TermsVersionId != terms.Id || proof.RequirementCode != "acceptance-proof" || !requirement.Satisfied ||
            !await db.Set<UnderwritingEvidenceEvent>().AnyAsync(x => x.Id == accepted.EvidenceReviewId && x.AssociationId == proof.Id && x.Outcome == "accepted", token) ||
            !await db.Set<QuoteEvidenceFile>().AnyAsync(x => x.Id == proof.FileId && x.ScreeningState == "accepted", token)) throw new QuoteOperationException(409, "policy-acceptance-proof-required");
    }
}
