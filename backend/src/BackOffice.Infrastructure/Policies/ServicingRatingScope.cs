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
    SettingVersion Setting, ServicingRatingSettings Settings);

internal static class ServicingRatingScope
{
    internal static bool Matches(HeldServicingRating held, ServicingCycle cycle, ServicingRatingRequestInput input)
        => held.Draft.State == "draft" && held.Draft.Kind == "adjustment" && held.Draft.CurrentCycleId == cycle.Id &&
            held.Draft.CurrentRevisionId == cycle.RevisionId && held.Draft.BaseVersionId == cycle.BaseVersionId &&
            held.Eligible.Capture.Terms.Id == input.AgencyTermsVersionId && held.Eligible.RatingVersion.Id == input.RatingRuleVersionId &&
            held.Eligible.BinderVersion.Id == input.BinderVersionId && held.Eligible.AuthorityVersion.Id == input.AuthorityVersionId &&
            held.Eligible.RuntimeVersion.Id == input.RuntimeVersionId && held.Eligible.ScenarioVersion.Id == input.ScenarioVersionId &&
            held.Setting.Id == input.ServicingSettingVersionId && held.Settings.AdjustmentFee == input.Fee &&
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
        return new(source, draft, term, revision, basis, resolved, eligible, setting, values);
    }
}
