using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed partial class QuoteUnderwritingReadModel
{
    private static async Task<Dictionary<string, object>> AssessCommercial(BackOfficeDbContext db, OwnedQuoteScope owned,
        QuoteRevision revision, UnderwritingCycle? cycle, QuoteRatingResult? rating, DateTimeOffset now, CancellationToken token)
    {
        var quote = owned.Quote;
        var blockers = new List<object>(); EligibleQuoteRating? eligible = null; var ready = false;
        using var proposal = JsonDocument.Parse(revision.ProposalJson);
        using var intent = JsonDocument.Parse(revision.TermIntentJson);
        var term = QuoteTerm.Assess(intent.RootElement);
        if (term.Term is null) foreach (var issue in term.Issues) blockers.Add(new UnderwritingReadBlocker(issue.Code, "Complete the policy term.", issue.Path));
        else
        {
            try
            {
                eligible = await QuoteRatingEligibility.ResolveAsync(db, owned, revision.ProductVersionId, revision.AgencyTermsVersionId, term.Term, now, token);
                QuoteRules.EnsureRetainedPins(QuoteService.Pins(revision), eligible.Capture.Pins);
                var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
                var input = CommercialUnderwritingInput.Project(proposal.RootElement, eligible.Capture.Pins, eligible.Rating, day);
                _ = CommercialRatingRules.Calculate(eligible.Rating, input.Rating, input.Term, eligible.CommissionBasisPoints, eligible.MinimumPremium);
                var matching = await QuoteMatching.AssessAsync(db, quote, now, token);
                var closure = quote.CaptureClosedAt is null || cycle is not null && cycle.QuoteRevisionId == revision.Id && cycle.State == "rated" ? null : "quote-capture-closed";
                var readiness = QuoteReadiness.Assess(quote.Id, revision.Id, proposal.RootElement, term, closure, day,
                    new Dictionary<Guid, string>(), matchingCode: matching.Code, includeEvidence: false);
                ready = readiness.Ready;
                foreach (var issue in readiness.Issues) blockers.Add(new UnderwritingReadBlocker(issue.Code, issue.Message, issue.Path));
            }
            catch (QuoteOperationException error) { blockers.Add(new UnderwritingReadBlocker(error.Code, "Current product, agency and underwriting configuration must be available.")); eligible = null; }
            catch (QuoteInputException error) { blockers.Add(new UnderwritingReadBlocker(error.Code, "Refresh the saved version selection.")); eligible = null; }
            catch (QuoteValidationException error) { foreach (var issue in error.Issues) blockers.Add(new UnderwritingReadBlocker(issue.Code, "Complete or correct the Commercial Combined details.", issue.Path)); }
            catch (ArgumentException) { blockers.Add(new UnderwritingReadBlocker("commercial-rating-input-invalid", "The captured amounts or selected cover are outside the published demo rating limits.")); }
        }
        var current = eligible is not null && cycle is not null && cycle.QuoteRevisionId == revision.Id && cycle.RatingRuleVersionId == eligible.RatingVersion.Id &&
            cycle.BinderVersionId == eligible.BinderVersion.Id && cycle.AuthorityVersionId == eligible.AuthorityVersion.Id &&
            cycle.AgencyId == quote.AgencyId && cycle.ClientId == quote.ClientId && cycle.RelationshipId == quote.RelationshipId && cycle.ProductId == quote.ProductId;
        if (current && cycle is not null && eligible is not null)
        {
            var stored = JsonSerializer.Deserialize<StoredRatingInput>(cycle.InputJson, QuoteRatingService.Json);
            current = stored?.IsCommercial == true && stored.RuntimeVersionId == eligible.RuntimeVersion.Id && stored.ScenarioVersionId == eligible.ScenarioVersion.Id &&
                stored.CommissionBasisPoints == eligible.CommissionBasisPoints && stored.MinimumPremium == eligible.MinimumPremium;
        }
        if (cycle is not null && !current) blockers.Add(new UnderwritingReadBlocker("underwriting-cycle-stale", "Return to draft or rate again using current configuration."));
        if (cycle?.State == "failed") blockers.Add(new UnderwritingReadBlocker("quote-rating-failed", "The rating did not apply. Review the job or return to draft."));
        if (rating is not null && rating.ExpiresAt <= now) blockers.Add(new UnderwritingReadBlocker("quote-rating-expired", "Obtain a current rating."));
        blockers.Add(new UnderwritingReadBlocker("commercial-underwriting-review-pending", "This demo price does not grant underwriting approval. Commercial evidence, referral and terms progression is not yet available."));
        var details = await (from version in db.Set<ProductVersion>().AsNoTracking()
                             join product in db.Set<Product>().AsNoTracking() on version.ProductId equals product.Id
                             join provider in db.Set<CapacityProvider>().AsNoTracking() on version.ProviderId equals provider.Id
                             where version.Id == revision.ProductVersionId && product.Id == quote.ProductId
                             select new { product.Name, version.Version, Provider = provider.Name, ProviderId = provider.Id }).SingleAsync(token);
        var writable = owned.Scope.Agency.State == "active" && owned.Scope.Client.IdentityState == "active" && owned.Scope.Relationship.State == "active";
        var result = new Dictionary<string, object> {
            ["quoteId"] = quote.Id, ["quoteEtag"] = "\"" + Convert.ToBase64String(quote.RowVersion) + "\"", ["state"] = quote.State, ["blockers"] = blockers.Take(200).ToList(),
            ["createdAt"] = quote.CreatedAt, ["productLabel"] = details.Name, ["productVersionLabel"] = "v" + details.Version.ToString(CultureInfo.InvariantCulture),
            ["providerLabel"] = details.Provider, ["providerId"] = details.ProviderId,
            ["capabilities"] = new { canRate = writable && ready && owned.Scope.Actor.HasCapability("quote-rate") && quote.State is "draft" or "rated" or "referred",
                canRevise = writable && cycle is not null && owned.Scope.Actor.HasCapability("quote-revise") && UnderwritingLifecycleRules.CanReturnToDraft(quote.State),
                canSubmit = false, canReviewEvidence = false, canDecide = false, canEscalate = false, canPrepareTerms = false, canSend = false, canAccept = false, canIssue = false },
            ["proofRequirements"] = Array.Empty<object>(), ["authorityViews"] = Array.Empty<object>(), ["appliedEndorsements"] = Array.Empty<object>(), ["refreshOptions"] = Array.Empty<object>()
        };
        if (cycle is not null)
        {
            result["context"] = new { quoteId = quote.Id, cycleId = cycle.Id, revisionId = cycle.QuoteRevisionId, pricingInputHash = Convert.ToHexStringLower(cycle.PricingInputHash),
                cycle.ClientId, cycle.RelationshipId, cycle.ProductVersionId, cycle.AgencyTermsVersionId, cycle.RatingRuleVersionId, cycle.BinderVersionId, cycle.AuthorityVersionId };
            result["jobId"] = cycle.WorkId;
        }
        if (rating is not null) result["ratingId"] = rating.Id;
        return result;
    }
}
