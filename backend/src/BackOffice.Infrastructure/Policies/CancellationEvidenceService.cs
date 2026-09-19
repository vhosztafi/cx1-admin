using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record CancellationEvidenceItem(Guid Id, Guid RevisionId, Guid FileId, string FileName, string Purpose,
    DateTimeOffset? NoticeDeliveredAt, Guid? ReviewId, string ReviewState, string? ReviewReason);
public sealed record CancellationEvidencePage(Guid DraftId, string DraftEtag, IReadOnlyList<CancellationEvidenceItem> Items);

public sealed partial class CancellationReviewService
{
    public Task<CommandOutcome> UploadAsync(ActorContext actor, Guid draftId, byte[] version, Guid leaseToken,
        string purpose, DateTimeOffset? noticeDeliveredAt, string fileName, string contentType, byte[] content,
        string key, Guid correlation, CancellationToken token = default)
    {
        if (!CancellationDecisionRules.Reasons.SelectMany(x => x.EvidencePurposes).Contains(purpose) ||
            noticeDeliveredAt is { } delivered && (purpose != "cancellation-notice" || delivered.Offset != TimeSpan.Zero || delivered > time.GetUtcNow()))
            throw new QuoteOperationException(422, "cancellation-evidence-invalid");
        var file = QuoteEvidenceRules.File(fileName, contentType, content, maximumNameLength: 200);
        return Mutate(actor, draftId, version, leaseToken, "cancellation-evidence/uploads",
            new { purpose, noticeDeliveredAt, file.FileName, file.ContentType, file.Sha256, length = file.Content.Length }, key, correlation,
            "underwriting-evidence-write", async (db, held, ct) =>
            {
                if (await db.Set<CancellationEvidence>().CountAsync(x => x.DraftId == draftId && x.RevisionId == held.Draft.CurrentRevisionId, ct) >= 100)
                    throw new QuoteOperationException(409, "cancellation-evidence-limit");
                var stored = new ServicingEvidenceFile { DraftId = draftId, FileName = file.FileName, ContentType = file.ContentType,
                    Content = file.Content, ByteLength = file.Content.Length, Sha256 = file.Sha256, CreatedAt = time.GetUtcNow(), CreatedBy = held.Source.Scope.Actor.UserId };
                db.Add(stored); await db.SaveChangesAsync(ct);
                var row = new CancellationEvidence { DraftId = draftId, RevisionId = held.Draft.CurrentRevisionId!.Value, FileId = stored.Id,
                    Purpose = purpose, NoticeDeliveredAt = noticeDeliveredAt, CreatedAt = time.GetUtcNow(), CreatedBy = held.Source.Scope.Actor.UserId };
                db.Add(row); return row.Id;
            }, token);
    }

    public Task<CommandOutcome> ReviewEvidenceAsync(ActorContext actor, Guid draftId, Guid evidenceId, byte[] version, Guid leaseToken,
        string outcome, string reason, string key, Guid correlation, CancellationToken token = default)
    {
        reason = Reason(reason);
        if (evidenceId == Guid.Empty || outcome is not ("accepted" or "rejected")) throw new QuoteOperationException(422, "cancellation-evidence-review-invalid");
        EffectiveUnderwritingGrant? grant = null;
        return Mutate(actor, draftId, version, leaseToken, $"cancellation-evidence/{evidenceId:D}/reviews", new { evidenceId, outcome, reason }, key, correlation,
            "underwriting-evidence-review", async (db, held, ct) =>
            {
                if (!await db.Set<CancellationEvidence>().AnyAsync(x => x.Id == evidenceId && x.DraftId == draftId && x.RevisionId == held.Draft.CurrentRevisionId, ct))
                    throw new QuoteOperationException(404, "cancellation-evidence-not-found");
                var row = new CancellationEvidenceReview { DraftId = draftId, RevisionId = held.Draft.CurrentRevisionId!.Value, EvidenceId = evidenceId,
                    Sequence = checked((await db.Set<CancellationEvidenceReview>().Where(x => x.EvidenceId == evidenceId).MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1),
                    Outcome = outcome, Reason = reason, AuthorityGrantId = grant!.Grant.Id, AuthorityVersionId = grant.Version.Id,
                    CreatedAt = time.GetUtcNow(), CreatedBy = held.Source.Scope.Actor.UserId };
                db.Add(row); return row.Id;
            }, token, (db, held, ct) =>
            {
                grant = held.Grants.FirstOrDefault() ?? throw new QuoteOperationException(403, "cancellation-evidence-review-authority-required");
                return Task.CompletedTask;
            });
    }

    public async Task<CancellationEvidencePage> EvidenceAsync(ActorContext actor, Guid draftId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var held = await Hold(db, actor, draftId, "policy-read", false, token);
        var rows = await (from e in db.Set<CancellationEvidence>().AsNoTracking() join f in db.Set<ServicingEvidenceFile>() on e.FileId equals f.Id
            where e.DraftId == draftId && e.RevisionId == held.Draft.CurrentRevisionId orderby e.CreatedAt, e.Id select new { Evidence = e, f.FileName }).Take(101).ToArrayAsync(token);
        if (rows.Length > 100) throw new QuoteOperationException(409, "cancellation-evidence-limit");
        var reviews = await db.Set<CancellationEvidenceReview>().AsNoTracking().Where(x => x.DraftId == draftId && x.RevisionId == held.Draft.CurrentRevisionId)
            .OrderBy(x => x.Sequence).ToArrayAsync(token);
        var items = new List<CancellationEvidenceItem>();
        foreach (var row in rows)
        {
            var e = row.Evidence; var review = reviews.LastOrDefault(x => x.EvidenceId == e.Id);
            var state = review?.Outcome ?? "unreviewed";
            if (review?.Outcome == "accepted" && !await CurrentGrant(db, held, review.AuthorityGrantId, review.AuthorityVersionId, review.CreatedBy!.Value, token)) state = "authority-expired";
            items.Add(new(e.Id, e.RevisionId, e.FileId, row.FileName, e.Purpose, e.NoticeDeliveredAt, review?.Id, state, review?.Reason));
        }
        await tx.CommitAsync(token); return new(draftId, Etag(held.Draft), items.AsReadOnly());
    }
}
