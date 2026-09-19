using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class CancellationReviewService
{
    private async Task<AssessedCancellation> Assess(BackOfficeDbContext db, HeldCancellation held, CancellationToken token)
    {
        var draft = held.Draft; var term = held.Term; var now = time.GetUtcNow();
        var revision = await db.Set<ServicingRevision>().AsNoTracking().SingleAsync(x => x.Id == draft.CurrentRevisionId && x.DraftId == draft.Id, token);
        using var proposal = JsonDocument.Parse(revision.ProposalJson);
        var root = proposal.RootElement;
        var code = root.TryGetProperty("cancellationReasonCode", out var selected) ? selected.GetString() ?? "" : "";
        var rule = CancellationDecisionRules.Reasons.SingleOrDefault(x => x.Code == code);
        var intent = root.GetProperty("commonEffectiveIntent");
        var resolved = QuoteTerm.ResolveLondonTime(intent.GetProperty("localDate").GetString()!, intent.GetProperty("localTime").GetString()!,
            intent.TryGetProperty("utcOffsetMinutes", out var offset) ? offset.GetInt32() : null);
        var effective = resolved.Instant ?? term.StartsAt;
        var blockers = new List<string>();
        if (resolved.Instant is null) blockers.Add("cancellation-effective-time-invalid");
        if (rule is null) blockers.Add("cancellation-reason-required");
        if (root.GetProperty("changes").GetArrayLength() != 0) blockers.Add("cancellation-risk-changes-forbidden");
        if (draft.State != "draft") blockers.Add("servicing-draft-closed");
        var versions = await db.Set<PolicyVersion>().AsNoTracking().Where(x => x.TermId == term.Id && x.PolicyId == draft.PolicyId)
            .OrderBy(x => x.EffectiveAt).ThenBy(x => x.Sequence).ThenBy(x => x.SliceOrdinal).ToArrayAsync(token);
        var latest = versions.Last();
        if (latest.Id != draft.BaseVersionId) blockers.Add("servicing-base-stale");
        var laterTerms = await db.Set<PolicyTerm>().AsNoTracking().Where(x => x.PolicyId == draft.PolicyId && x.Number > term.Number)
            .OrderBy(x => x.Number).Select(x => new { x.Id, x.Number, x.StartsAt, x.EndsAt }).ToArrayAsync(token);
        var transactions = await db.Set<PolicyTransaction>().AsNoTracking().Where(x => x.PolicyId == draft.PolicyId && x.TermId == term.Id)
            .OrderBy(x => x.Sequence).ToArrayAsync(token);
        if (transactions.Any(x => x.Kind == "cancellation")) blockers.Add("policy-already-cancelled");
        var obligations = await db.Set<IssueFinancialObligation>().AsNoTracking().Where(x => x.PolicyId == draft.PolicyId && x.TermId == term.Id)
            .OrderBy(x => x.Id).ToArrayAsync(token);
        var journals = await db.Set<Journal>().AsNoTracking().Where(x => obligations.Select(o => o.Id).Contains(x.ObligationId)).OrderBy(x => x.Id).ToArrayAsync(token);
        var components = await db.Set<IssueFinancialComponent>().AsNoTracking().Where(x => obligations.Select(o => o.Id).Contains(x.ObligationId))
            .OrderBy(x => x.Id).ToArrayAsync(token);
        if (transactions.Length != obligations.Length || obligations.Length != journals.Length || journals.Any(x => x.PostedAt is null) ||
            transactions.Any(t => !obligations.Any(o => o.TransactionId == t.Id)) || obligations.Any(o => !journals.Any(j => j.ObligationId == o.Id && j.TransactionId == o.TransactionId)))
            blockers.Add("cancellation-posted-ledger-required");
        var basis = obligations.FirstOrDefault();
        if (basis is null || obligations.Any(x => x.Currency != "GBP" || x.AgencyId != basis.AgencyId || x.ClientId != basis.ClientId ||
            x.RelationshipId != basis.RelationshipId || x.ProviderId != basis.ProviderId || x.DebtorKind != basis.DebtorKind ||
            x.DebtorAgencyId != basis.DebtorAgencyId || x.DebtorRelationshipId != basis.DebtorRelationshipId || x.Settlement != basis.Settlement))
            blockers.Add("cancellation-settlement-combination-unsupported");
        var evidence = await db.Set<CancellationEvidence>().AsNoTracking().Where(x => x.DraftId == draft.Id && x.RevisionId == revision.Id)
            .OrderBy(x => x.Id).ToArrayAsync(token);
        var reviews = await db.Set<CancellationEvidenceReview>().AsNoTracking().Where(x => x.DraftId == draft.Id && x.RevisionId == revision.Id)
            .OrderBy(x => x.EvidenceId).ThenBy(x => x.Sequence).ToArrayAsync(token);
        var accepted = new List<CancellationNoticeEvidence>();
        var evidenceInputs = new List<object>();
        foreach (var association in evidence)
        {
            var review = reviews.LastOrDefault(x => x.EvidenceId == association.Id);
            var valid = review?.Outcome == "accepted" && await CurrentGrant(db, held, review.AuthorityGrantId, review.AuthorityVersionId, review.CreatedBy!.Value, token);
            var file = await db.Set<ServicingEvidenceFile>().AsNoTracking().SingleAsync(x => x.Id == association.FileId && x.DraftId == draft.Id, token);
            valid = valid && file.ScreeningState == "accepted";
            evidenceInputs.Add(new { association.Id, association.RevisionId, association.Purpose, association.NoticeDeliveredAt,
                fileId = file.Id, file.Sha256, reviewId = review?.Id, accepted = valid });
            if (rule?.EvidencePurposes.Contains(association.Purpose) == true)
                accepted.Add(new(association.Id, association.Purpose, valid, association.NoticeDeliveredAt));
        }
        CancellationDecisionAssessment? decision = null;
        if (rule is not null)
        {
            decision = CancellationDecisionRules.Assess(code, term.StartsAt, term.EndsAt, latest.EffectiveAt, effective, now,
                laterTerms.Length > 0, held.Source.Scope.Actor.Roles.Contains("senior-underwriter") &&
                held.Grants.Any(x => held.Settings.SeniorAuthorityVersions.Contains(x.Version.Version)), accepted);
            blockers.AddRange(decision.Blockers);
        }
        CancellationReturnPreview? amounts = null;
        if (basis is not null)
        {
            try
            {
                amounts = CancellationReviewRules.Calculate(term.StartsAt, term.EndsAt, effective, basis.DebtorKind == "agency" ? "agency" : "mga", basis.Settlement,
                    components.Select(x => new CancellationPostedComponent(x.Id, x.Code, x.Amount, x.CoverageStartsAt, x.CoverageEndsAt, x.OriginalComponentId)).ToArray());
            }
            catch (ArgumentException) { blockers.Add("cancellation-return-ledger-unsupported"); }
        }
        var inputJson = JsonSerializer.Serialize(new { draft.Id, draft.PolicyId, draft.BaseTermId, draft.BaseVersionId, revisionId = revision.Id,
            revision.ContentHash, proposal = root, settingId = held.Setting.Id, settingValues = held.Setting.Values,
            effectiveAt = effective, versions = versions.Select(x => new { x.Id, x.ContentHash, x.EffectiveAt, x.Sequence, x.SliceOrdinal }),
            laterTerms, transactions = transactions.Select(x => new { x.Id, x.Kind, x.Sequence, x.EffectiveAt }),
            obligations, journals, components, evidence = evidenceInputs, amounts }, Json);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inputJson)));
        var hashBytes = Convert.FromHexString(hash);
        var preview = await db.Set<CancellationPreview>().AsNoTracking().Where(x => x.DraftId == draft.Id && x.RevisionId == revision.Id && x.InputHash == hashBytes)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(token);
        var approval = preview is null ? null : await db.Set<CancellationApproval>().AsNoTracking().Where(x => x.PreviewId == preview.Id)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(token);
        var approvalCurrent = approval is not null && await CurrentGrant(db, held, approval.AuthorityGrantId, approval.AuthorityVersionId, approval.CreatedBy!.Value, token, code, draft.CreatedBy);
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        int Day(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, london).DateTime).DayNumber;
        var termDays = Day(term.EndsAt) - Day(term.StartsAt);
        return new(new(draft.Id, revision.Id, draft.BaseVersionId, Etag(draft), preview?.Id, approvalCurrent ? approval!.Id : null,
            hash, held.Settings.RuleVersion, code, effective, latest.EffectiveAt, decision?.NoticeEffectiveFrom, amounts,
            blockers.Distinct().ToArray(), rule is not null && blockers.Count == 0 && ApprovalGrant(held, code) is not null,
            Math.Clamp(Day(effective) - Day(term.StartsAt), 0, termDays), termDays,
            amounts is null ? null : components.Where(x => x.Code == "premium").Sum(x => x.Amount) + amounts.Posting.Premium), inputJson);
    }

    private async Task<bool> CurrentGrant(BackOfficeDbContext db, HeldCancellation held, Guid grantId, Guid authorityId,
        Guid userId, CancellationToken token, string? approvalReason = null, Guid? requesterId = null)
    {
        var now = time.GetUtcNow();
        var grant = await db.Set<UserAuthorityGrant>().FromSqlInterpolated($"SELECT * FROM UserAuthorityGrant WITH(HOLDLOCK) WHERE Id={grantId}").AsNoTracking().SingleOrDefaultAsync(token);
        var authority = await db.Set<AuthorityVersion>().FromSqlInterpolated($"SELECT * FROM AuthorityVersion WITH(HOLDLOCK) WHERE Id={authorityId}").AsNoTracking().SingleOrDefaultAsync(token);
        var user = await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(HOLDLOCK) WHERE Id={userId}").AsNoTracking().SingleOrDefaultAsync(token);
        var roles = await (from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId == userId select role.Code).ToArrayAsync(token);
        if (grant is null || authority is null || user?.State != "active" || !roles.Any(x => x is "underwriter" or "senior-underwriter") ||
            grant.UserId != userId || grant.AuthorityVersionId != authorityId || grant.RevokedAt is not null ||
            grant.EffectiveFrom > now || grant.EffectiveTo <= now || authority.State != "published" || authority.EffectiveFrom > now || authority.EffectiveTo <= now ||
            authority.ProductVersionId != held.Term.ProductVersionId || authority.ProductId != held.Term.ProductId ||
            grant.EffectiveFrom > held.Term.StartsAt || grant.EffectiveTo < held.Term.EndsAt ||
            authority.EffectiveFrom > held.Term.StartsAt || authority.EffectiveTo < held.Term.EndsAt ||
            !held.Settings.AuthorityVersions.Contains(authority.Version)) return false;
        var binder = await db.Set<BinderVersion>().FromSqlInterpolated($"SELECT * FROM BinderVersion WITH(HOLDLOCK) WHERE Id={authority.BinderVersionId}").AsNoTracking().SingleOrDefaultAsync(token);
        if (binder is null || binder.ProductId != held.Term.ProductId || binder.State != "published" || binder.EffectiveFrom > now || binder.EffectiveTo <= now ||
            binder.EffectiveFrom > held.Term.StartsAt || binder.EffectiveTo < held.Term.EndsAt) return false;
        var senior = roles.Contains("senior-underwriter") && held.Settings.SeniorAuthorityVersions.Contains(authority.Version);
        return approvalReason is null || CancellationDecisionRules.CanApprove(approvalReason, requesterId ?? Guid.Empty, userId, true, senior);
    }
}
