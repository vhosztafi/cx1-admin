using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Finance;

public sealed record FinanceLedgerRow(string SourceKey, string SourceKind, string MovementKind, Guid AgencyId, Guid? RelationshipId,
    Guid? PolicyId, Guid? TransactionId, DateTimeOffset EffectiveAt, DateOnly PostingDate, DateTimeOffset PostedAt,
    string DebtorKind, Guid? DebtorId, string DebtorDelta, string ProviderDelta, string Currency, string? DueDate,
    string Status, Guid? AccountingPeriodId, string? GrossDue, string? Tax, string? Fee, string? Commission,
    string? NetDue, Guid? AgencyTermsVersionId = null);
public sealed record FinanceLedgerPage(int Page, int PageSize, int Total, IReadOnlyList<FinanceLedgerRow> Items);
public sealed record FinanceAccountSummary(Guid AgencyId, Guid? RelationshipId, int MovementCount,
    string AgencyReceivable, string RelationshipReceivable, string ProviderPayable, string Currency);
public sealed record FinanceTransactionDetail(Guid TransactionId, Guid PolicyId, Guid AgencyId, Guid RelationshipId,
    Guid ObligationId, Guid JournalId, Guid? AccountingPeriodId, DateOnly PostingDate, DateTimeOffset PostedAt,
    DateTimeOffset EffectiveAt, Guid AgencyTermsVersionId, string DebtorKind, string Settlement,
    string InvoiceDue, string BrokerPayable, string TermsSnapshotJson, FinanceLedgerRow Movement);

public sealed class FinanceLedgerService(IDbContextFactory<BackOfficeDbContext> factory)
{
    internal sealed record Entry(FinanceLedgerRow View, long Debtor, long Provider);

    public async Task<FinanceLedgerPage> ListAsync(ActorContext actor, Guid agencyId, Guid? relationshipId = null,
        DateOnly? from = null, DateOnly? to = null, int page = 1, int pageSize = 50, CancellationToken token = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (from is not null && to is not null && from >= to))
            throw new QuoteOperationException(400, "finance-query-invalid");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        await Authorize(db, actor, agencyId, relationshipId, token);
        var entries = await Load(db, agencyId, relationshipId, null, token);
        var filtered = entries.Where(x => (from is null || x.View.PostingDate >= from) && (to is null || x.View.PostingDate < to))
            .OrderByDescending(x => x.View.PostingDate).ThenByDescending(x => x.View.PostedAt).ThenBy(x => x.View.SourceKey, StringComparer.Ordinal).ToArray();
        var result = new FinanceLedgerPage(page, pageSize, filtered.Length,
            filtered.Skip(checked((page - 1) * pageSize)).Take(pageSize).Select(x => x.View).ToArray());
        await tx.CommitAsync(token);
        return result;
    }

    public async Task<FinanceAccountSummary> AccountAsync(ActorContext actor, Guid agencyId, Guid? relationshipId = null, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        await Authorize(db, actor, agencyId, relationshipId, token);
        var entries = await Load(db, agencyId, relationshipId, null, token);
        long agency = 0, relationship = 0, provider = 0;
        foreach (var entry in entries)
        {
            if (entry.View.DebtorKind == "agency") agency = checked(agency + entry.Debtor);
            else relationship = checked(relationship + entry.Debtor);
            provider = checked(provider + entry.Provider);
        }
        var result = new FinanceAccountSummary(agencyId, relationshipId, entries.Count,
            FinanceLedgerMath.Money(agency / 100m), FinanceLedgerMath.Money(relationship / 100m),
            FinanceLedgerMath.Money(provider / 100m), "GBP");
        await tx.CommitAsync(token);
        return result;
    }

    public async Task<FinanceTransactionDetail> TransactionAsync(ActorContext actor, Guid transactionId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        if (!actor.HasCapability("finance-read")) throw new QuoteOperationException(403, "finance-scope-denied");
        var obligation = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleOrDefaultAsync(x => x.TransactionId == transactionId, token)
            ?? throw new QuoteOperationException(404, "finance-transaction-not-found");
        await Authorize(db, actor, obligation.AgencyId, obligation.RelationshipId, token);
        var entries = await Load(db, obligation.AgencyId, obligation.RelationshipId, obligation.PolicyId, token);
        var movement = entries.SingleOrDefault(x => x.View.SourceKind == "insurance" && x.View.TransactionId == transactionId)?.View
            ?? throw new QuoteOperationException(404, "finance-transaction-not-posted");
        var journal = await db.Set<Journal>().AsNoTracking().SingleAsync(x => x.TransactionId == transactionId && x.PostedAt != null, token);
        var result = new FinanceTransactionDetail(transactionId, obligation.PolicyId, obligation.AgencyId,
            obligation.RelationshipId, obligation.Id, journal.Id, journal.AccountingPeriodId, movement.PostingDate,
            journal.PostedAt!.Value, movement.EffectiveAt, obligation.AgencyTermsVersionId, obligation.DebtorKind,
            obligation.Settlement, FinanceLedgerMath.Money(obligation.InvoiceDue), FinanceLedgerMath.Money(obligation.BrokerPayable),
            obligation.TermsSnapshotJson, movement);
        await tx.CommitAsync(token);
        return result;
    }

    public async Task<FinanceLedgerPage> PolicyAsync(ActorContext actor, Guid policyId, CancellationToken token = default)
    {
        if (!actor.HasCapability("policy-read") && !actor.HasCapability("finance-read"))
            throw new QuoteOperationException(403, "finance-scope-denied");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var policy = await db.Set<Policy>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == policyId, token)
            ?? throw new QuoteOperationException(404, "finance-policy-not-found");
        if (actor.HasCapability("policy-read")) await PolicyScope.Hold(db, actor, policyId, token);
        else await Authorize(db, actor, policy.AgencyId, policy.RelationshipId, token);
        var entries = await Load(db, policy.AgencyId, policy.RelationshipId, policyId, token);
        var result = new FinanceLedgerPage(1, Math.Max(1, entries.Count), entries.Count,
            entries.OrderBy(x => x.View.PostingDate).ThenBy(x => x.View.SourceKey, StringComparer.Ordinal).Select(x => x.View).ToArray());
        await tx.CommitAsync(token);
        return result;
    }

    internal static async Task Authorize(BackOfficeDbContext db, ActorContext actor, Guid agencyId, Guid? relationshipId, CancellationToken token,
        bool forWrite = false)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Finance read requires a held transaction.");
        if (!actor.HasCapability("finance-read")) throw new QuoteOperationException(403, "finance-scope-denied");
        // Match the shared agency -> user -> roles lock order used by quote scope.
        var agency = forWrite
            ? db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK) WHERE Id={agencyId}")
            : db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(HOLDLOCK) WHERE Id={agencyId}");
        if (!await agency.AnyAsync(token))
            throw new QuoteOperationException(404, "finance-agency-not-found");
        var identity = await IdentitySnapshot.Lock(db, new IdentityReference(actor.UserId, null), token);
        if (identity is null || !actor.Roles.SetEquals(identity.Roles.Select(x => x.Code)) || identity.User.AgencyId is not null ||
            !new ActorContext(identity.User.Id, identity.User.TeamId, identity.User.AgencyId, identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal)).HasCapability("finance-read"))
            throw new QuoteOperationException(403, "finance-scope-denied");
        if (relationshipId is Guid id && !await db.Set<ClientAgencyRelationship>().FromSqlInterpolated($"SELECT * FROM ClientAgencyRelationship WITH(HOLDLOCK) WHERE Id={id} AND AgencyId={agencyId}").AnyAsync(token))
            throw new QuoteOperationException(404, "finance-relationship-not-found");
    }

    internal static async Task<List<Entry>> Load(BackOfficeDbContext db, Guid agencyId, Guid? relationshipId, Guid? policyId, CancellationToken token,
        DateTimeOffset? cutoff = null)
    {
        var insurance = await (from j in db.Set<Journal>().AsNoTracking()
            join o in db.Set<IssueFinancialObligation>().AsNoTracking() on j.ObligationId equals o.Id
            join t in db.Set<PolicyTransaction>().AsNoTracking() on j.TransactionId equals t.Id
            where j.PostedAt != null && (cutoff == null || j.PostedAt <= cutoff) && o.AgencyId == agencyId && (relationshipId == null || o.RelationshipId == relationshipId)
                && (policyId == null || o.PolicyId == policyId)
            select new { Journal = j, Obligation = o, Transaction = t }).ToArrayAsync(token);
        var ids = insurance.Select(x => x.Journal.Id).ToArray();
        var providerAmounts = await db.Set<JournalLine>().AsNoTracking().Where(x => ids.Contains(x.JournalId) && x.AccountCode == "insurer-payable")
            .GroupBy(x => x.JournalId).Select(g => new { JournalId = g.Key, Amount = g.Sum(x => x.Credit - x.Debit) }).ToDictionaryAsync(x => x.JournalId, x => x.Amount, token);
        var entries = new List<Entry>(insurance.Length);
        foreach (var row in insurance)
        {
            var journal = row.Journal; var obligation = row.Obligation;
            var postedAt = journal.PostedAt!.Value;
            var postingDate = journal.PostingDate ?? FinanceLedgerMath.LegacyPostingDate(postedAt);
            var provider = providerAmounts.GetValueOrDefault(journal.Id);
            // Due date stays on the source processing basis even when a closed
            // period rolls posting forward to a later open period.
            var due = DueDate(obligation.TermsSnapshotJson, FinanceLedgerMath.LegacyPostingDate(postedAt));
            var view = new FinanceLedgerRow("insurance/" + journal.Id.ToString("N"), "insurance", journal.Purpose, obligation.AgencyId,
                obligation.RelationshipId, obligation.PolicyId, obligation.TransactionId, row.Transaction.EffectiveAt,
                postingDate, postedAt, obligation.DebtorKind, obligation.DebtorAgencyId ?? obligation.DebtorRelationshipId,
                FinanceLedgerMath.Money(obligation.InvoiceDue), FinanceLedgerMath.Money(provider), obligation.Currency,
                due?.ToString("yyyy-MM-dd"), obligation.InvoiceDue < 0 ? "credit" : obligation.InvoiceDue > 0 ? "outstanding" : "no-balance", journal.AccountingPeriodId,
                FinanceLedgerMath.Money(obligation.GrossDue), FinanceLedgerMath.Money(obligation.Tax),
                FinanceLedgerMath.Money(obligation.Fee), FinanceLedgerMath.Money(obligation.Commission),
                FinanceLedgerMath.Money(obligation.NetDue), obligation.AgencyTermsVersionId);
            entries.Add(new Entry(view, FinanceLedgerMath.Pence(obligation.InvoiceDue), FinanceLedgerMath.Pence(provider)));
        }
        var postings = await db.Set<FinancePosting>().AsNoTracking().Where(x => x.AgencyId == agencyId && (cutoff == null || x.PostedAt <= cutoff) &&
            (relationshipId == null || x.RelationshipId == relationshipId) && (policyId == null || x.PolicyId == policyId)).ToArrayAsync(token);
        foreach (var posting in postings)
        {
            var view = new FinanceLedgerRow(posting.SourceKind + "/" + posting.SourceId.ToString("N"), posting.SourceKind, posting.SourceKind,
                posting.AgencyId, posting.RelationshipId, posting.PolicyId, posting.TransactionId, posting.EffectiveAt,
                posting.PostingDate, posting.PostedAt, posting.DebtorKind,
                posting.DebtorKind == "agency" ? posting.AgencyId : posting.RelationshipId, FinanceLedgerMath.Money(posting.DebtorDelta),
                FinanceLedgerMath.Money(posting.ProviderDelta), posting.Currency, null, "posted", posting.AccountingPeriodId,
                null, null, null, null, null);
            entries.Add(new Entry(view, FinanceLedgerMath.Pence(posting.DebtorDelta), FinanceLedgerMath.Pence(posting.ProviderDelta)));
        }
        if (entries.Select(x => x.View.SourceKey).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidOperationException("Finance source appears twice in the ledger.");
        return entries;
    }

    private static DateOnly? DueDate(string termsJson, DateOnly postingDate)
    {
        using var document = JsonDocument.Parse(termsJson);
        if (!document.RootElement.TryGetProperty("paymentTermsDays", out var days) || !days.TryGetInt32(out var count) || count is < 0 or > 365)
            return null;
        return postingDate.AddDays(count);
    }
}
