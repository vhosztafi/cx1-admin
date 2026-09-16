using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed partial class UnderwritingEvidenceService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);

    public Task<CommandOutcome> UploadAsync(ActorContext actor, Guid quoteId, byte[] version, string fileName, string contentType,
        byte[] content, string key, Guid correlationId, CancellationToken token = default)
    {
        var file = QuoteEvidenceRules.File(fileName, contentType, content); UnderwritingDecisionContext? held = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/underwriting/evidence-files", key, correlationId),
            new { quoteId, version = Convert.ToBase64String(version), file.FileName, file.ContentType, file.Sha256, length = file.Content.Length }, "underwriting.evidence-uploaded",
            async (db, ct) => { held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, null, "underwriting-evidence-write", time.GetUtcNow(), false, ct); },
            async (db, ct) =>
            {
                var now = time.GetUtcNow(); held!.Current(version, now);
                var row = new QuoteEvidenceFile { QuoteId = quoteId, FileName = file.FileName, ContentType = file.ContentType, Content = file.Content,
                    Sha256 = file.Sha256, ByteLength = file.Content.Length, CreatedBy = actor.UserId, CreatedAt = now };
                db.Add(row); return await held.Receipt(db, row.Id, 201, "underwriting.evidence-uploaded", now, ct);
            }, token);
    }

    public Task<CommandOutcome> AttachAsync(ActorContext actor, Guid quoteId, Guid cycleId, byte[] version, Guid fileId,
        string requirementCode, Guid? riskItemId, Guid? conditionId, Guid? termsVersionId, string fingerprint, string reason,
        string key, Guid correlationId, CancellationToken token = default)
    {
        reason = QuoteRatingService.Reason(reason); if (!ReferralRules.Hash(fingerprint) || fileId == Guid.Empty) throw new QuoteOperationException(422, "evidence-input-invalid");
        UnderwritingDecisionContext? held = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/underwriting/evidence", key, correlationId),
            new { quoteId, cycleId, version = Convert.ToBase64String(version), fileId, requirementCode, riskItemId, conditionId, termsVersionId, fingerprint, reason }, "underwriting.evidence-attached",
            async (db, ct) =>
            {
                held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, cycleId, "underwriting-evidence-write", time.GetUtcNow(), false, ct);
                if (!await db.Set<QuoteEvidenceFile>().AnyAsync(x => x.Id == fileId && x.QuoteId == quoteId && x.ScreeningState == "accepted", ct)) throw new QuoteOperationException(404, "evidence-file-not-found");
            },
            async (db, ct) =>
            {
                var now = time.GetUtcNow(); held!.Current(version, now);
                var required = (await Requirements(db, held.Cycle, held.Revision, held.Input, ct)).SingleOrDefault(x => x.Code == requirementCode && x.RiskItemId == riskItemId && x.ConditionId == conditionId && x.TermsVersionId == termsVersionId)
                    ?? throw new QuoteOperationException(422, "evidence-purpose-inapplicable");
                if (required.InputFingerprint != fingerprint) throw new QuoteOperationException(412, "stale-evidence-input");
                var row = new UnderwritingEvidenceAssociation { QuoteId = quoteId, CycleId = cycleId, FileId = fileId, RequirementCode = requirementCode, RiskItemId = riskItemId,
                    ConditionId = conditionId, TermsVersionId = termsVersionId, InputFingerprint = fingerprint, Reason = reason, CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
                db.Add(row); await db.SaveChangesAsync(ct); await QuoteReferralService.RefreshState(db, held, ct);
                return await held.Receipt(db, row.Id, 201, "underwriting.evidence-attached", now, ct);
            }, token);
    }

    public Task<CommandOutcome> ReviewAsync(ActorContext actor, Guid quoteId, Guid cycleId, Guid associationId, byte[] version,
        byte[] associationVersion, string outcome, string expectedFingerprint, string reason, string key, Guid correlationId, CancellationToken token = default) =>
        Event(actor, quoteId, cycleId, associationId, version, associationVersion, "review", outcome, expectedFingerprint, reason, key, correlationId, token);
    public Task<CommandOutcome> WithdrawAsync(ActorContext actor, Guid quoteId, Guid cycleId, Guid associationId, byte[] version,
        byte[] associationVersion, string reason, string key, Guid correlationId, CancellationToken token = default) =>
        Event(actor, quoteId, cycleId, associationId, version, associationVersion, "withdrawal", null, null, reason, key, correlationId, token);

    private Task<CommandOutcome> Event(ActorContext actor, Guid quoteId, Guid cycleId, Guid associationId, byte[] version, byte[] associationVersion,
        string kind, string? outcome, string? expectedFingerprint, string reason, string key, Guid correlationId, CancellationToken token)
    {
        reason = QuoteRatingService.Reason(reason);
        if (kind == "review" && (outcome is not ("accepted" or "rejected") || expectedFingerprint is null || !ReferralRules.Hash(expectedFingerprint))) throw new QuoteOperationException(422, "evidence-review-invalid");
        UnderwritingDecisionContext? held = null; UnderwritingEvidenceAssociation? association = null; EffectiveUnderwritingGrant? grant = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/underwriting/evidence/{associationId:D}/" + (kind == "review" ? "reviews" : "withdraw"), key, correlationId),
            new { quoteId, cycleId, associationId, version = Convert.ToBase64String(version), associationVersion = Convert.ToBase64String(associationVersion), outcome, expectedFingerprint, reason }, "underwriting.evidence-" + kind,
            async (db, ct) =>
            {
                held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, cycleId, kind == "review" ? "underwriting-evidence-review" : "underwriting-evidence-write", time.GetUtcNow(), kind == "review", ct);
                association = await db.Set<UnderwritingEvidenceAssociation>().FromSqlInterpolated($"SELECT * FROM UnderwritingEvidenceAssociation WITH(UPDLOCK,HOLDLOCK) WHERE Id={associationId} AND QuoteId={quoteId} AND CycleId={cycleId}").SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "underwriting-evidence-not-found");
                if (kind == "review")
                {
                    grant = held.Grants.FirstOrDefault(x => association.RequirementCode != "trading-history" || x.Definition.GetProperty("limits").GetProperty("reviewTradingHistory").GetBoolean())
                        ?? throw new QuoteOperationException(403, "underwriting-proof-authority-required");
                }
            },
            async (db, ct) =>
            {
                var now = time.GetUtcNow(); held!.Current(version, now);
                UnderwritingDecisionContext.CheckVersion(association!.RowVersion, associationVersion, "stale-underwriting-evidence");
                if (association.WithdrawnEventId is not null) throw new QuoteOperationException(409, "underwriting-evidence-withdrawn");
                if (kind == "review")
                {
                    var purpose = (await Requirements(db, held.Cycle, held.Revision, held.Input, ct)).SingleOrDefault(x => x.Code == association.RequirementCode && x.RiskItemId == association.RiskItemId && x.ConditionId == association.ConditionId && x.TermsVersionId == association.TermsVersionId);
                    if (purpose is null || purpose.InputFingerprint != association.InputFingerprint || expectedFingerprint != association.InputFingerprint) throw new QuoteOperationException(412, "stale-evidence-input");
                    if (!await db.Set<QuoteEvidenceFile>().AnyAsync(x => x.Id == association.FileId && x.QuoteId == quoteId && x.ScreeningState == "accepted", ct)) throw new QuoteOperationException(409, "evidence-screening-required");
                }
                var row = new UnderwritingEvidenceEvent { AssociationId = associationId, QuoteId = quoteId, CycleId = cycleId,
                    Sequence = checked((await db.Set<UnderwritingEvidenceEvent>().Where(x => x.AssociationId == associationId).MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1),
                    Kind = kind, Outcome = outcome, Reason = reason, ActorId = actor.UserId, AuthorityVersionId = grant?.Version.Id,
                    InputFingerprint = association.InputFingerprint, RecordedAt = now, CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(row); await db.SaveChangesAsync(ct);
                if (kind == "review") association.LatestReviewId = row.Id; else association.WithdrawnEventId = row.Id;
                await db.SaveChangesAsync(ct);
                await QuoteReferralService.RefreshState(db, held, ct);
                return await held.Receipt(db, row.Id, 200, "underwriting.evidence-" + kind, now, ct);
            }, token);
    }
}
