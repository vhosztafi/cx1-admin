using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingPostingReceipt(Guid ObligationId, Guid JournalId, Guid AccountingPeriodId,
    DateOnly PostingDate, decimal GrossDue, decimal InvoiceDue, decimal NetDue, decimal BrokerPayable);

// Infrastructure primitive only. No endpoint accepts ledger amounts or parties.
// The issue command owns current actor/authority and one transaction around the
// entire policy graph. Storage also derives the exact movement set independently.
public static class ServicingPostingService
{
    public static async Task<ServicingPostingReceipt> WriteAsync(BackOfficeDbContext db, Guid transactionId, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Servicing posting requires one held issue transaction.");
        var transaction = await db.Set<PolicyTransaction>().AsNoTracking().SingleAsync(x => x.Id == transactionId, token);
        if (transaction.Kind is not("adjustment" or "renewal")) throw new QuoteOperationException(409, "unsupported-servicing-posting");
        var policy = await db.Set<Policy>().AsNoTracking().SingleAsync(x => x.Id == transaction.PolicyId, token);
        var term = await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x => x.Id == transaction.TermId, token);
        var cycle = await db.Set<ServicingCycle>().FromSqlInterpolated($"SELECT * FROM ServicingCycle WITH(UPDLOCK,HOLDLOCK) WHERE Id={transaction.ServicingCycleId}").AsNoTracking().SingleAsync(token);
        var rating = await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x => x.Id == transaction.ServicingRatingId, token);
        if (cycle.State != "rated" || cycle.CurrentRatingId != rating.Id || cycle.CurrentAcceptanceId != transaction.ServicingAcceptanceId || rating.ExpiresAt <= transaction.ProcessedAt)
            throw new QuoteOperationException(409, "servicing-posting-source-changed");
        var terms = await db.Set<AgencyTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == cycle.AgencyTermsVersionId, token);
        var provider = await db.Set<BinderVersion>().Where(x => x.Id == cycle.BinderVersionId).Select(x => x.ProviderId).SingleAsync(token);
        var outcome = JsonSerializer.Deserialize<ServicingRatingOutcome>(rating.ResultJson, ServicingRatingService.Json);
        var rated = outcome?.Rating ?? throw new QuoteOperationException(409, "servicing-posting-rating-invalid");
        using var commercial = JsonDocument.Parse(terms.Snapshot); var root = commercial.RootElement;
        var share = root.GetProperty("commercialTerms").GetProperty("feeSharing").GetString() == "agreed-split" ? root.GetProperty("commercialTerms").GetProperty("feeShareBasisPoints").GetInt32() : 0;
        var movements = new List<ServicingPostingMovement>();
        foreach (var (slice, index) in rated.Slices.Select((slice, index) => (slice, index)))
        {
            movements.Add(new("premium", index + 1, slice.Premium, slice.EffectiveAt, slice.CoverageEndsAt));
            movements.Add(new("tax", index + 1, slice.Tax, slice.EffectiveAt, slice.CoverageEndsAt));
            movements.Add(new("commission", index + 1, slice.BrokerCommission, slice.EffectiveAt, slice.CoverageEndsAt));
        }
        movements.Add(new("fee", 1, rating.Fee, transaction.EffectiveAt, term.EndsAt));
        movements.Add(new("fee-share", 1, decimal.Round(rating.Fee * share / 10000m, 2, MidpointRounding.AwayFromZero), transaction.EffectiveAt, term.EndsAt));
        var posting = ServicingPostingRules.Calculate(new(transaction.Kind, root.GetProperty("settlement").GetProperty("premiumCollection").GetString()!,
            root.GetProperty("settlement").GetProperty("commissionSettlement").GetString()!, movements));
        var period = await AccountingPeriods.HoldAsync(db, transaction.ProcessedAt, token);
        var obligation = new IssueFinancialObligation { PolicyId = policy.Id, TermId = term.Id, TransactionId = transaction.Id, Purpose = transaction.Kind,
            AgencyId = policy.AgencyId, ClientId = policy.ClientId, RelationshipId = policy.RelationshipId, ProviderId = provider, AgencyTermsVersionId = terms.Id,
            DebtorKind = posting.DebtorKind, DebtorAgencyId = posting.DebtorKind == "agency" ? policy.AgencyId : null,
            DebtorRelationshipId = posting.DebtorKind == "relationship" ? policy.RelationshipId : null, Settlement = posting.Settlement,
            Premium = posting.Premium, Tax = posting.Tax, Fee = posting.Fee, Commission = posting.Commission, FeeShare = posting.FeeShare,
            GrossDue = posting.GrossDue, InvoiceDue = posting.InvoiceDue, NetDue = posting.NetDue, BrokerPayable = posting.BrokerPayable,
            TermsSnapshotJson = terms.Snapshot, CreatedAt = transaction.ProcessedAt, CreatedBy = transaction.CreatedBy };
        db.Add(obligation); await db.SaveChangesAsync(token);
        var components = movements.Select(x => new IssueFinancialComponent { ObligationId = obligation.Id, TransactionId = transaction.Id,
            Code = x.Code, Ordinal = x.Ordinal, Amount = x.Amount, CoverageStartsAt = x.StartsAt, CoverageEndsAt = x.EndsAt,
            OriginalComponentId = x.OriginalComponentId, CreatedAt = transaction.ProcessedAt, CreatedBy = transaction.CreatedBy }).ToArray();
        db.AddRange(components); await db.SaveChangesAsync(token);
        var journal = new Journal { TransactionId = transaction.Id, ObligationId = obligation.Id, Purpose = transaction.Kind,
            AccountingPeriodId = period.PeriodId, PostingDate = period.PostingDate, CreatedAt = transaction.ProcessedAt, CreatedBy = transaction.CreatedBy };
        db.Add(journal); await db.SaveChangesAsync(token);
        foreach (var line in posting.Lines)
            db.Add(new JournalLine { JournalId = journal.Id, TransactionId = transaction.Id,
                SourceComponentId = components.Single(x => x.Code == line.Code && x.Ordinal == line.Ordinal).Id,
                ComponentCode = line.Code, AccountCode = line.AccountCode, Debit = line.Debit, Credit = line.Credit,
                CoverageStartsAt = line.StartsAt, CoverageEndsAt = line.EndsAt,
                PartyKind = line.AccountCode == "fee-income" ? "internal" : line.AccountCode == "insurer-payable" ? "provider" : line.AccountCode == "relationship-receivable" ? "relationship" : "agency",
                PartyId = line.AccountCode == "fee-income" ? null : line.AccountCode == "insurer-payable" ? provider : line.AccountCode == "relationship-receivable" ? policy.RelationshipId : policy.AgencyId,
                CreatedAt = transaction.ProcessedAt, CreatedBy = transaction.CreatedBy });
        await db.SaveChangesAsync(token); journal.PostedAt = transaction.ProcessedAt; await db.SaveChangesAsync(token);
        return new(obligation.Id, journal.Id, period.PeriodId, period.PostingDate, posting.GrossDue, posting.InvoiceDue, posting.NetDue, posting.BrokerPayable);
    }
}
