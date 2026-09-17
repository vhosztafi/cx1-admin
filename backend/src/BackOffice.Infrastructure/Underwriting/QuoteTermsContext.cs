using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed partial class QuoteTermsService
{
    internal static async Task Ready(BackOfficeDbContext db, UnderwritingDecisionContext held, DateTimeOffset now, bool signing, CancellationToken token)
    {
        if (held.Eligible.RuntimeVersion.Id != held.Input.RuntimeVersionId || held.Eligible.ScenarioVersion.Id != held.Input.ScenarioVersionId ||
            held.Eligible.CommissionBasisPoints != held.Input.CommissionBasisPoints || held.Eligible.MinimumPremium != held.Input.MinimumPremium)
            throw new QuoteOperationException(409, "underwriting-cycle-stale");
        if ((await QuoteMatching.AssessAsync(db, held.Owned.Quote, now, token)).Code is not null) throw new QuoteOperationException(409, "quote-matching-required");
        var conditions = await UnderwritingEvidenceService.ActiveConditions(db, held.Cycle.Id, token);
        using var proposal = JsonDocument.Parse(held.Revision.ProposalJson);
        var definitions = conditions.Select(x => QuoteReferralService.Parse(x.DefinitionJson, proposal.RootElement)).ToArray();
        foreach (var condition in conditions)
            if (condition.Code != "provide-signed-statement" || signing)
                if (!await UnderwritingEvidenceService.Resolved(db, condition, token)) throw new QuoteOperationException(409, "quote-condition-outstanding");
        foreach (var referral in await db.Set<QuoteReferral>().AsNoTracking().Where(x => x.CycleId == held.Cycle.Id).ToArrayAsync(token))
        {
            if (referral.State is "open" or "queried" or "declined" or "superseded" || referral.LatestDecisionId is null)
                throw new QuoteOperationException(409, "quote-referral-outstanding");
            if (await CapacityAuthority.HasBlockingRequest(db, held, referral.Id, now, token)) throw new QuoteOperationException(409, "capacity-response-required");
            var decision = await db.Set<QuoteReferralDecision>().AsNoTracking().SingleAsync(x => x.Id == referral.LatestDecisionId, token);
            if (decision.Outcome is not ("approve" or "approve-with-conditions")) throw new QuoteOperationException(409, "quote-referral-outstanding");
            // A retained decision records provenance; current revocation still
            // removes its power to authorise new terms or acceptance.
            var authority = await db.Set<AuthorityVersion>().FromSqlInterpolated($"SELECT * FROM AuthorityVersion WITH(HOLDLOCK) WHERE Id={decision.AuthorityVersionId}").AsNoTracking().SingleAsync(token);
            if (authority.State != "published" || authority.EffectiveFrom > now || now >= authority.EffectiveTo || authority.EffectiveFrom > held.Cycle.StartsAt || authority.EffectiveTo < held.Cycle.EndsAt ||
                !await db.Set<UserAuthorityGrant>().AnyAsync(x => x.UserId == decision.ActorId && x.AuthorityVersionId == authority.Id && x.RevokedAt == null && x.EffectiveFrom <= now && x.EffectiveTo > now && x.EffectiveFrom <= held.Cycle.StartsAt && x.EffectiveTo >= held.Cycle.EndsAt, token) ||
                !await db.Set<StaffUser>().AnyAsync(x => x.Id == decision.ActorId && x.State == "active" && x.AgencyId == null, token) ||
                !await (from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId == decision.ActorId && (role.Code == "underwriter" || role.Code == "senior-underwriter") select link.Id).AnyAsync(token))
                throw new QuoteOperationException(409, "quote-decision-authority-stale");
            using var definition = JsonDocument.Parse(authority.DefinitionJson);
            if (!await CapacityAuthority.Allows(db, held, definition.RootElement, definitions, now, token)) throw new QuoteOperationException(409, "quote-decision-authority-stale");
        }
        var proofs = await UnderwritingEvidenceService.Requirements(db, held.Cycle, held.Revision, held.Input, token);
        if (proofs.Any(x => !x.Satisfied && x.Code is not ("capacity-response" or "acceptance-proof") && (signing || x.Code != "signed-statement")))
            throw new QuoteOperationException(409, "quote-proof-review-required");
        if (signing && !proofs.Any(x => x.Code == "signed-statement" && x.TermsVersionId == held.Cycle.CurrentTermsVersionId && x.Satisfied))
            throw new QuoteOperationException(409, "quote-signature-required");
    }

    internal static async Task<JsonElement> Payload(BackOfficeDbContext db, UnderwritingDecisionContext held, TemplateVersion template, CancellationToken token)
    {
        using var proposal = JsonDocument.Parse(held.Revision.ProposalJson);
        using var commercial = JsonDocument.Parse(held.Eligible.Capture.Terms.Snapshot);
        using var content = JsonDocument.Parse(template.ContentJson);
        using var result = JsonDocument.Parse(held.Rating!.ResultJson);
        var conditions = (await UnderwritingEvidenceService.ActiveConditions(db, held.Cycle.Id, token))
            .Where(x => x.Kind != "documentary").OrderBy(x => x.Id).Select(x => new { id = x.Id, x.Code, x.Kind, x.Wording, x.EndorsementCode, x.DecisionId, definition = JsonSerializer.Deserialize<JsonElement>(x.DefinitionJson) }).ToArray();
        var root = proposal.RootElement; var terms = commercial.RootElement; var settlement = terms.GetProperty("settlement"); var agreement = terms.GetProperty("commercialTerms");
        string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        return JsonSerializer.SerializeToElement(new { format = "quote-contract-1", quoteId = held.Cycle.QuoteId, cycleId = held.Cycle.Id,
            revisionId = held.Revision.Id, revisionHash = Convert.ToHexStringLower(held.Revision.ContentHash), ratingId = held.Rating.Id,
            templateVersionId = template.Id, template = content.RootElement, clientId = held.Cycle.ClientId, relationshipId = held.Cycle.RelationshipId,
            insuredName = held.Owned.Scope.Client.LegalName, agencyName = held.Owned.Scope.Agency.LegalName,
            productVersionId = held.Cycle.ProductVersionId, agencyTermsVersionId = held.Cycle.AgencyTermsVersionId,
            productCode = root.GetProperty("productCode"), risk = root.GetProperty("risk"), cover = root.GetProperty("cover"), termIntent = root.GetProperty("termIntent"),
            startsAt = held.Cycle.StartsAt, endsAt = held.Cycle.EndsAt, expiresAt = held.Rating.ExpiresAt,
            conditions, rating = result.RootElement, price = new { currency = "GBP", annualPremium = Money(held.Rating.AnnualPremium), termPremium = Money(held.Rating.TermPremium),
                tax = Money(held.Rating.Tax), fee = Money(held.Rating.Fee), grossPayable = Money(held.Rating.GrossPayable), brokerCommission = Money(held.Rating.BrokerCommission) },
            settlement = new { collector = settlement.GetProperty("premiumCollection").GetString(), mode = settlement.GetProperty("commissionSettlement").GetString(),
                commissionRateBps = held.Eligible.CommissionBasisPoints, feeShareBps = agreement.GetProperty("feeSharing").GetString() == "agreed-split" ? agreement.GetProperty("feeShareBasisPoints").GetInt32() : 0 },
            documentState = "structured-payload" }, QuoteRatingService.Json);
    }
}
