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
        var grants = eligible is not null && term.Term is not null ? await QuoteUnderwritingScope.GrantsAsync(db, owned, revision.ProductVersionId, eligible.BinderVersion,
            "commercial-combined", term.Term, now, token) : [];
        IReadOnlyList<UnderwritingProofRequirement> proofs = [];
        var authorityViews = new List<object>();
        if (current && cycle is not null && rating is { Outcome: "rated" } && eligible is not null)
        {
            proofs = await UnderwritingEvidenceService.Requirements(db, cycle, revision, StoredRatingInput.Read(cycle), token);
            foreach (var proof in proofs.Where(x => !x.Satisfied && x.Code != "capacity-response")) blockers.Add(new UnderwritingReadBlocker("commercial-proof-review-required", proof.Label + " requires current reviewed evidence.", proof.Path, proof.RiskItemId));
            var referrals = await db.Set<QuoteReferral>().AsNoTracking().Where(x => x.CycleId == cycle.Id && x.State != "superseded" && x.State != "approved").ToArrayAsync(token);
            foreach (var referral in referrals) blockers.Add(new UnderwritingReadBlocker(referral.RuleCode, referral.Reason, TargetId: referral.RiskItemId, Dimension: referral.Dimension));
            var binderIssues = CommercialReferralRules.AssessAuthority(eligible.Binder, proposal.RootElement, rating.AnnualPremium);
            object View(EffectiveUnderwritingGrant? grant)
            {
                var actorIssues = grant is null ? binderIssues : CommercialReferralRules.AssessAuthority(grant.Definition, proposal.RootElement, rating.AnnualPremium);
                var rows = new List<object>();
                var facts = StoredRatingInput.Read(cycle).Commercial!.Rating;
                void Limit(string code, string label, decimal? requested, string field)
                {
                    var binderLimit = decimal.Parse(eligible.Binder.GetProperty("limits").GetProperty(field).GetString()!, CultureInfo.InvariantCulture);
                    var actorLimit = grant is null ? (decimal?)null : decimal.Parse(grant.Definition.GetProperty("limits").GetProperty(field).GetString()!, CultureInfo.InvariantCulture);
                    rows.Add(new { code, label, requested = requested?.ToString("F2", CultureInfo.InvariantCulture) ?? "Whole-book check at issue",
                        actorLimit = actorLimit?.ToString("F2", CultureInfo.InvariantCulture) ?? "No current grant", binderLimit = binderLimit.ToString("F2", CultureInfo.InvariantCulture),
                        actorAllows = requested is not null && actorLimit is not null && requested <= actorLimit, binderAllows = requested is not null && requested <= binderLimit });
                }
                Limit("premium", "Annual premium", rating.AnnualPremium, "annualPremium");
                foreach (var location in facts.Locations)
                {
                    var total = location.Buildings + location.Contents + location.Stock;
                    Limit("location:" + location.Id, "Location sum insured", total, "singleLocation");
                    Limit("mel:" + location.Id, "Conservative location loss proxy", total, "maximumEstimatedLoss");
                }
                Limit("district", "Postcode district exposure", null, "districtProperty");
                Limit("employers", "Employers liability", facts.EmployersLimit, "employersLiability");
                Limit("public", "Public liability", facts.PublicLimit, "publicLiability");
                Limit("products", "Products liability", facts.ProductsLimit, "productsLiability");
                Limit("bi", "Business interruption", facts.BiSumInsured, "businessInterruption");
                Limit("works", "Contract works", facts.ContractWorks, "contractWorks");
                rows.AddRange(binderIssues.Concat(actorIssues).DistinctBy(x => new { x.RuleCode, x.TargetId }).Select(x => new {
                    code = x.RuleCode + (x.TargetId is Guid id ? ":" + id : ""), label = x.Dimension.Replace('-', ' '),
                    requested = x.RequestedAmount?.ToString("F2", CultureInfo.InvariantCulture) ?? "Review required",
                    actorLimit = grant is null ? "No current grant" : actorIssues.FirstOrDefault(a => a.RuleCode == x.RuleCode && a.TargetId == x.TargetId)?.AuthorisedAmount?.ToString("F2", CultureInfo.InvariantCulture) ?? "Published review authority",
                    binderLimit = binderIssues.FirstOrDefault(a => a.RuleCode == x.RuleCode && a.TargetId == x.TargetId)?.AuthorisedAmount?.ToString("F2", CultureInfo.InvariantCulture) ?? "Published review authority",
                    actorAllows = grant is not null && !actorIssues.Any(a => a.RuleCode == x.RuleCode && a.TargetId == x.TargetId),
                    binderAllows = !binderIssues.Any(a => a.RuleCode == x.RuleCode && a.TargetId == x.TargetId) }));
                var view = new Dictionary<string, object> { ["hasCurrentGrant"] = grant is not null, ["rows"] = rows.ToArray() };
                if (grant is not null) view["authorityVersionId"] = grant.Version.Id;
                return view;
            }
            if (grants.Count == 0) authorityViews.Add(View(null)); else foreach (var grant in grants) authorityViews.Add(View(grant));
        }
        var canPrepareTerms = false; var canSend = false; var canAccept = false; QuoteTermsVersion? currentTerms = null;
        var acceptanceCurrent = false;
        if (current && cycle is not null && eligible is not null && rating is { Outcome: "rated" } && rating.ExpiresAt > now && quote.State is not ("draft" or "bound" or "withdrawn"))
        {
            var held = new UnderwritingDecisionContext(owned, cycle, revision, rating, StoredRatingInput.Read(cycle), eligible, grants);
            try
            {
                await QuoteTermsService.Ready(db, held, now, false, token);
                canPrepareTerms = owned.Scope.Actor.HasCapability("quote-terms") && await db.Set<TemplateVersion>().AnyAsync(x => x.Kind == "quote-terms" && x.ProductId == cycle.ProductId && x.State == "published" && x.EffectiveFrom <= now && now < x.EffectiveTo, token);
                if (cycle.CurrentTermsVersionId is Guid termsId)
                {
                    currentTerms = await QuoteTermsService.CurrentTerms(db, held, termsId, now, token);
                    await QuoteTermsService.Ready(db, held, now, true, token);
                    canSend = owned.Scope.Actor.HasCapability("quote-terms");
                    var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == cycle.CurrentDeliveryId && x.TermsVersionId == termsId && x.State == "delivered", token);
                    if (delivery is not null)
                    {
                        var recipients = JsonSerializer.Deserialize<QuoteTermsRecipient[]>(delivery.RecipientSnapshotJson, QuoteRatingService.Json)!;
                        var actual = await QuoteTermsService.Recipients(db, held, recipients.Select(x => x.Id).ToArray(), token);
                        canAccept = recipients.SequenceEqual(actual) && owned.Scope.Actor.HasCapability("quote-acceptance") && proofs.Any(x => x.Code == "acceptance-proof" && x.TermsVersionId == termsId && x.Satisfied);
                        var assurance = await UnderwritingEvidenceService.Assurance(db, cycle, revision, token);
                        acceptanceCurrent = recipients.SequenceEqual(actual) && await db.Set<QuoteAcceptance>().AnyAsync(x => x.Id == cycle.CurrentAcceptanceId && x.TermsVersionId == termsId && x.DeliveryId == delivery.Id && x.TermsHash == currentTerms.TermsHash && x.AssuranceHash == assurance, token);
                    }
                }
            }
            catch (QuoteOperationException error) { blockers.Add(new UnderwritingReadBlocker(error.Code, "Complete the current quotation prerequisites.")); }
        }
        blockers.Add(new UnderwritingReadBlocker("commercial-issue-progression-pending", "Commercial Combined policy issue and whole-book exposure checks are not yet available."));
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
                canSubmit = writable && current && ready && cycle?.State == "rated" && rating?.ExpiresAt > now && quote.State is "rated" or "referred" && owned.Scope.Actor.HasCapability("quote-submit"), canReviewEvidence = writable && current && grants.Count > 0 && cycle?.State == "rated" && owned.Scope.Actor.HasCapability("underwriting-evidence-review"),
                canDecide = writable && current && grants.Count > 0 && cycle?.State == "rated" && rating?.ExpiresAt > now && owned.Scope.Actor.HasCapability("underwriting-decide-within-authority"),
                canEscalate = writable && current && grants.Count > 0 && cycle?.State == "rated" && rating?.ExpiresAt > now && owned.Scope.Actor.HasCapability("underwriting-escalate"), canPrepareTerms, canSend, canAccept, canIssue = false },
            ["proofRequirements"] = proofs, ["authorityViews"] = authorityViews, ["appliedEndorsements"] = Array.Empty<object>(), ["refreshOptions"] = Array.Empty<object>()
        };
        if (cycle is not null)
        {
            result["context"] = new { quoteId = quote.Id, cycleId = cycle.Id, revisionId = cycle.QuoteRevisionId, pricingInputHash = Convert.ToHexStringLower(cycle.PricingInputHash),
                cycle.ClientId, cycle.RelationshipId, cycle.ProductVersionId, cycle.AgencyTermsVersionId, cycle.RatingRuleVersionId, cycle.BinderVersionId, cycle.AuthorityVersionId };
            result["jobId"] = cycle.WorkId;
        }
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
        if (rating is not null) result["ratingId"] = rating.Id;
        if (cycle is not null) result["assuranceHash"] = await UnderwritingEvidenceService.Assurance(db, cycle, revision, token);
        if (currentTerms is not null) { result["termsVersionId"] = currentTerms.Id; result["termsHash"] = currentTerms.TermsHash; }
        if (acceptanceCurrent && cycle?.CurrentAcceptanceId is Guid acceptanceId) result["acceptanceId"] = acceptanceId;
        return result;
    }
}
