using System.Globalization;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed class QuoteTermsReadModel(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public async Task<string> VersionAsync(ActorContext actor, Guid quoteId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        var version = await HistoryVersion(db, owned.Quote, token); await tx.CommitAsync(token); return version;
    }

    public async Task<(Dictionary<string, object> View, bool MoreTerms, bool MoreDeliveries, bool MoreAcceptances)> ReadAsync(
        ActorContext actor, Guid quoteId, string expectedVersion, int termsOffset, int deliveriesOffset, int acceptancesOffset, int size, CancellationToken token)
    {
        if (size is < 1 or > 100 || termsOffset < 0 || deliveriesOffset < 0 || acceptancesOffset < 0) throw new QuoteOperationException(400, "invalid-query");
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        if (!owned.Scope.Actor.HasCapability("underwriting-read")) throw new QuoteOperationException(403, "underwriting-access-denied");
        if (await HistoryVersion(db, owned.Quote, token) != expectedVersion) throw new QuoteOperationException(409, "quote-terms-history-changed");
        var terms = await db.Set<QuoteTermsVersion>().AsNoTracking().Where(x => x.QuoteId == quoteId).OrderByDescending(x => x.PreparedAt).ThenBy(x => x.Id).Skip(termsOffset).Take(size + 1).ToArrayAsync(token);
        var deliveries = await db.Set<QuoteTermsDelivery>().AsNoTracking().Where(x => x.QuoteId == quoteId).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(deliveriesOffset).Take(size + 1).ToArrayAsync(token);
        var acceptances = await db.Set<QuoteAcceptance>().AsNoTracking().Where(x => x.QuoteId == quoteId).OrderByDescending(x => x.RecordedAt).ThenBy(x => x.Id).Skip(acceptancesOffset).Take(size + 1).ToArrayAsync(token);
        var assessment = await new QuoteUnderwritingReadModel(factory, time).Assess(db, actor, quoteId, token);
        var termViews = new List<object>(); foreach (var row in terms.Take(size)) termViews.Add(await Terms(db, row, assessment, token));
        var deliveryViews = new List<object>();
        foreach (var row in deliveries.Take(size))
        {
            var recipients = JsonSerializer.Deserialize<QuoteTermsRecipient[]>(row.RecipientSnapshotJson, QuoteRatingService.Json)!;
            var attempts = await db.Set<AdapterAttempt>().AsNoTracking().Where(x => x.WorkId == row.WorkId).OrderBy(x => x.AttemptNumber).Select(x => new { number = x.AttemptNumber, x.Outcome, x.StartedAt, x.EndedAt, x.ErrorCode }).ToArrayAsync(token);
            var item = new Dictionary<string, object> { ["id"] = row.Id, ["termsVersionId"] = row.TermsVersionId, ["jobId"] = row.WorkId, ["state"] = row.State,
                ["recipientContactIds"] = recipients.Select(x => x.Id).ToArray(), ["recipients"] = recipients, ["payloadHash"] = row.PayloadHash, ["attempts"] = attempts, ["queuedAt"] = row.CreatedAt };
            if (row.CompletedAt is { } completed) item["completedAt"] = completed; if (row.OutcomeCode is { } error) item["errorCode"] = error;
            deliveryViews.Add(item);
        }
        var now = time.GetUtcNow();
        var templates = await db.Set<TemplateVersion>().AsNoTracking().Where(x => x.ProductId == owned.Quote.ProductId && x.Kind == "quote-terms" && x.State == "published" && x.EffectiveFrom <= now && now < x.EffectiveTo)
            .OrderBy(x => x.Code).ThenByDescending(x => x.Version).Select(x => new { x.Id, x.Code, x.Version, x.ContentJson }).Take(100).ToArrayAsync(token);
        var contacts = await db.Set<Contact>().AsNoTracking().Where(x => x.ClientId == owned.Quote.ClientId && x.RelationshipId == owned.Quote.RelationshipId && x.EndedAt == null && x.Email != null)
            .OrderBy(x => x.DeclaredFullName).ThenBy(x => x.Id).Select(x => new QuoteTermsRecipient(x.Id, x.DeclaredFullName, x.Email!)).Take(1000).ToArrayAsync(token);
        var view = new Dictionary<string, object> { ["terms"] = termViews, ["deliveries"] = deliveryViews,
            ["acceptances"] = acceptances.Take(size).Select(x => new { x.Id, x.QuoteId, x.CycleId, x.RatingId, x.TermsVersionId, x.DeliveryId, x.TermsHash, x.AssuranceHash, x.AccepterLabel, x.AcceptedAt, x.Channel, x.EvidenceAssociationId, x.RecordedBy, x.RecordedAt }).ToArray(),
            ["templates"] = templates.Select(x => new { x.Id, x.Code, x.Version, title = JsonSerializer.Deserialize<JsonElement>(x.ContentJson).GetProperty("title").GetString() }).ToArray(),
            ["recipientOptions"] = contacts.Where(x => System.Net.Mail.MailAddress.TryCreate(x.Email, out var mail) && mail.Address == x.Email).ToArray() };
        await tx.CommitAsync(token); return (view, terms.Length > size, deliveries.Length > size, acceptances.Length > size);
    }

    // Only this quote's history invalidates these cursors. Database-wide @@DBTS
    // also changes for unrelated sessions/jobs and cannot be a useful quotation
    // history generation on a busy back office. Current access is checked first.
    private static async Task<string> HistoryVersion(BackOfficeDbContext db, Quote quote, CancellationToken token)
    {
        var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().Where(x => x.QuoteId == quote.Id).OrderByDescending(x => x.RowVersion).Select(x => x.RowVersion).FirstOrDefaultAsync(token);
        return Convert.ToHexString(quote.RowVersion) + ":" + Convert.ToHexString(delivery ?? []);
    }

    private static async Task<object> Terms(BackOfficeDbContext db, QuoteTermsVersion row, Dictionary<string, object> assessment, CancellationToken token)
    {
        using var payload = JsonDocument.Parse(row.TermsJson); var document = payload.RootElement;
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == row.CycleId, token);
        var input = StoredRatingInput.ReadMotorTrade(cycle);
        var rating = await db.Set<QuoteRatingResult>().AsNoTracking().SingleAsync(x => x.Id == row.RatingId, token);
        var outcome = JsonSerializer.Deserialize<QuoteRatingOutcome>(rating.ResultJson, QuoteRatingService.Json)!;
        var revision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == cycle.QuoteRevisionId, token);
        string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        var cover = new List<object> { new { code = "road-risks", limit = Money(input.Input.CoverLimits["road-risks"]), excess = Money(input.Input.Pricing.GetProperty("cover").GetProperty("excess").GetDecimal()), targetIds = Array.Empty<Guid>() } };
        foreach (var section in document.GetProperty("cover").GetProperty("requestedSections").EnumerateArray().Where(x => x.GetProperty("selected").GetBoolean()))
            cover.Add(new { code = section.GetProperty("code").GetString(), limit = section.GetProperty("limit").GetString(), excess = section.GetProperty("excess").GetString(), targetIds = section.TryGetProperty("premisesIds", out var ids) ? ids.EnumerateArray().Select(x => x.GetGuid()).ToArray() : [] });
        var conditions = new List<object>(); var endorsements = new List<object>();
        foreach (var item in document.GetProperty("conditions").EnumerateArray())
        {
            var condition = await db.Set<QuoteCondition>().AsNoTracking().SingleAsync(x => x.Id == item.GetProperty("id").GetGuid(), token);
            conditions.Add(new { id = condition.Id, decisionId = condition.DecisionId, cycleId = condition.CycleId, etag = UnderwritingDecisionContext.Etag(condition.RowVersion), definition = item.GetProperty("definition").Clone(), state = await UnderwritingEvidenceService.Resolved(db, condition, token) ? "resolved" : "outstanding" });
            if (condition.Kind == "warranty")
            {
                using var proposal = JsonDocument.Parse(revision.ProposalJson); var definition = QuoteReferralService.Parse(condition.DefinitionJson, proposal.RootElement);
                endorsements.Add(new { code = condition.EndorsementCode!, version = "1", wording = condition.Wording, decisionId = condition.DecisionId, targetIds = definition.TargetIds });
            }
        }
        return new { row.Id, row.QuoteId, row.CycleId, revisionId = cycle.QuoteRevisionId, pricingInputHash = Convert.ToHexStringLower(cycle.PricingInputHash), row.RatingId,
            row.Number, row.TermsHash, row.AssuranceHashAtPreparation, row.TemplateVersionId, row.PreparedAt, row.PreparedBy, cover, endorsements, conditions,
            rating = new { id = rating.Id, rating.QuoteId, rating.CycleId, revisionId = revision.Id, pricingInputHash = Convert.ToHexStringLower(rating.InputHash), rating.RuleVersionId,
                rating.CompletedAt, rating.ExpiresAt, applicable = assessment.TryGetValue("ratingId", out var current) && current is Guid currentId && currentId == rating.Id &&
                    !((IEnumerable<object>)assessment["blockers"]).OfType<UnderwritingReadBlocker>().Any(x => x.Code is "underwriting-cycle-stale" or "quote-rating-expired" or "underwriting-product-unavailable" or "underwriting-configuration-unavailable" or "quote-terms-refresh-required"), currency = "GBP", annualPremium = Money(rating.AnnualPremium), termPremium = Money(rating.TermPremium), tax = Money(rating.Tax),
                fee = Money(rating.Fee), grossPayable = Money(rating.GrossPayable), brokerCommission = Money(rating.BrokerCommission), cycle.AgencyTermsVersionId,
                factors = outcome.Rating!.Factors.Select(x => new { x.Code, label = x.Code.Replace('-', ' '), amount = Money(x.Amount), x.Direction, basisAmount = Money(x.BasisAmount), x.BasisPoints }).ToArray(),
                input = JsonSerializer.Deserialize<JsonElement>(revision.ProposalJson), blockers = Array.Empty<object>() },
            cycle.AgencyTermsVersionId, settlement = document.GetProperty("settlement").Clone(), documentState = "structured-payload" };
    }
}
