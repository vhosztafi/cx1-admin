using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record CancellationReviewView(Guid DraftId, Guid RevisionId, Guid BaseVersionId, string DraftEtag,
    Guid? PreviewId, Guid? ApprovalId, string PreviewHash, string RuleVersion, string ReasonCode, DateTimeOffset EffectiveAt,
    DateTimeOffset SupportedFrom, DateOnly? NoticeEffectiveFrom, CancellationReturnPreview? Amounts,
    IReadOnlyList<string> Blockers, bool CanApprove, int DaysOnCover, int TermDays, decimal? PremiumChargedToDate);
internal sealed record HeldCancellation(ServicingDraft Draft, OwnedQuoteScope Source, PolicyTerm Term,
    SettingVersion Setting, CancellationSettings Settings, IReadOnlyList<EffectiveUnderwritingGrant> Grants);
internal sealed record AssessedCancellation(CancellationReviewView View, string InputJson);

public sealed partial class CancellationReviewService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CancellationReviewView> ReadAsync(ActorContext actor, Guid draftId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var held = await Hold(db, actor, draftId, "policy-read", false, token);
        var result = await Assess(db, held, token);
        await tx.CommitAsync(token); return result.View;
    }

    public Task<CommandOutcome> PrepareAsync(ActorContext actor, Guid draftId, byte[] version, Guid leaseToken,
        string previewHash, string key, Guid correlation, CancellationToken token = default)
    {
        DemandHash(previewHash);
        return Mutate(actor, draftId, version, leaseToken, "cancellation-preview", new { previewHash }, key, correlation,
            "policy-draft-write", async (db, held, ct) =>
            {
                var assessed = await Assess(db, held, ct); DemandPreview(assessed.View, previewHash);
                if (assessed.View.PreviewId is { } existing) return existing;
                var row = new CancellationPreview { DraftId = draftId, PolicyId = held.Draft.PolicyId, BaseTermId = held.Draft.BaseTermId,
                    BaseVersionId = held.Draft.BaseVersionId, RevisionId = held.Draft.CurrentRevisionId!.Value,
                    RuleSettingVersionId = held.Setting.Id, RuleVersion = held.Settings.RuleVersion, ReasonCode = assessed.View.ReasonCode,
                    EffectiveAt = assessed.View.EffectiveAt, InputHash = Convert.FromHexString(previewHash), InputJson = assessed.InputJson,
                    ResultJson = JsonSerializer.Serialize(assessed.View.Amounts, Json), CreatedAt = time.GetUtcNow(), CreatedBy = held.Source.Scope.Actor.UserId };
                db.Add(row); return row.Id;
            }, token);
    }

    public Task<CommandOutcome> ApproveAsync(ActorContext actor, Guid draftId, byte[] version, Guid leaseToken,
        Guid previewId, string previewHash, string reason, string key, Guid correlation, CancellationToken token = default)
    {
        DemandHash(previewHash); reason = Reason(reason);
        if (previewId == Guid.Empty) throw new QuoteOperationException(422, "cancellation-preview-required");
        EffectiveUnderwritingGrant? authority = null;
        return Mutate(actor, draftId, version, leaseToken, "cancellation-approvals", new { previewId, previewHash, reason }, key, correlation,
            "underwriting-decide-within-authority", async (db, held, ct) =>
            {
                var assessed = await Assess(db, held, ct); DemandPreview(assessed.View, previewHash);
                if (assessed.View.PreviewId != previewId) throw new QuoteOperationException(409, "cancellation-preview-stale");
                if (assessed.View.ApprovalId is { } existing) return existing;
                var row = new CancellationApproval { DraftId = draftId, RevisionId = held.Draft.CurrentRevisionId!.Value,
                    PreviewId = previewId, PreviewHash = Convert.FromHexString(previewHash), AuthorityGrantId = authority!.Grant.Id,
                    AuthorityVersionId = authority.Version.Id, Reason = reason, CreatedAt = time.GetUtcNow(), CreatedBy = held.Source.Scope.Actor.UserId };
                db.Add(row); return row.Id;
            }, token, async (db, held, ct) =>
            {
                // Authorization precedes idempotency replay, including revoked
                // grants and requester/approver separation for this exact intent.
                var preview = await db.Set<CancellationPreview>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == previewId && x.DraftId == draftId, ct)
                    ?? throw new QuoteOperationException(404, "cancellation-preview-not-found");
                authority = ApprovalGrant(held, preview.ReasonCode);
                if (authority is null) throw new QuoteOperationException(403, "cancellation-approval-authority-required");
            });
    }

    private async Task<HeldCancellation> Hold(BackOfficeDbContext db, ActorContext actor, Guid draftId,
        string capability, bool write, CancellationToken token)
    {
        if (!actor.HasCapability(capability)) throw new QuoteOperationException(403, "cancellation-access-denied");
        var quoteId = await (from d in db.Set<ServicingDraft>() join p in db.Set<Policy>() on d.PolicyId equals p.Id
            where d.Id == draftId select (Guid?)p.SourceQuoteId).SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "servicing-draft-not-found");
        var source = await QuoteScope.ForQuoteAsync(db, actor, quoteId, write ? QuoteAccess.Underwriting : QuoteAccess.Read, token);
        if (!source.Scope.Actor.HasCapability(capability)) throw new QuoteOperationException(403, "cancellation-access-denied");
        var draft = await ServicingDraftService.HoldDraft(db, source.Scope.Actor, draftId, write, token);
        if (draft.Kind != "cancellation") throw new QuoteOperationException(409, "cancellation-draft-required");
        var term = await db.Set<PolicyTerm>().SingleAsync(x => x.Id == draft.BaseTermId, token);
        var now = time.GetUtcNow();
        var settings = await db.Set<SettingVersion>().FromSqlInterpolated($"SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope={CancellationConfiguration.Scope}").AsNoTracking().ToArrayAsync(token);
        var setting = settings.Where(x => x.EffectiveFrom <= now).OrderByDescending(x => x.Version).FirstOrDefault();
        var config = setting is null ? null : CancellationConfiguration.Parse(setting.Values);
        if (setting is null || config is null) throw new QuoteOperationException(503, "cancellation-configuration-unavailable");
        var grants = new List<EffectiveUnderwritingGrant>();
        if (source.Scope.Actor.HasCapability("underwriting-decide-within-authority"))
        {
            var product = await db.Set<Product>().AsNoTracking().SingleAsync(x => x.Id == term.ProductId, token);
            using var intent = JsonDocument.Parse(term.LocalTermIntentJson);
            var resolved = QuoteTerm.Assess(intent.RootElement).Term ?? throw new QuoteOperationException(409, "servicing-term-unavailable");
            var authorities = await db.Set<AuthorityVersion>().FromSqlInterpolated($"SELECT * FROM AuthorityVersion WITH(HOLDLOCK) WHERE ProductVersionId={term.ProductVersionId}").AsNoTracking().ToArrayAsync(token);
            foreach (var binderId in authorities.Select(x => x.BinderVersionId).Distinct())
            {
                var binder = await db.Set<BinderVersion>().FromSqlInterpolated($"SELECT * FROM BinderVersion WITH(HOLDLOCK) WHERE Id={binderId}").AsNoTracking().SingleAsync(token);
                grants.AddRange((await QuoteUnderwritingScope.GrantsAsync(db, source, term.ProductVersionId, binder, product.Code, resolved, now, token))
                    .Where(x => config.AuthorityVersions.Contains(x.Version.Version)));
            }
        }
        return new(draft, source, term, setting, config, grants);
    }

    private static EffectiveUnderwritingGrant? ApprovalGrant(HeldCancellation held, string code) => held.Grants.FirstOrDefault(x =>
        CancellationDecisionRules.CanApprove(code, held.Draft.CreatedBy ?? Guid.Empty, held.Source.Scope.Actor.UserId, true,
            held.Source.Scope.Actor.Roles.Contains("senior-underwriter") && held.Settings.SeniorAuthorityVersions.Contains(x.Version.Version)));

    private Task<CommandOutcome> Mutate<T>(ActorContext actor, Guid draftId, byte[] version, Guid leaseToken,
        string action, T input, string key, Guid correlation, string capability,
        Func<BackOfficeDbContext, HeldCancellation, CancellationToken, Task<Guid>> handler, CancellationToken token,
        Func<BackOfficeDbContext, HeldCancellation, CancellationToken, Task>? authorize = null)
    {
        if (version.Length != 8 || leaseToken == Guid.Empty) throw new QuoteOperationException(400, "cancellation-command-invalid");
        HeldCancellation? held = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/drafts/{draftId:D}/{action}", key, correlation),
            new { draftId, version = Convert.ToBase64String(version), leaseToken, input }, "servicing." + action.Replace('/', '-'),
            async (db, ct) => { held = await Hold(db, actor, draftId, capability, true, ct); if (authorize is not null) await authorize(db, held, ct); },
            async (db, ct) =>
            {
                if (held!.Draft.State != "draft") throw new QuoteOperationException(409, "servicing-draft-closed");
                if (!held.Draft.RowVersion.SequenceEqual(version)) throw new QuoteOperationException(412, "servicing-version-conflict");
                await new ServicingDraftService(factory, time).DemandLease(db, draftId, held.Source.Scope.Actor.UserId, leaseToken, ct);
                var id = await handler(db, held, ct);
                held.Draft.UpdatedAt = time.GetUtcNow(); db.Entry(held.Draft).Property(x => x.UpdatedAt).IsModified = true;
                await db.SaveChangesAsync(ct); var etag = Etag(held.Draft);
                return new(id, 201, JsonSerializer.Serialize(new { draftId, resourceId = id, draftEtag = etag }, Json), Etag: etag);
            }, token);
    }

    private static string Etag(ServicingDraft draft) => "\"" + Convert.ToBase64String(draft.RowVersion) + "\"";
    private static string Reason(string reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length is >= 10 and <= 2000 && !reason.Any(char.IsControl)
        ? reason.Trim() : throw new QuoteOperationException(422, "cancellation-reason-required");
    private static void DemandHash(string hash)
    { if (hash is null || hash.Length != 64 || hash.Any(c => !char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f'))) throw new QuoteOperationException(422, "cancellation-preview-hash-invalid"); }
    private static void DemandPreview(CancellationReviewView view, string hash)
    {
        if (view.PreviewHash != hash) throw new QuoteOperationException(409, "cancellation-preview-stale");
        if (view.Blockers.Count != 0 || view.Amounts is null) throw new QuoteOperationException(409, "cancellation-review-blocked");
    }
}
