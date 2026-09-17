using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed record QuoteAcceptanceInput(Guid CycleId, Guid RatingId, Guid TermsVersionId, string TermsHash, string AssuranceHash,
    string AccepterLabel, DateTimeOffset AcceptedAt, string Channel, Guid EvidenceAssociationId);

public sealed class QuoteAcceptanceService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);
    public Task<CommandOutcome> RecordAsync(ActorContext actor, Guid quoteId, byte[] version, QuoteAcceptanceInput input, string key, Guid correlationId, CancellationToken token = default)
    {
        if (!QuoteTermsRules.ValidAcceptanceIdentity(input.AccepterLabel, input.Channel) || !ReferralRules.Hash(input.TermsHash) || !ReferralRules.Hash(input.AssuranceHash))
            throw new QuoteOperationException(422, "quote-acceptance-invalid");
        input = input with { AccepterLabel = input.AccepterLabel.Trim(), AcceptedAt = input.AcceptedAt.ToUniversalTime() };
        UnderwritingDecisionContext? held = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/acceptances", key, correlationId),
            new { quoteId, version = Convert.ToBase64String(version), input }, "quote.acceptance-recorded",
            async (db, ct) => { held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, input.CycleId, "quote-acceptance", time.GetUtcNow(), false, ct); },
            async (db, ct) => {
                var now = time.GetUtcNow(); held!.Current(version, now, currentPrice: true);
                var terms = await QuoteTermsService.CurrentTerms(db, held, input.TermsVersionId, now, ct);
                if (held.Rating!.Id != input.RatingId || terms.TermsHash != input.TermsHash ||
                    await UnderwritingEvidenceService.Assurance(db, held.Cycle, held.Revision, ct) != input.AssuranceHash)
                    throw new QuoteOperationException(412, "quote-acceptance-context-stale");
                await QuoteTermsService.Ready(db, held, now, signing: true, ct);
                var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == held.Cycle.CurrentDeliveryId && x.TermsVersionId == terms.Id && x.CycleId == held.Cycle.Id && x.QuoteId == quoteId, ct);
                if (delivery is null || !QuoteTermsRules.AcceptanceWindow(delivery.State, delivery.CompletedAt, input.AcceptedAt, now, held.Rating.ExpiresAt))
                    throw new QuoteOperationException(409, "quote-delivered-terms-required");
                var recipients = JsonSerializer.Deserialize<QuoteTermsRecipient[]>(delivery.RecipientSnapshotJson, QuoteRatingService.Json)!;
                var currentRecipients = await QuoteTermsService.Recipients(db, held, recipients.Select(x => x.Id).ToArray(), ct);
                if (!recipients.SequenceEqual(currentRecipients)) throw new QuoteOperationException(409, "quote-recipient-changed");
                var proof = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.EvidenceAssociationId && x.CycleId == input.CycleId && x.QuoteId == quoteId, ct);
                var purpose = (await UnderwritingEvidenceService.Requirements(db, held.Cycle, held.Revision, held.Input, ct)).Single(x => x.Code == "acceptance-proof" && x.ConditionId == null && x.TermsVersionId == terms.Id);
                if (proof is null || proof.RequirementCode != "acceptance-proof" || proof.TermsVersionId != terms.Id || proof.WithdrawnEventId is not null ||
                    proof.LatestReviewId is null || proof.InputFingerprint != purpose.InputFingerprint ||
                    !await db.Set<UnderwritingEvidenceEvent>().AnyAsync(x => x.Id == proof.LatestReviewId && x.AssociationId == proof.Id && x.Outcome == "accepted", ct) ||
                    !await db.Set<QuoteEvidenceFile>().AnyAsync(x => x.Id == proof.FileId && x.ScreeningState == "accepted", ct)) throw new QuoteOperationException(409, "quote-acceptance-proof-required");
                var row = new QuoteAcceptance { QuoteId = quoteId, CycleId = input.CycleId, RatingId = input.RatingId, TermsVersionId = terms.Id, DeliveryId = delivery.Id,
                    TermsHash = input.TermsHash, AssuranceHash = input.AssuranceHash, AccepterLabel = input.AccepterLabel, AcceptedAt = input.AcceptedAt, Channel = input.Channel,
                    EvidenceAssociationId = proof.Id, EvidenceReviewId = proof.LatestReviewId.Value, RecordedBy = actor.UserId, RecordedAt = now, CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(row); await db.SaveChangesAsync(ct);
                var cycle = await QuoteTermsService.TrackedCycle(db, held, ct); cycle.CurrentAcceptanceId = row.Id;
                QuoteTermsService.SetState(db, held, "accepted");
                return await held.Receipt(db, row.Id, 201, "quote.acceptance-recorded", now, ct);
            }, token);
    }
}
