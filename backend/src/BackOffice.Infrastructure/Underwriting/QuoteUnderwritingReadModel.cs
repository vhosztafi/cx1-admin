using System.Globalization;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed partial class QuoteUnderwritingReadModel(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public async Task<Dictionary<string, object>> AssessmentAsync(ActorContext actor, Guid quoteId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var result = await Assess(db, actor, quoteId, token); await tx.CommitAsync(token); return result;
    }

    public async Task<Dictionary<string, object>> RatingAsync(ActorContext actor, Guid ratingId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var hint = await db.Set<QuoteRatingResult>().AsNoTracking().Where(x => x.Id == ratingId).Select(x => (Guid?)x.QuoteId).SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "rating-not-found");
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var assessment = await Assess(db, actor, hint, token);
        var rating = await db.Set<QuoteRatingResult>().AsNoTracking().SingleAsync(x => x.Id == ratingId && x.QuoteId == hint, token);
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == rating.CycleId && x.QuoteId == hint, token);
        var revision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == cycle.QuoteRevisionId && x.QuoteId == hint, token);
        using var proposal = JsonDocument.Parse(revision.ProposalJson);
        var outcome = JsonSerializer.Deserialize<QuoteRatingOutcome>(rating.ResultJson, QuoteRatingService.Json)!;
        var result = new Dictionary<string, object> {
            ["id"] = rating.Id, ["quoteId"] = hint, ["cycleId"] = cycle.Id, ["revisionId"] = revision.Id, ["pricingInputHash"] = Convert.ToHexStringLower(rating.InputHash),
            ["ruleVersionId"] = rating.RuleVersionId, ["completedAt"] = rating.CompletedAt, ["expiresAt"] = rating.ExpiresAt,
            ["applicable"] = assessment.TryGetValue("ratingId", out var current) && current is Guid id && id == ratingId && rating.Outcome == "rated" &&
                !((List<object>)assessment["blockers"]).OfType<UnderwritingReadBlocker>().Any(x => x.Code is "underwriting-cycle-stale" or "quote-rating-expired" or "underwriting-product-unavailable" or "underwriting-configuration-unavailable" or "quote-terms-refresh-required"),
            ["currency"] = "GBP", ["annualPremium"] = Money(rating.AnnualPremium), ["termPremium"] = Money(rating.TermPremium), ["tax"] = Money(rating.Tax),
            ["fee"] = Money(rating.Fee), ["grossPayable"] = Money(rating.GrossPayable), ["brokerCommission"] = Money(rating.BrokerCommission), ["agencyTermsVersionId"] = cycle.AgencyTermsVersionId,
            ["factors"] = outcome.Rating?.Factors.Select(x => (object)new { x.Code, label = x.Code.Replace('-', ' '), amount = Money(x.Amount), x.Direction, basisAmount = Money(x.BasisAmount), x.BasisPoints }).ToArray() ?? [],
            ["input"] = proposal.RootElement.Clone(), ["blockers"] = assessment["blockers"] };
        await tx.CommitAsync(token); return result;
    }

    private async Task<Dictionary<string, object>> Assess(BackOfficeDbContext db, ActorContext actor, Guid quoteId, CancellationToken token)
    {
        var owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token); var quote = owned.Quote;
        if (!owned.Scope.Actor.HasCapability("underwriting-read")) throw new QuoteOperationException(403, "underwriting-access-denied");
        var revision = await QuoteService.CurrentRevision(db, quote, token); var now = time.GetUtcNow();
        var cycle = quote.CurrentUnderwritingCycleId is Guid id ? await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == id && x.QuoteId == quoteId, token) : null;
        var rating = cycle?.CurrentRatingId is Guid ratingId ? await db.Set<QuoteRatingResult>().AsNoTracking().SingleAsync(x => x.Id == ratingId && x.CycleId == cycle.Id, token) : null;
        var blockers = new List<object>(); EligibleQuoteRating? eligible = null; var readyToRate = false;
        using var proposal = JsonDocument.Parse(revision.ProposalJson); using var intent = JsonDocument.Parse(revision.TermIntentJson);
        var term = QuoteTerm.Assess(intent.RootElement);
        var matching = await QuoteMatching.AssessAsync(db, quote, now, token);
        if (term.Term is null) foreach (var issue in term.Issues) blockers.Add(new UnderwritingReadBlocker(issue.Code, "Complete the policy term.", issue.Path));
        else
        {
            try
            {
                eligible = await QuoteRatingEligibility.ResolveAsync(db, owned, revision.ProductVersionId, revision.AgencyTermsVersionId, term.Term, now, token);
                QuoteRules.EnsureRetainedPins(QuoteService.Pins(revision), eligible.Capture.Pins);
                _ = QuoteUnderwritingInput.Project(proposal.RootElement, eligible.Rating);
                var closure = quote.CaptureClosedAt is null || cycle is not null && cycle.QuoteRevisionId == revision.Id && cycle.State == "rated" ? null : "quote-capture-closed";
                var readiness = QuoteReadiness.Assess(quoteId, revision.Id, proposal.RootElement, term, closure,
                    DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime),
                    await QuoteLookupProvenance.VehicleModesAsync(db, revision, token), matchingCode: matching.Code, includeEvidence: false);
                readyToRate = readiness.Ready;
                foreach (var issue in readiness.Issues) blockers.Add(new UnderwritingReadBlocker(issue.Code, issue.Message, issue.Path));
            }
            catch (QuoteOperationException error) { blockers.Add(new UnderwritingReadBlocker(error.Code, "Current product, agency and underwriting configuration must be available.")); eligible = null; }
            catch (QuoteInputException error) { blockers.Add(new UnderwritingReadBlocker(error.Code, "Refresh the saved version selection.")); eligible = null; }
            catch (QuoteValidationException error) { foreach (var issue in error.Issues) blockers.Add(new UnderwritingReadBlocker(issue.Code, "Complete or correct the captured details.", issue.Path)); }
        }
        var current = eligible is not null && cycle is not null && cycle.QuoteRevisionId == revision.Id && cycle.RatingRuleVersionId == eligible.RatingVersion.Id &&
            quote.AgencyId == cycle.AgencyId && quote.ClientId == cycle.ClientId && quote.RelationshipId == cycle.RelationshipId && quote.ProductId == cycle.ProductId &&
            cycle.BinderVersionId == eligible.BinderVersion.Id && cycle.AuthorityVersionId == eligible.AuthorityVersion.Id;
        if (cycle is not null && !current) blockers.Add(new UnderwritingReadBlocker("underwriting-cycle-stale", "Return to draft or rate again using current configuration."));
        if (cycle?.State == "failed") blockers.Add(new UnderwritingReadBlocker("quote-rating-failed", "The rating did not apply. Review the job or return to draft."));
        if (rating is not null && rating.ExpiresAt <= now) blockers.Add(new UnderwritingReadBlocker("quote-rating-expired", "Obtain a current rating."));
        if (cycle is not null)
            foreach (var referral in await db.Set<QuoteReferral>().AsNoTracking().Where(x => x.CycleId == cycle.Id && x.State != "approved" && x.State != "superseded").OrderBy(x => x.Sequence).ToArrayAsync(token))
                blockers.Add(new UnderwritingReadBlocker(referral.RuleCode, referral.Reason, TargetId: referral.RiskItemId, Dimension: referral.Dimension));
        IReadOnlyList<UnderwritingProofRequirement> proofRequirements = [];
        if (cycle is not null)
        {
            var storedInput = JsonSerializer.Deserialize<StoredRatingInput>(cycle.InputJson, QuoteRatingService.Json)!;
            proofRequirements = await UnderwritingEvidenceService.Requirements(db, cycle, revision, storedInput, token);
            foreach (var proof in proofRequirements.Where(x => !x.Satisfied))
                blockers.Add(new UnderwritingReadBlocker("evidence-review-required-" + proof.Code, proof.Label + " requires current underwriting review.", proof.Path, proof.RiskItemId));
        }
        else foreach (var proof in (await QuoteEvidenceReadModel.AssessAsync(db, revision, token)).Requirements.Where(x => x.State != "current"))
            blockers.Add(new UnderwritingReadBlocker("evidence-missing-" + proof.Code, proof.Label + " is required before later progression.", proof.Path, proof.RiskItemId));
        var writable = owned.Scope.Agency.State == "active" && owned.Scope.Client.IdentityState == "active" && owned.Scope.Relationship.State == "active";
        var grants = current && eligible is not null && term.Term is not null
            ? await QuoteUnderwritingScope.GrantsAsync(db, owned, revision.ProductVersionId, eligible.BinderVersion, eligible.Capture.Product.Code, term.Term, now, token) : [];
        var decisionContext = writable && current && cycle?.State == "rated" && quote.State is not ("bound" or "withdrawn" or "draft");
        var productDetails = await (from version in db.Set<ProductVersion>().AsNoTracking()
                                    join product in db.Set<Product>().AsNoTracking() on version.ProductId equals product.Id
                                    join provider in db.Set<CapacityProvider>().AsNoTracking() on version.ProviderId equals provider.Id
                                    where version.Id == revision.ProductVersionId && product.Id == quote.ProductId
                                    select new { productLabel = product.Name, version.Version, providerLabel = provider.Name }).SingleAsync(token);
        var result = new Dictionary<string, object> {
            ["quoteId"] = quoteId, ["quoteEtag"] = "\"" + Convert.ToBase64String(quote.RowVersion) + "\"", ["state"] = quote.State, ["blockers"] = blockers.Take(200).ToList(),
            ["createdAt"] = quote.CreatedAt, ["productLabel"] = productDetails.productLabel,
            ["productVersionLabel"] = "v" + productDetails.Version.ToString(CultureInfo.InvariantCulture), ["providerLabel"] = productDetails.providerLabel,
            ["capabilities"] = new { canRate = writable && readyToRate && quote.State is "draft" or "rated" or "referred",
                canSubmit = writable && current && matching.Code is null && cycle?.State == "rated" && rating is { Outcome: "rated" } && rating.ExpiresAt > now && quote.State is "rated" or "referred",
                canRevise = writable && cycle is not null && UnderwritingLifecycleRules.CanReturnToDraft(quote.State),
                canReviewEvidence = decisionContext && grants.Count > 0 && owned.Scope.Actor.HasCapability("underwriting-evidence-review"),
                canDecide = decisionContext && grants.Count > 0 && rating?.ExpiresAt > now && owned.Scope.Actor.HasCapability("underwriting-decide-within-authority"),
                canEscalate = false, canPrepareTerms = false, canSend = false, canAccept = false, canIssue = false } };
        result["proofRequirements"] = proofRequirements;
        if (cycle is not null) result["assuranceHash"] = await UnderwritingEvidenceService.Assurance(db, cycle, revision, token);
        var endorsements = new List<object>();
        if (cycle is not null)
            foreach (var condition in await UnderwritingEvidenceService.ActiveConditions(db, cycle.Id, token))
            {
                if (condition.Kind != "warranty") continue;
                var definition = QuoteReferralService.Parse(condition.DefinitionJson, proposal.RootElement);
                endorsements.Add(new { code = condition.EndorsementCode!, version = "1", wording = condition.Wording,
                    decisionId = condition.DecisionId, targetIds = definition.TargetIds });
            }
        result["appliedEndorsements"] = endorsements;
        if (cycle is not null) result["context"] = new { quoteId, cycleId = cycle.Id, revisionId = cycle.QuoteRevisionId, pricingInputHash = Convert.ToHexStringLower(cycle.PricingInputHash),
            cycle.ClientId, cycle.RelationshipId, cycle.ProductVersionId, cycle.AgencyTermsVersionId, cycle.RatingRuleVersionId, cycle.BinderVersionId, cycle.AuthorityVersionId };
        if (cycle is not null) result["jobId"] = cycle.WorkId;
        if (rating is not null) result["ratingId"] = rating.Id;
        var submission = cycle is null ? null : await db.Set<QuoteSubmission>().AsNoTracking().Where(x => x.CycleId == cycle.Id).OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(token);
        if (submission is not null)
        {
            result["submissionId"] = submission.Id;
            if (submission.AssignedUserId is Guid assignedUserId)
            {
                result["assignedUserId"] = assignedUserId;
                result["assignedUserLabel"] = await db.Set<StaffUser>().Where(x => x.Id == assignedUserId).Select(x => x.DisplayName).SingleAsync(token);
            }
            if (submission.AssignedTeamId is Guid teamId)
            {
                result["assignedTeamId"] = teamId;
                result["assignedTeamLabel"] = await db.Set<Team>().Where(x => x.Id == teamId).Select(x => x.Name).SingleAsync(token);
            }
        }
        var refreshOptions = new List<object>();
        if (writable && quote.State == "draft" && quote.CaptureClosedAt is null && term.Term is not null && owned.Scope.Actor.HasCapability("quote-revise"))
        {
            try
            {
                var settings = await QuoteCaptureEligibility.LoadSettingsAsync(db, now, token);
                foreach (var pin in settings.Products.Values.OrderBy(x => x.ProductVersionId))
                {
                    try
                    {
                        var candidate = await QuoteCaptureEligibility.ResolveAsync(db, owned.Scope, pin.ProductVersionId, now, token: token);
                        if (candidate.Product.Id != quote.ProductId || candidate.ProductVersion.Id == revision.ProductVersionId && candidate.Terms.Id == revision.AgencyTermsVersionId) continue;
                        _ = await QuoteRatingEligibility.ResolveAsync(db, owned, candidate.ProductVersion.Id, candidate.Terms.Id, term.Term, now, token);
                        refreshOptions.Add(new { productVersionId = candidate.ProductVersion.Id, displayName = candidate.Product.Name,
                            versionLabel = "v" + candidate.ProductVersion.Version.ToString(CultureInfo.InvariantCulture), agencyTermsVersionId = candidate.Terms.Id,
                            termsVersion = candidate.Terms.Version, effectiveFrom = candidate.Terms.EffectiveFrom });
                    }
                    catch (QuoteOperationException) { /* Unavailable versions are not refresh offers. */ }
                }
            }
            catch (QuoteOperationException) { /* Assessment blockers already explain unavailable configuration. */ }
        }
        result["refreshOptions"] = refreshOptions;
        return result;
    }
    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}

public sealed record UnderwritingReadBlocker(string Code, string Message,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Path = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? TargetId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Dimension = null);
