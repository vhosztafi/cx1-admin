using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

internal sealed record HeldServicingRating(OwnedQuoteScope Source, ServicingDraft Draft, PolicyTerm Term,
    ServicingRevision Revision, PolicyVersion Base, ResolvedQuoteTerm ResolvedTerm, EligibleQuoteRating Eligible,
    SettingVersion Setting, decimal Fee, RenewalRatingContext? Renewal = null);

internal static class ServicingRatingScope
{
    internal static bool Matches(HeldServicingRating held, ServicingCycle cycle, ServicingRatingRequestInput input)
        => held.Draft.State == "draft" && held.Draft.Kind is "adjustment" or "renewal" && held.Draft.CurrentCycleId == cycle.Id &&
            held.Draft.CurrentRevisionId == cycle.RevisionId && held.Draft.BaseVersionId == cycle.BaseVersionId &&
            held.Eligible.Capture.Terms.Id == input.AgencyTermsVersionId && held.Eligible.RatingVersion.Id == input.RatingRuleVersionId &&
            held.Eligible.BinderVersion.Id == input.BinderVersionId && held.Eligible.AuthorityVersion.Id == input.AuthorityVersionId &&
            held.Eligible.RuntimeVersion.Id == input.RuntimeVersionId && held.Eligible.ScenarioVersion.Id == input.ScenarioVersionId &&
            held.Setting.Id == input.ServicingSettingVersionId && held.Fee == input.Fee && held.Renewal == input.Renewal &&
            cycle.RenewalPreparationVersionId == input.Renewal?.PreparationVersionId &&
            cycle.RenewalExperienceVersionId == input.Renewal?.ExperienceVersionId && cycle.RenewalExperienceReviewId == input.Renewal?.ExperienceReviewId &&
            held.ResolvedTerm == input.Term && held.Eligible.Capture.ProductVersion.Id == input.ProductVersionId &&
            held.Eligible.CommissionBasisPoints == input.CommissionBasisPoints && held.Eligible.MinimumPremium == input.MinimumPremium;

    // Source quote is an immutable ownership/configuration anchor, never a
    // reopened capture or a surrogate servicing underwriting cycle.
    internal static async Task<HeldServicingRating> HoldAsync(BackOfficeDbContext db, ActorContext actor, Guid draftId,
        DateTimeOffset now, CancellationToken token, bool write = true)
    {
        var hint = await db.Set<ServicingDraft>().AsNoTracking().Where(x => x.Id == draftId).Select(x => new { x.PolicyId }).SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "servicing-draft-not-found");
        var quoteId = await db.Set<Policy>().Where(x => x.Id == hint.PolicyId).Select(x => x.SourceQuoteId).SingleAsync(token);
        var source = write ? await QuoteUnderwritingScope.HoldAsync(db, actor, quoteId, "quote-rate", token)
            : await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        if (write && (!source.Scope.Actor.HasCapability("policy-draft-write") || !source.Scope.Actor.HasCapability("policy-draft-rate"))) throw new QuoteOperationException(403, "servicing-rating-denied");
        var draft = await ServicingDraftService.HoldDraft(db, source.Scope.Actor, draftId, write, token);
        var term = await db.Set<PolicyTerm>().SingleAsync(x => x.Id == draft.BaseTermId, token);
        var revision = await db.Set<ServicingRevision>().AsNoTracking().SingleAsync(x => x.Id == draft.CurrentRevisionId && x.DraftId == draft.Id, token);
        var basis = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.Id == draft.BaseVersionId && x.TermId == term.Id && x.PolicyId == draft.PolicyId, token);
        using var productSource = JsonDocument.Parse(basis.SnapshotJson);
        if (productSource.RootElement.GetProperty("productCode").GetString() == CommercialCaptureRules.ProductCode)
            throw new QuoteOperationException(409, "commercial-servicing-rating-unavailable");
        if (draft.Kind == "renewal")
            return await HoldRenewal(db, source, draft, term, revision, basis, now, token);
        using var intent = JsonDocument.Parse(term.LocalTermIntentJson); var assessedTerm = QuoteTerm.Assess(intent.RootElement);
        if (assessedTerm.Term is not { } resolved || resolved.StartsAt != term.StartsAt || resolved.EndsAt != term.EndsAt)
            throw new QuoteOperationException(409, "servicing-term-unavailable");
        using var proposal = JsonDocument.Parse(ServicingProposalInput.Parse(revision.ProposalJson, draft.BaseVersionId).Json);
        var common = proposal.RootElement.GetProperty("commonEffectiveIntent");
        var effective = QuoteTerm.ResolveLondonTime(common.GetProperty("localDate").GetString(), common.GetProperty("localTime").GetString(),
            common.TryGetProperty("utcOffsetMinutes", out var offset) ? offset.GetInt32() : null);
        if (effective.Instant is not { } start || start < resolved.StartsAt || start >= resolved.EndsAt)
            throw new QuoteOperationException(422, effective.Code ?? "effective-outside-term");
        // Eligibility concerns the adjustment's remaining coverage. The original
        // full term is retained separately for annual pricing and day earning.
        var remaining = resolved with { Kind = "short-period", StartsAt = start };
        var capture = await QuoteCaptureEligibility.ResolveAsync(db, source.Scope, term.ProductVersionId, now, null, token);
        var eligible = await QuoteRatingEligibility.ResolveAsync(db, source, term.ProductVersionId, capture.Terms.Id, remaining, now, token);
        var settings = await db.Set<SettingVersion>().FromSqlInterpolated($"SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope={ServicingRatingSeed.Scope}")
            .AsNoTracking().ToArrayAsync(token);
        var setting = settings.Where(x => x.EffectiveFrom <= now).OrderByDescending(x => x.Version).FirstOrDefault();
        var values = setting is null ? null : ServicingRatingConfiguration.Parse(setting.Values);
        if (setting is null || values is null) throw new QuoteOperationException(503, "servicing-rating-configuration-unavailable");
        return new(source, draft, term, revision, basis, resolved, eligible, setting, values.AdjustmentFee);
    }

    private static async Task<HeldServicingRating> HoldRenewal(BackOfficeDbContext db, OwnedQuoteScope source, ServicingDraft draft,
        PolicyTerm term, ServicingRevision revision, PolicyVersion basis, DateTimeOffset now, CancellationToken token)
    {
        var preparations = await db.Set<RenewalPreparationVersion>().FromSqlInterpolated($"SELECT * FROM RenewalPreparationVersion WITH(HOLDLOCK) WHERE DraftId={draft.Id}")
            .AsNoTracking().ToArrayAsync(token);
        var preparation = preparations.OrderByDescending(x => x.Sequence).FirstOrDefault()
            ?? throw new QuoteOperationException(409, "renewal-preparation-required");
        var held = await RenewalPreparationService.ResolveEligibility(db, source, term.Id, preparation.TermMonths, preparation.EndUtcOffsetMinutes, now, token);
        if (held.Basis.Id != basis.Id || preparation.ProductVersionId != held.Eligible.Capture.ProductVersion.Id ||
            preparation.BinderVersionId != held.Eligible.BinderVersion.Id || preparation.AgencyTermsVersionId != held.Eligible.Capture.Terms.Id ||
            preparation.RuleSettingVersionId != held.Setting.Id || preparation.FairValueAssessmentId != held.FairValue?.Id ||
            preparation.StartsAt != held.Prepared.Term.StartsAt || preparation.EndsAt != held.Prepared.Term.EndsAt)
            throw new QuoteOperationException(409, "renewal-preparation-stale");
        using var proposal = JsonDocument.Parse(ServicingProposalInput.Parse(revision.ProposalJson, draft.BaseVersionId).Json);
        var common = proposal.RootElement.GetProperty("commonEffectiveIntent");
        var effective = QuoteTerm.ResolveLondonTime(common.GetProperty("localDate").GetString(), common.GetProperty("localTime").GetString(),
            common.TryGetProperty("utcOffsetMinutes", out var offset) ? offset.GetInt32() : null);
        if (effective.Instant != held.Prepared.Term.StartsAt)
            throw new QuoteOperationException(422, "renewal-effective-inception-required");
        var experiences = await db.Set<RenewalExperienceVersion>().FromSqlInterpolated($"SELECT * FROM RenewalExperienceVersion WITH(HOLDLOCK) WHERE DraftId={draft.Id}")
            .AsNoTracking().ToArrayAsync(token);
        var experience = experiences.OrderByDescending(x => x.Sequence).FirstOrDefault();
        RenewalExperienceReview? review = null;
        if (experience is not null)
        {
            var reviews = await db.Set<RenewalExperienceReview>().FromSqlInterpolated($"SELECT * FROM RenewalExperienceReview WITH(HOLDLOCK) WHERE ExperienceVersionId={experience.Id} AND DraftId={draft.Id}")
                .AsNoTracking().ToArrayAsync(token);
            review = reviews.OrderByDescending(x => x.Sequence).FirstOrDefault();
        }
        if (review?.Outcome == "accepted" && !await (from grant in db.Set<UserAuthorityGrant>()
            join authority in db.Set<AuthorityVersion>() on grant.AuthorityVersionId equals authority.Id
            join reviewer in db.Set<StaffUser>() on grant.UserId equals reviewer.Id
            where grant.Id == review.AuthorityGrantId && grant.UserId == review.CreatedBy && authority.Id == review.AuthorityVersionId &&
                grant.RevokedAt == null && grant.EffectiveFrom <= now && now < grant.EffectiveTo &&
                authority.State == "published" && authority.EffectiveFrom <= now && now < authority.EffectiveTo && reviewer.State == "active"
            select grant.Id).AnyAsync(token))
            throw new QuoteOperationException(409, "renewal-experience-review-stale");
        var facts = experience is null ? null : new RenewalExperienceFacts(experience.ObservationStartsOn, experience.ObservationEndsOn,
            experience.ClaimCount, experience.Paid, experience.Outstanding, experience.EarnedPremium, experience.SourceCode,
            experience.SourceReference, experience.EvidenceAssociationId);
        var context = new RenewalRatingContext(preparation.Id, experience?.Id, review?.Id, preparation.FairValueAssessmentId,
            facts, review?.Outcome == "accepted", held.Settings.RuleVersion, held.Settings.LossRatioThresholdBasisPoints, held.Settings.ExperienceLoadingBasisPoints);
        return new(source, draft, term, revision, basis, held.Prepared.Term, held.Eligible, held.Setting, held.Settings.RenewalFee, context);
    }
}
