using System.Data;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Finance;

public sealed record FinancePeriodReview(Guid Id, DateOnly From, DateOnly To, string State,
    IReadOnlyList<string> Blockers, DateTimeOffset? ClosedAt, Guid? ClosedBy,
    string? CloseReason, DateTimeOffset? SourceCutoff, string? CloseChecklistJson, string Etag);
public sealed record FinanceCorrectionView(Guid Id, Guid OriginalSourceId, string OriginalSourceKind,
    Guid PostingId, Guid AgencyId, DateOnly PostingDate, DateTimeOffset EffectiveAt,
    string DebtorDelta, string ProviderDelta, string CashDelta, string InternalDelta, string Reason);

public sealed class FinancePeriodService(IDbContextFactory<BackOfficeDbContext> factory,
    SqlCommandBoundary commands, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<FinancePeriodReview>> ListAsync(ActorContext actor,
        CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await Authorize(db, actor, "finance-read", token);
        var periods = await db.Set<AccountingPeriod>().AsNoTracking()
            .OrderBy(x => x.StartsOn).ToArrayAsync(token);
        var result = new List<FinancePeriodReview>(periods.Length);
        foreach (var period in periods)
            result.Add(View(period, period.State == "open" ? await Blockers(db, period, token) : []));
        await tx.CommitAsync(token);
        return result;
    }

    public async Task<FinancePeriodReview> ReviewAsync(ActorContext actor, Guid periodId,
        CancellationToken token = default)
    {
        if (periodId == Guid.Empty) throw new QuoteOperationException(400, "finance-period-input-invalid");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await Authorize(db, actor, "finance-read", token);
        var period = await db.Set<AccountingPeriod>().FromSqlInterpolated(
            $"SELECT * FROM AccountingPeriod WITH(HOLDLOCK) WHERE Id={periodId}").AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "finance-period-not-found");
        var blockers = period.State == "open" ? await Blockers(db, period, token) : [];
        await tx.CommitAsync(token);
        return View(period, blockers);
    }

    public Task<CommandOutcome> CloseAsync(ActorContext actor, Guid periodId, byte[] expectedVersion,
        string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        if (periodId == Guid.Empty || expectedVersion is not { Length: 8 } ||
            reason is not { Length: >= 10 and <= 1000 } || reason.Trim().Length < 10)
            throw new QuoteOperationException(400, "finance-period-input-invalid");
        reason = reason.Trim();
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
                $"/api/v1/finance/periods/{periodId:N}/close", key, correlationId),
            new { periodId, expectedVersion = Convert.ToBase64String(expectedVersion), reason },
            "finance.period.closed", (db, ct) => Authorize(db, actor, "finance-period-close", ct),
            async (db, ct) =>
            {
                var period = await db.Set<AccountingPeriod>().FromSqlInterpolated(
                    $"SELECT * FROM AccountingPeriod WITH(UPDLOCK,HOLDLOCK) WHERE Id={periodId}").SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "finance-period-not-found");
                if (!period.RowVersion.SequenceEqual(expectedVersion))
                    throw new QuoteOperationException(412, "finance-period-version-stale");
                if (period.State != "open") throw new QuoteOperationException(409, "finance-period-closed");
                var blockers = await Blockers(db, period, ct);
                if (blockers.Count != 0) throw new QuoteOperationException(409,
                    "finance-period-has-blockers:" + string.Join(',', blockers));
                var now = time.GetUtcNow();
                period.State = "closed"; period.ClosedAt = now; period.ClosedBy = actor.UserId;
                period.CloseReason = reason; period.SourceCutoff = now;
                period.CloseChecklistJson = JsonSerializer.Serialize(new
                {
                    reconciliation = "complete-or-not-applicable",
                    payments = "no-acknowledged-unapplied-or-pending",
                    statements = "current-for-every-active-agency-or-not-applicable",
                    bordereaux = "valid-for-every-active-provider-or-not-applicable",
                    checkedAt = now
                }, Json);
                await db.SaveChangesAsync(ct);
                return new CommandOutcome(period.Id, 200, JsonSerializer.Serialize(new
                { periodId = period.Id, state = period.State, closedAt = now }, Json));
            }, token, IsolationLevel.Serializable);
    }

    public Task<CommandOutcome> PostCorrectionAsync(ActorContext actor, string originalKind,
        Guid originalId, string debtor, string provider, string cash, string internalDelta,
        DateTimeOffset effectiveAt, string reason, string key, Guid correlationId,
        CancellationToken token = default)
    {
        if (originalKind is not ("insurance" or "finance-posting") || originalId == Guid.Empty ||
            effectiveAt.Offset != TimeSpan.Zero || effectiveAt > time.GetUtcNow() ||
            reason is not { Length: >= 10 and <= 1000 } || reason.Trim().Length < 10 ||
            !FinancePeriodMath.TryBalanced(debtor, provider, cash, internalDelta))
            throw new QuoteOperationException(400, "finance-correction-unbalanced-or-invalid");
        reason = reason.Trim();
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
                $"/api/v1/finance/corrections", key, correlationId),
            new { originalKind, originalId, debtor, provider, cash, internalDelta, effectiveAt, reason },
            "finance.correction.posted", (db, ct) => Authorize(db, actor, "finance-correction-post", ct),
            async (db, ct) =>
            {
                Guid agencyId; Guid? relationshipId; Guid? policyId; Guid? transactionId;
                string debtorKind; DateOnly oldPostingDate;
                if (originalKind == "insurance")
                {
                    var source = await (from journal in db.Set<Journal>().AsNoTracking()
                        join obligation in db.Set<IssueFinancialObligation>().AsNoTracking()
                            on journal.ObligationId equals obligation.Id
                        where journal.Id == originalId && journal.PostedAt != null
                        select new { journal, obligation }).SingleOrDefaultAsync(ct)
                        ?? throw new QuoteOperationException(404, "finance-correction-source-not-found");
                    agencyId = source.obligation.AgencyId; relationshipId = source.obligation.RelationshipId;
                    policyId = source.obligation.PolicyId; transactionId = source.obligation.TransactionId;
                    debtorKind = source.obligation.DebtorKind;
                    oldPostingDate = source.journal.PostingDate ??
                        FinanceLedgerMath.LegacyPostingDate(source.journal.PostedAt!.Value);
                    if (!await db.Set<AccountingPeriod>().AnyAsync(x => x.StartsOn <= oldPostingDate &&
                        oldPostingDate < x.EndsOn && x.State == "closed" &&
                        (source.journal.AccountingPeriodId == null || x.Id == source.journal.AccountingPeriodId), ct))
                        throw new QuoteOperationException(409, "finance-correction-source-period-open");
                }
                else
                {
                    var source = await db.Set<FinancePosting>().AsNoTracking()
                        .SingleOrDefaultAsync(x => x.Id == originalId, ct)
                        ?? throw new QuoteOperationException(404, "finance-correction-source-not-found");
                    agencyId = source.AgencyId; relationshipId = source.RelationshipId;
                    policyId = source.PolicyId; transactionId = source.TransactionId;
                    debtorKind = source.DebtorKind; oldPostingDate = source.PostingDate;
                    if (!await db.Set<AccountingPeriod>().AnyAsync(x => x.Id == source.AccountingPeriodId &&
                        x.State == "closed", ct))
                        throw new QuoteOperationException(409, "finance-correction-source-period-open");
                }
                await FinanceLedgerService.Authorize(db, actor, agencyId, relationshipId, ct, true);
                var now = time.GetUtcNow();
                var held = await AccountingPeriods.HoldAsync(db, now, ct);
                if (held.PostingDate <= oldPostingDate)
                    throw new QuoteOperationException(409, "finance-correction-needs-later-open-period");
                var correction = new FinanceCorrection
                {
                    OriginalSourceKind = originalKind, OriginalSourceId = originalId,
                    AgencyId = agencyId, RelationshipId = relationshipId, PolicyId = policyId,
                    TransactionId = transactionId, DebtorKind = debtorKind,
                    Reason = reason, CreatedAt = now, CreatedBy = actor.UserId
                };
                db.Add(correction); await db.SaveChangesAsync(ct);
                var posting = new FinancePosting
                {
                    SourceKind = "correction", SourceId = correction.Id, AgencyId = agencyId,
                    RelationshipId = relationshipId, PolicyId = policyId, TransactionId = transactionId,
                    DebtorKind = debtorKind, AccountingPeriodId = held.PeriodId,
                    PostingDate = held.PostingDate, EffectiveAt = effectiveAt, PostedAt = now,
                    CreatedAt = now, CreatedBy = actor.UserId, Reason = reason,
                    DebtorDelta = FinancePeriodMath.ParseOrZero(debtor),
                    ProviderDelta = FinancePeriodMath.ParseOrZero(provider),
                    CashDelta = FinancePeriodMath.ParseOrZero(cash),
                    InternalDelta = FinancePeriodMath.ParseOrZero(internalDelta)
                };
                db.Add(posting); await db.SaveChangesAsync(ct);
                return new CommandOutcome(correction.Id, 201, JsonSerializer.Serialize(new
                { id = correction.Id, postingId = posting.Id, accountingPeriodId = held.PeriodId,
                    postingDate = held.PostingDate }, Json));
            }, token, IsolationLevel.Serializable);
    }

    public async Task<FinanceCorrectionView> CorrectionAsync(ActorContext actor, Guid id,
        CancellationToken token = default)
    {
        if (id == Guid.Empty) throw new QuoteOperationException(400, "finance-correction-input-invalid");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await Authorize(db, actor, "finance-read", token);
        var correction = await db.Set<FinanceCorrection>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new QuoteOperationException(404, "finance-correction-not-found");
        await FinanceLedgerService.Authorize(db, actor, correction.AgencyId, correction.RelationshipId, token);
        var posting = await db.Set<FinancePosting>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.SourceKind == "correction" && x.SourceId == id, token)
            ?? throw new QuoteOperationException(404, "finance-correction-posting-not-found");
        await tx.CommitAsync(token);
        return new FinanceCorrectionView(correction.Id, correction.OriginalSourceId,
            correction.OriginalSourceKind, posting.Id, correction.AgencyId, posting.PostingDate,
            posting.EffectiveAt, FinanceLedgerMath.Money(posting.DebtorDelta),
            FinanceLedgerMath.Money(posting.ProviderDelta), FinanceLedgerMath.Money(posting.CashDelta),
            FinanceLedgerMath.Money(posting.InternalDelta), correction.Reason);
    }

    private static async Task Authorize(BackOfficeDbContext db, ActorContext actor,
        string capability, CancellationToken token)
    {
        if (!actor.HasCapability(capability)) throw new QuoteOperationException(403, "finance-period-scope-denied");
        var identity = await IdentitySnapshot.Lock(db, new IdentityReference(actor.UserId, null), token);
        if (identity is null || identity.User.State != "active" || identity.User.AgencyId is not null ||
            !actor.Roles.SetEquals(identity.Roles.Select(x => x.Code)) ||
            !new ActorContext(identity.User.Id, identity.User.TeamId, identity.User.AgencyId,
                identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal)).HasCapability(capability))
            throw new QuoteOperationException(403, "finance-period-scope-denied");
    }

    private static FinancePeriodReview View(AccountingPeriod period, IReadOnlyList<string> blockers)
        => new(period.Id, period.StartsOn, period.EndsOn, period.State, blockers,
            period.ClosedAt, period.ClosedBy, period.CloseReason, period.SourceCutoff,
            period.CloseChecklistJson, '"' + Convert.ToBase64String(period.RowVersion) + '"');

    private static async Task<List<string>> Blockers(BackOfficeDbContext db,
        AccountingPeriod period, CancellationToken token)
    {
        var blockers = new List<string>();
        var allJournals = await (from j in db.Set<Journal>().AsNoTracking()
            join o in db.Set<IssueFinancialObligation>().AsNoTracking() on j.ObligationId equals o.Id
            where j.PostedAt != null
            select new { j.PostedAt, j.PostingDate, j.AccountingPeriodId, o.AgencyId, o.ProviderId })
            .ToArrayAsync(token);
        var journals = allJournals.Where(x => x.AccountingPeriodId == period.Id ||
            x.AccountingPeriodId is null &&
            (x.PostingDate ?? FinanceLedgerMath.LegacyPostingDate(x.PostedAt!.Value)) >= period.StartsOn &&
            (x.PostingDate ?? FinanceLedgerMath.LegacyPostingDate(x.PostedAt!.Value)) < period.EndsOn).ToArray();
        var postings = await db.Set<FinancePosting>().AsNoTracking()
            .Where(x => x.AccountingPeriodId == period.Id).ToArrayAsync(token);
        var lines = await db.Set<BankLine>().AsNoTracking()
            .Where(x => x.ValueDate >= period.StartsOn && x.ValueDate < period.EndsOn).ToArrayAsync(token);
        var reconciliations = await db.Set<Reconciliation>().AsNoTracking()
            .Where(x => x.From < period.EndsOn && period.StartsOn < x.To).ToArrayAsync(token);
        if (reconciliations.Any(x => x.CompletedAt is null)) blockers.Add("reconciliation-open");
        if (lines.Any(x => !reconciliations.Any(r => r.AgencyId == x.AgencyId &&
            r.CompletedAt != null && r.From <= x.ValueDate && x.ValueDate < r.To)) ||
            postings.Any(x => x.CashDelta != 0 && !reconciliations.Any(r => r.AgencyId == x.AgencyId &&
                r.CompletedAt != null && r.From <= x.PostingDate && x.PostingDate < r.To)))
            blockers.Add("cash-or-bank-reconciliation-missing");
        var pendingPayments = await (from payment in db.Set<FinanceRefundPayment>().AsNoTracking()
            join request in db.Set<RefundRequest>().AsNoTracking() on payment.RefundRequestId equals request.Id
            join journal in db.Set<Journal>().AsNoTracking() on request.CreditObligationId equals journal.ObligationId
            where payment.State == "queued"
            select new { payment.Id, journal.AccountingPeriodId, journal.PostingDate, journal.PostedAt })
            .ToArrayAsync(token);
        if (pendingPayments.Any(x => x.AccountingPeriodId == period.Id ||
            x.AccountingPeriodId is null && x.PostedAt != null &&
            (x.PostingDate ?? FinanceLedgerMath.LegacyPostingDate(x.PostedAt.Value)) >= period.StartsOn &&
            (x.PostingDate ?? FinanceLedgerMath.LegacyPostingDate(x.PostedAt.Value)) < period.EndsOn))
            blockers.Add("refund-payment-pending");
        var unappliedKeys = await db.Set<FinanceRefundPayment>().AsNoTracking()
            .Where(x => x.State != "paid").Select(x => x.OperationKey).ToArrayAsync(token);
        var acceptedKeys = await db.Set<DemoProviderOperation>().AsNoTracking()
            .Where(x => x.Kind == "finance-refund-payment" && x.State == "accepted" && x.Result != null)
            .Select(x => x.OperationKey).ToArrayAsync(token);
        var acceptedUnapplied = acceptedKeys.Any(x => unappliedKeys.Contains(x, StringComparer.Ordinal));
        if (acceptedUnapplied) blockers.Add("refund-provider-acknowledged-unapplied");
        var agencies = journals.Select(x => x.AgencyId).Concat(postings.Select(x => x.AgencyId)).Distinct().ToArray();
        var statements = await db.Set<FinanceStatementVersion>().AsNoTracking()
            .Where(x => agencies.Contains(x.AgencyId) && x.From <= period.StartsOn && x.To >= period.EndsOn)
            .ToArrayAsync(token);
        if (agencies.Any(id => !statements.Any(s => s.AgencyId == id &&
            s.SourceCutoff >= journals.Where(j => j.AgencyId == id).Select(j => j.PostedAt)
                .Concat(postings.Where(p => p.AgencyId == id).Select(p => (DateTimeOffset?)p.PostedAt)).Max())))
            blockers.Add("agency-statement-missing-or-stale");
        var providers = journals.Select(x => x.ProviderId).Distinct().ToArray();
        var batches = await db.Set<FinanceBordereauBatch>().AsNoTracking()
            .Where(x => x.AccountingPeriodId == period.Id && providers.Contains(x.ProviderId)).ToArrayAsync(token);
        var currentVersionIds = batches.Select(b => b.CurrentVersionId).ToArray();
        var versions = await db.Set<FinanceBordereauVersion>().AsNoTracking()
            .Where(x => currentVersionIds.Contains(x.Id)).ToArrayAsync(token);
        if (providers.Any(id => !batches.Any(b => b.ProviderId == id &&
            versions.Any(v => v.Id == b.CurrentVersionId && v.State == "valid" &&
                v.SourceCutoff >= journals.Where(j => j.ProviderId == id).Select(j => j.PostedAt).Max()))))
            blockers.Add("provider-bordereau-missing-or-stale");
        return blockers;
    }
}
