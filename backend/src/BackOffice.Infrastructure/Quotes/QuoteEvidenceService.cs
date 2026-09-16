using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public sealed class QuoteEvidenceService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);

    public async Task<QuoteEvidenceSnapshot> ReadAsync(ActorContext actor, Guid quoteId, Guid? revisionId = null, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        var revision = revisionId is null ? await QuoteService.CurrentRevision(db, owned.Quote, token) :
            await db.Set<QuoteRevision>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == revisionId && x.QuoteId == quoteId, token)
                ?? throw new QuoteOperationException(404, "evidence-revision-not-found");
        var result = await QuoteEvidenceReadModel.AssessAsync(db, revision, token);
        await transaction.CommitAsync(token); return result;
    }

    public async Task<QuoteEvidenceFileView[]> FilesAsync(ActorContext actor, Guid quoteId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        var files = await db.Set<QuoteEvidenceFile>().AsNoTracking().Where(x => x.QuoteId == quoteId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(100)
            .Select(x => new QuoteEvidenceFileView(x.Id, x.QuoteId, x.FileName, x.ContentType, x.ByteLength, x.Sha256,
                x.CreatedAt, x.ScreeningState, x.ScreeningMethod)).ToArrayAsync(token);
        await transaction.CommitAsync(token); return files;
    }

    public Task<CommandOutcome> UploadAsync(ActorContext actor, Guid quoteId, byte[] version, string fileName,
        string contentType, byte[] content, string key, Guid correlationId, CancellationToken token = default)
    {
        Version(version); var file = QuoteEvidenceRules.File(fileName, contentType, content);
        OwnedQuoteScope? owned = null; QuoteRevision? revision = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/evidence-files", key, correlationId),
            new { quoteId, version = Convert.ToBase64String(version), file.FileName, file.ContentType, file.Sha256, length = file.Content.Length },
            "quote.evidence-file-uploaded", async (db, ct) => { (owned, revision) = await Writable(db, actor, quoteId, ct); },
            async (db, ct) =>
            {
                Current(owned!.Quote, version); var now = time.GetUtcNow();
                var row = new QuoteEvidenceFile { QuoteId = quoteId, FileName = file.FileName, ContentType = file.ContentType,
                    Content = file.Content, Sha256 = file.Sha256, ByteLength = file.Content.Length, CreatedBy = actor.UserId, CreatedAt = now };
                db.Add(row); Activity(db, quoteId, revision!.Id, actor.UserId, now, "quote.evidence-file-uploaded");
                await db.SaveChangesAsync(ct); return Receipt(row.Id, 201, owned.Quote.RowVersion);
            }, token);
    }

    public Task<CommandOutcome> AttachAsync(ActorContext actor, Guid quoteId, byte[] version, Guid revisionId,
        string requirementCode, Guid? riskItemId, Guid fileId, string fingerprint, string reason,
        string key, Guid correlationId, CancellationToken token = default)
    {
        Version(version); reason = QuoteEvidenceRules.Reason(reason);
        if (revisionId == Guid.Empty || fileId == Guid.Empty) throw new QuoteOperationException(422, "evidence-identity-required");
        OwnedQuoteScope? owned = null; QuoteRevision? revision = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/evidence", key, correlationId),
            new { quoteId, version = Convert.ToBase64String(version), revisionId, requirementCode, riskItemId, fileId, fingerprint, reason },
            "quote.evidence-attached", async (db, ct) =>
            {
                (owned, revision) = await Writable(db, actor, quoteId, ct);
                if (!await db.Set<QuoteEvidenceFile>().AnyAsync(x => x.Id == fileId && x.QuoteId == quoteId && x.ScreeningState == "accepted", ct))
                    throw new QuoteOperationException(404, "evidence-file-not-found");
            },
            async (db, ct) =>
            {
                Current(owned!.Quote, version);
                if (revision!.Id != revisionId) throw new QuoteOperationException(412, "stale-evidence-input");
                using var proposal = JsonDocument.Parse(revision.ProposalJson);
                var input = QuoteEvidenceRules.Prepare(proposal.RootElement, requirementCode, riskItemId, QuoteService.Pins(revision));
                if (!QuoteEvidenceRules.Matches(input, fingerprint)) throw new QuoteOperationException(412, "stale-evidence-input");
                var now = time.GetUtcNow();
                var row = new QuoteCaptureEvidence { QuoteId = quoteId, RevisionId = revisionId, RequirementCode = requirementCode,
                    RiskItemId = riskItemId, FileId = fileId, InputFingerprint = input.InputFingerprint, Reason = reason,
                    ActorId = actor.UserId, CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
                db.Add(row); Activity(db, quoteId, revisionId, actor.UserId, now, "quote.evidence-attached");
                await db.SaveChangesAsync(ct); return Receipt(row.Id, 201, row.RowVersion);
            }, token);
    }

    public Task<CommandOutcome> WithdrawAsync(ActorContext actor, Guid quoteId, Guid evidenceId, byte[] evidenceVersion,
        string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        Version(evidenceVersion); reason = QuoteEvidenceRules.Reason(reason);
        OwnedQuoteScope? owned = null; QuoteRevision? revision = null; QuoteCaptureEvidence? evidence = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/evidence/{evidenceId:D}/withdraw", key, correlationId),
            new { quoteId, evidenceId, version = Convert.ToBase64String(evidenceVersion), reason }, "quote.evidence-withdrawn",
            async (db, ct) =>
            {
                (owned, revision) = await Writable(db, actor, quoteId, ct);
                evidence = await db.Set<QuoteCaptureEvidence>().FromSqlInterpolated($"SELECT * FROM QuoteCaptureEvidence WITH(UPDLOCK,HOLDLOCK) WHERE Id={evidenceId} AND QuoteId={quoteId}")
                    .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw new QuoteOperationException(404, "evidence-not-found");
            },
            async (db, ct) =>
            {
                QuoteRules.EnsureEditable(owned!.Quote.State, owned.Quote.CaptureClosedAt);
                if (!CryptographicOperations.FixedTimeEquals(evidence!.RowVersion, evidenceVersion)) throw new QuoteOperationException(412, "stale-evidence");
                if (await db.Set<QuoteEvidenceWithdrawal>().AnyAsync(x => x.EvidenceId == evidenceId, ct)) throw new QuoteOperationException(409, "evidence-already-withdrawn");
                var now = time.GetUtcNow();
                db.Add(new QuoteEvidenceWithdrawal { QuoteId = quoteId, EvidenceId = evidenceId, Reason = reason,
                    ActorId = actor.UserId, CreatedBy = actor.UserId, CreatedAt = now });
                Activity(db, quoteId, revision!.Id, actor.UserId, now, "quote.evidence-withdrawn");
                await db.SaveChangesAsync(ct); return Receipt(evidenceId, 200, evidence.RowVersion);
            }, token);
    }

    public async Task<QuoteEvidenceFile> DownloadAsync(ActorContext actor, Guid quoteId, Guid fileId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        var file = await db.Set<QuoteEvidenceFile>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == fileId && x.QuoteId == quoteId && x.ScreeningState == "accepted", token)
            ?? throw new QuoteOperationException(404, "evidence-file-not-found");
        await transaction.CommitAsync(token); return file;
    }

    private async Task<(OwnedQuoteScope Owned, QuoteRevision Revision)> Writable(BackOfficeDbContext db, ActorContext actor, Guid quoteId, CancellationToken token)
    {
        var owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Capture, token);
        var revision = await QuoteService.CurrentRevision(db, owned.Quote, token);
        var capture = await QuoteCaptureEligibility.ResolveAsync(db, owned.Scope, revision.ProductVersionId, time.GetUtcNow(), revision.AgencyTermsVersionId, token);
        QuoteRules.EnsureRetainedPins(QuoteService.Pins(revision), capture.Pins); return (owned, revision);
    }
    private static void Version(byte[] value) { if (value.Length != 8) throw new QuoteOperationException(400, "invalid-evidence-version"); }
    private static void Current(Quote quote, byte[] version)
    {
        QuoteRules.EnsureEditable(quote.State, quote.CaptureClosedAt);
        if (!CryptographicOperations.FixedTimeEquals(quote.RowVersion, version)) throw new QuoteOperationException(412, "stale-quote");
    }
    private static CommandOutcome Receipt(Guid id, int status, byte[] version) => new(id, status, JsonSerializer.Serialize(new { id }), Etag: "\"" + Convert.ToBase64String(version) + "\"");
    private static void Activity(BackOfficeDbContext db, Guid quoteId, Guid revisionId, Guid actor, DateTimeOffset now, string type) =>
        db.Add(new QuoteActivity { QuoteId = quoteId, RevisionId = revisionId, ActorId = actor, CreatedBy = actor, CreatedAt = now, OccurredAt = now, EventType = type });
}
