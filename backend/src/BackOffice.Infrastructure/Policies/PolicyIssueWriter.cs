using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

internal sealed record IssuedPolicy(Policy Policy, PolicyTerm Term, PolicyVersion Version, PolicyTransaction Transaction, IssueFinancialObligation Obligation, Guid[] Documents, Guid? ExposureDecisionId = null);

internal static class PolicyIssueWriter
{
    internal static readonly string[] DocumentKinds = ["policy-schedule", "policy-certificate", "policy-statement"];
    internal static async Task<TemplateVersion[]> Templates(BackOfficeDbContext db, Guid productId, DateTimeOffset now, CancellationToken token, string[]? documentKinds = null)
    {
        var templates = await db.Set<TemplateVersion>().FromSqlInterpolated($"SELECT * FROM TemplateVersion WITH(HOLDLOCK) WHERE ProductId={productId}").AsNoTracking().ToArrayAsync(token);
        return (documentKinds ?? DocumentKinds).Select(kind => templates.Where(x => x.Kind == kind && x.State == "published" && x.EffectiveFrom <= now && now < x.EffectiveTo)
            .OrderByDescending(x => x.Version).ThenBy(x => x.Id).FirstOrDefault() ?? throw new QuoteOperationException(409, "policy-template-unavailable")).ToArray();
    }

    internal static async Task<IssuedPolicy> Write(BackOfficeDbContext db, UnderwritingDecisionContext held, EffectiveUnderwritingGrant grant,
        string reason, TemplateVersion[] templates, DateTimeOffset now, Guid correlationId, CancellationToken token, CommercialIssuePlan? commercialPlan = null)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Policy issue requires one held transaction.");
        if (held.Input.IsCommercial != (commercialPlan is not null)) throw new InvalidOperationException("Commercial issue requires its held capacity assessment.");
        if (held.Input.IsCommercial)
        {
            await CommercialExposureLock.RequireAsync(db, token);
            if (!templates.Select(x => x.Kind).SequenceEqual(CommercialDocumentSelection.Kinds(held.Input.Commercial!.Rating.EmployersSelected)))
                throw new InvalidOperationException("Commercial documents must match selected cover.");
        }
        var quote = held.Owned.Quote; var cycle = held.Cycle; var rating = held.Rating!; var actorId = held.Owned.Scope.Actor.UserId;
        using var commercial = JsonDocument.Parse(held.Eligible.Capture.Terms.Snapshot); var root = commercial.RootElement;
        var share = root.GetProperty("commercialTerms").GetProperty("feeSharing").GetString() == "agreed-split" ? root.GetProperty("commercialTerms").GetProperty("feeShareBasisPoints").GetInt32() : 0;
        var collector = root.GetProperty("settlement").GetProperty("premiumCollection").GetString()!;
        var posting = IssuePostingRules.Calculate(new(rating.TermPremium, rating.Tax, rating.Fee, rating.BrokerCommission, share, collector,
            root.GetProperty("settlement").GetProperty("commissionSettlement").GetString()!, cycle.StartsAt, cycle.EndsAt));
        var conditions = await UnderwritingEvidenceService.ActiveConditions(db, cycle.Id, token);
        var snapshot = PolicyIssueSnapshot.Create(held, grant.Version.Id, posting, collector, share, conditions);
        var policy = new Policy { Id = commercialPlan?.PolicyId ?? Guid.NewGuid(), SourceQuoteId = quote.Id, AgencyId = quote.AgencyId, ClientId = quote.ClientId, RelationshipId = quote.RelationshipId,
            ProductId = quote.ProductId, ReferencePrefix = held.Input.IsCommercial ? "PL-CC-" : "PL-MT-", CreatedAt = now, UpdatedAt = now, CreatedBy = actorId };
        db.Add(policy); await db.SaveChangesAsync(token);
        var term = new PolicyTerm { Id = commercialPlan?.TermId ?? Guid.NewGuid(), PolicyId = policy.Id, Number = 1, StartsAt = cycle.StartsAt, EndsAt = cycle.EndsAt, LocalTermIntentJson = held.Revision.TermIntentJson,
            ProductId = quote.ProductId, ProductVersionId = cycle.ProductVersionId, CreatedAt = now, UpdatedAt = now, CreatedBy = actorId };
        db.Add(term); await db.SaveChangesAsync(token);
        var transaction = new PolicyTransaction { PolicyId = policy.Id, TermId = term.Id, SourceQuoteId = quote.Id, CycleId = cycle.Id, QuoteRevisionId = held.Revision.Id,
            RatingId = rating.Id, AcceptanceId = cycle.CurrentAcceptanceId!.Value, Sequence = 1, EffectiveAt = cycle.StartsAt, ProcessedAt = now, Reason = reason,
            OperationKey = "policy-issue/" + quote.Id.ToString("N"), CreatedAt = now, CreatedBy = actorId };
        db.Add(transaction); await db.SaveChangesAsync(token);
        var version = new PolicyVersion { Id = commercialPlan?.VersionId ?? Guid.NewGuid(), PolicyId = policy.Id, TermId = term.Id, TransactionId = transaction.Id, Sequence = 1, SliceOrdinal = 1, SnapshotJson = snapshot,
            ContentHash = Hash(snapshot), EffectiveAt = term.StartsAt, ProcessedAt = now, CreatedAt = now, CreatedBy = actorId };
        db.Add(version); await db.SaveChangesAsync(token); policy.CurrentTermId = term.Id; term.CurrentVersionId = version.Id;
        Guid? exposureDecisionId = null;
        if (commercialPlan is not null)
        {
            var exposure = await CommercialExposureProjection.AppendAsync(db, version.Id, cycle.BinderVersionId, actorId, token);
            exposureDecisionId = await CommercialExposureService.Record(db, exposure, commercialPlan, actorId, token);
        }
        using var risk = JsonDocument.Parse(snapshot);
        foreach (var vehicle in risk.RootElement.GetProperty("risk").TryGetProperty("vehicles", out var vehicles) ? vehicles.EnumerateArray().ToArray() : [])
            db.Add(new PolicyRegistration { PolicyId = policy.Id, VersionId = version.Id, RiskItemId = vehicle.GetProperty("id").GetGuid(),
                NormalizedRegistration = vehicle.GetProperty("registration").GetString()!.Replace(" ", "").Replace("-", "").ToUpperInvariant() });
        await db.SaveChangesAsync(token);
        var provider = held.Eligible.BinderVersion.ProviderId;
        var obligation = new IssueFinancialObligation { PolicyId = policy.Id, TermId = term.Id, TransactionId = transaction.Id, AgencyId = quote.AgencyId, ClientId = quote.ClientId,
            RelationshipId = quote.RelationshipId, ProviderId = provider, AgencyTermsVersionId = cycle.AgencyTermsVersionId, DebtorKind = posting.DebtorKind,
            DebtorAgencyId = posting.DebtorKind == "agency" ? quote.AgencyId : null, DebtorRelationshipId = posting.DebtorKind == "relationship" ? quote.RelationshipId : null,
            Settlement = posting.EffectiveSettlement, Premium = rating.TermPremium, Tax = rating.Tax, Fee = rating.Fee, Commission = rating.BrokerCommission, FeeShare = posting.FeeShare,
            GrossDue = posting.GrossDue, InvoiceDue = posting.InvoiceDue, NetDue = posting.NetDue, BrokerPayable = posting.BrokerPayable,
            TermsSnapshotJson = held.Eligible.Capture.Terms.Snapshot, CreatedAt = now, CreatedBy = actorId };
        db.Add(obligation); await db.SaveChangesAsync(token);
        var components = posting.Components.Select(x => new IssueFinancialComponent { ObligationId = obligation.Id, TransactionId = transaction.Id, Code = x.Code, Amount = x.Amount,
            CoverageStartsAt = term.StartsAt, CoverageEndsAt = term.EndsAt, CreatedAt = now, CreatedBy = actorId }).ToArray();
        db.AddRange(components); await db.SaveChangesAsync(token);
        var heldPeriod = await AccountingPeriods.HoldAsync(db, now, token);
        var journal = new Journal { TransactionId = transaction.Id, ObligationId = obligation.Id, AccountingPeriodId = heldPeriod.PeriodId,
            PostingDate = heldPeriod.PostingDate, CreatedAt = now, CreatedBy = actorId }; db.Add(journal); await db.SaveChangesAsync(token);
        foreach (var line in posting.Lines)
            db.Add(new JournalLine { JournalId = journal.Id, TransactionId = transaction.Id, SourceComponentId = components.Single(x => x.Code == line.ComponentCode).Id,
                ComponentCode = line.ComponentCode, AccountCode = line.AccountCode, Debit = line.Debit, Credit = line.Credit, CoverageStartsAt = term.StartsAt, CoverageEndsAt = term.EndsAt,
                PartyKind = line.AccountCode == "fee-income" ? "internal" : line.AccountCode == "insurer-payable" ? "provider" : line.AccountCode == "relationship-receivable" ? "relationship" : "agency",
                PartyId = line.AccountCode == "fee-income" ? null : line.AccountCode == "insurer-payable" ? provider : line.AccountCode == "relationship-receivable" ? quote.RelationshipId : quote.AgencyId,
                CreatedAt = now, CreatedBy = actorId });
        await db.SaveChangesAsync(token); journal.PostedAt = now; await db.SaveChangesAsync(token);
        await FinanceEarningService.MaterializeObligationAsync(db, obligation.Id, token);
        var documents = new List<Guid>();
        foreach (var template in templates)
        {
            var request = new PolicyDocumentRequest { PolicyId = policy.Id, TermId = term.Id, TransactionId = transaction.Id, VersionId = version.Id, Kind = template.Kind,
                TemplateVersionId = template.Id, CreatedAt = now, UpdatedAt = now, CreatedBy = actorId };
            request.PayloadJson = JsonSerializer.Serialize(new { format = "policy-document-1", requestId = request.Id, policyId = policy.Id, policyReference = policy.Reference,
                termId = term.Id, transactionId = transaction.Id, versionId = version.Id, contentHash = Convert.ToHexStringLower(version.ContentHash),
                kind = request.Kind, templateVersionId = template.Id, template = JsonSerializer.Deserialize<JsonElement>(template.ContentJson), snapshot = risk.RootElement }, QuoteRatingService.Json);
            request.PayloadJson = CommercialDocumentRequestPayload.Complete(request.PayloadJson, version, term.EndsAt, request.Kind);
            request.PayloadHash = Hash(request.PayloadJson);
            var work = new OutboxWork { Kind = "policy-document", OperationKey = "policy-document/" + request.Id.ToString("N"), SubjectRecordId = request.Id,
                Payload = request.PayloadJson, CreatedAt = now, NextAttemptAt = now, CorrelationId = correlationId };
            db.Add(work); await db.SaveChangesAsync(token); request.WorkId = work.Id; db.Add(request); await db.SaveChangesAsync(token); documents.Add(request.Id);
        }
        if (!held.Input.IsCommercial) await Operations.MidSubmissionRegistration.InitialIntent(db, version, term, actorId, now, correlationId, token);
        return new(policy, term, version, transaction, obligation, documents.ToArray(), exposureDecisionId);
    }
    private static byte[] Hash(string json) => SHA256.HashData(Encoding.UTF8.GetBytes(json));
    internal static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
