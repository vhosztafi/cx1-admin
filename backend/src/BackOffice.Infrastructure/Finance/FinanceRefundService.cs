using System.Data;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Finance;

public sealed record RefundSourceInput(Guid AllocationId, string Amount);
public sealed record RefundSourceView(Guid AllocationId, Guid ReceiptId, string Amount);
public sealed record RefundDecisionView(Guid Id, string Kind, Guid ActorId, string Reason, DateTimeOffset DecidedAt);
public sealed record RefundView(Guid Id, Guid AgencyId, Guid PolicyId, Guid CreditObligationId,
    string DebtorKind, Guid DebtorId, string Amount, string Currency, string State,
    Guid RuleId, int RuleVersion, string ApprovalThreshold, int RequiredApprovals,
    Guid RequestedBy, DateTimeOffset RequestedAt, string Reason,
    IReadOnlyList<RefundSourceView> Sources, IReadOnlyList<RefundDecisionView> Decisions, string Etag);

public sealed class FinanceRefundService(IDbContextFactory<BackOfficeDbContext> factory,
    SqlCommandBoundary commands, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<CommandOutcome> RequestAsync(ActorContext actor, Guid creditObligationId,
        string amountText, IReadOnlyList<RefundSourceInput> sources, string reason,
        string key, Guid correlationId, CancellationToken token = default)
    {
        var amount = Amount(amountText);
        if (creditObligationId == Guid.Empty || sources is null || sources.Count is < 1 or > 100 ||
            sources.Any(x => x.AllocationId == Guid.Empty) ||
            sources.Select(x => x.AllocationId).Distinct().Count() != sources.Count || !Reason(reason))
            throw new QuoteOperationException(400, "refund-request-invalid");
        var parts = sources.Select(x => Amount(x.Amount)).ToArray();
        if (parts.Aggregate(0L, checked((sum, part) => sum + part)) != amount)
            throw new QuoteOperationException(400, "refund-source-total-mismatch");
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
                $"/api/v1/finance/credits/{creditObligationId:N}/refunds", key, correlationId),
            new { creditObligationId, amountText, sources, reason }, "finance.refund.requested",
            async (db, ct) =>
            {
                var credit = await db.Set<IssueFinancialObligation>().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == creditObligationId, ct)
                    ?? throw new QuoteOperationException(404, "refund-credit-not-found");
                await Authorize(db, actor, credit.AgencyId, credit.RelationshipId, ct, "finance-refund-request", true);
            },
            async (db, ct) =>
            {
                var credit = await db.Set<IssueFinancialObligation>()
                    .FromSqlInterpolated($"SELECT * FROM IssueFinancialObligation WITH(UPDLOCK,HOLDLOCK) WHERE Id={creditObligationId}")
                    .AsNoTracking().SingleAsync(ct);
                if (credit.InvoiceDue >= 0 || credit.Purpose != "cancellation" || credit.Currency != "GBP" ||
                    !await db.Set<Journal>().AnyAsync(x => x.ObligationId == credit.Id && x.PostedAt != null, ct))
                    throw new QuoteOperationException(409, "refund-credit-not-posted");
                var debtorId = DebtorId(credit);
                var sourceRows = new List<(Allocation Allocation, Receipt Receipt, long Pence)>(sources.Count);
                foreach (var input in sources.Select((value, index) => (value, index)).OrderBy(x => x.value.AllocationId))
                {
                    var allocation = await db.Set<Allocation>().AsNoTracking()
                        .SingleOrDefaultAsync(x => x.Id == input.value.AllocationId && x.ReversalOfId == null, ct)
                        ?? throw new QuoteOperationException(404, "refund-source-not-found");
                    var receipt = await db.Set<Receipt>()
                        .FromSqlInterpolated($"SELECT * FROM Receipt WITH(UPDLOCK,HOLDLOCK) WHERE Id={allocation.ReceiptId}")
                        .AsNoTracking().SingleAsync(ct);
                    sourceRows.Add((allocation, receipt, parts[input.index]));
                }
                foreach (var (allocation, receipt, pence) in sourceRows)
                {
                    var invoice = await db.Set<IssueFinancialObligation>().AsNoTracking()
                        .SingleAsync(x => x.Id == allocation.ObligationId, ct);
                    var assignment = await db.Set<ReceiptPayerAssignment>().AsNoTracking()
                        .Where(x => x.ReceiptId == receipt.Id).OrderByDescending(x => x.Ordinal).FirstAsync(ct);
                    if (receipt.AgencyId != credit.AgencyId || receipt.Currency != "GBP" ||
                        invoice.PolicyId != credit.PolicyId || invoice.InvoiceDue <= 0 ||
                        invoice.DebtorKind != credit.DebtorKind || DebtorId(invoice) != debtorId ||
                        assignment.PayerKind != credit.DebtorKind ||
                        (credit.DebtorKind == "agency" ? assignment.PayerAgencyId : assignment.PayerRelationshipId) != debtorId ||
                        await db.Set<Allocation>().AnyAsync(x => x.ReversalOfId == allocation.Id, ct))
                        throw new QuoteOperationException(409, "refund-source-payee-mismatch");
                    var used = await (from reservation in db.Set<RefundCashReservation>()
                        join activeRequest in db.Set<RefundRequest>() on reservation.RefundRequestId equals activeRequest.Id
                        where reservation.AllocationId == allocation.Id && activeRequest.State != "rejected"
                        select reservation.Amount).ToArrayAsync(ct);
                    if (pence > checked(FinanceLedgerMath.Pence(allocation.Amount) - used.Aggregate(0L,
                        (sum, value) => checked(sum + FinanceLedgerMath.Pence(value)))))
                        throw new QuoteOperationException(409, "refund-already-reserved");
                }
                var obligations = await db.Set<IssueFinancialObligation>().AsNoTracking()
                    .Where(x => x.PolicyId == credit.PolicyId && x.AgencyId == credit.AgencyId &&
                        x.DebtorKind == credit.DebtorKind).ToArrayAsync(ct);
                obligations = obligations.Where(x => DebtorId(x) == debtorId).ToArray();
                var positiveIds = obligations.Where(x => x.InvoiceDue > 0).Select(x => x.Id).ToArray();
                var allocations = await db.Set<Allocation>().AsNoTracking()
                    .Where(x => positiveIds.Contains(x.ObligationId)).ToArrayAsync(ct);
                var journals = await db.Set<Journal>().AsNoTracking()
                    .Where(x => x.PostedAt != null && x.ObligationId != Guid.Empty).ToArrayAsync(ct);
                var posted = journals.Select(x => x.ObligationId).ToHashSet();
                var invoicePence = obligations.Where(x => x.InvoiceDue > 0 && posted.Contains(x.Id))
                    .Aggregate(0L, (sum, x) => checked(sum + FinanceLedgerMath.Pence(x.InvoiceDue)));
                var appliedPence = allocations.Aggregate(0L, (sum, x) => checked(sum +
                    (x.ReversalOfId is null ? 1 : -1) * FinanceLedgerMath.Pence(x.Amount)));
                var outstanding = Math.Max(0, checked(invoicePence - appliedPence));
                var currentJournal = journals.Single(x => x.ObligationId == credit.Id);
                var postedAt = journals.ToDictionary(x => x.ObligationId, x => x.PostedAt!.Value);
                var earlierCredits = obligations.Where(x => x.InvoiceDue < 0 && x.Id != credit.Id && posted.Contains(x.Id))
                    .Where(x => postedAt[x.Id] < currentJournal.PostedAt ||
                        postedAt[x.Id] == currentJournal.PostedAt && x.Id.CompareTo(credit.Id) < 0)
                    .Aggregate(0L, (sum, x) => checked(sum - FinanceLedgerMath.Pence(x.InvoiceDue)));
                var collected = allocations.Where(x => x.ReversalOfId is null).Aggregate(0L,
                    (sum, x) => checked(sum + FinanceLedgerMath.Pence(x.Amount))) -
                    allocations.Where(x => x.ReversalOfId is not null).Aggregate(0L,
                    (sum, x) => checked(sum + FinanceLedgerMath.Pence(x.Amount)));
                var prior = await db.Set<RefundRequest>().AsNoTracking()
                    .Where(x => x.CreditObligationId == credit.Id && x.State != "rejected")
                    .Select(x => x.Amount).ToArrayAsync(ct);
                var reserved = prior.Aggregate(0L, (sum, value) => checked(sum + FinanceLedgerMath.Pence(value)));
                long entitlement;
                try { entitlement = FinanceRefundMath.Entitlement(-FinanceLedgerMath.Pence(credit.InvoiceDue),
                    outstanding, earlierCredits, collected, reserved); }
                catch (ArgumentOutOfRangeException) { throw new QuoteOperationException(409, "refund-entitlement-invalid"); }
                if (amount > entitlement) throw new QuoteOperationException(409, "refund-not-collected");
                var rule = await db.Set<RefundApprovalRule>().AsNoTracking()
                    .OrderByDescending(x => x.Version).FirstAsync(ct);
                var now = time.GetUtcNow();
                var request = new RefundRequest { AgencyId = credit.AgencyId, PolicyId = credit.PolicyId,
                    CreditObligationId = credit.Id, DebtorKind = credit.DebtorKind, DebtorId = debtorId,
                    Amount = amount / 100m, RuleId = rule.Id, RequestedBy = actor.UserId,
                    RequestedAt = now, Reason = reason.Trim(), CreatedAt = now, CreatedBy = actor.UserId,
                    UpdatedAt = now };
                db.Add(request); await db.SaveChangesAsync(ct);
                foreach (var (allocation, _, pence) in sourceRows)
                    db.Add(new RefundCashReservation { RefundRequestId = request.Id,
                        AllocationId = allocation.Id, Amount = pence / 100m,
                        CreatedAt = now, CreatedBy = actor.UserId });
                await db.SaveChangesAsync(ct);
                request.State = "pending"; request.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                return Outcome(request.Id, new { id = request.Id, state = request.State });
            }, token, IsolationLevel.Serializable);
    }

    public Task<CommandOutcome> DecideAsync(ActorContext actor, Guid requestId, byte[] expectedVersion,
        string kind, string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        if (requestId == Guid.Empty || expectedVersion is not { Length: 8 } ||
            kind is not ("approve" or "reject") || !Reason(reason))
            throw new QuoteOperationException(400, "refund-decision-invalid");
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
                $"/api/v1/finance/refunds/{requestId:N}/decisions", key, correlationId),
            new { requestId, expectedVersion = Convert.ToBase64String(expectedVersion), kind, reason },
            "finance.refund.decided",
            async (db, ct) =>
            {
                var request = await db.Set<RefundRequest>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId, ct)
                    ?? throw new QuoteOperationException(404, "refund-not-found");
                await Authorize(db, actor, request.AgencyId, null, ct, "finance-refund-approve", true);
                await ApprovalAuthority(db, request.Amount, ct);
                if (request.RequestedBy == actor.UserId) throw new QuoteOperationException(403, "self-approval");
            },
            async (db, ct) =>
            {
                var request = await db.Set<RefundRequest>()
                    .FromSqlInterpolated($"SELECT * FROM RefundRequest WITH(UPDLOCK,HOLDLOCK) WHERE Id={requestId}")
                    .SingleAsync(ct);
                if (!request.RowVersion.SequenceEqual(expectedVersion))
                    throw new QuoteOperationException(412, "refund-version-stale");
                if (request.State != "pending") throw new QuoteOperationException(409, "refund-decision-closed");
                if (request.RequestedBy == actor.UserId) throw new QuoteOperationException(403, "self-approval");
                if (await db.Set<RefundDecision>().AnyAsync(x => x.RefundRequestId == request.Id && x.ActorId == actor.UserId, ct))
                    throw new QuoteOperationException(409, "refund-already-decided");
                var limit = await ApprovalAuthority(db, request.Amount, ct);
                var now = time.GetUtcNow();
                var decision = new RefundDecision { RefundRequestId = request.Id, Kind = kind,
                    ActorId = actor.UserId, AuthorityLimitSnapshot = limit,
                    RuleId = request.RuleId, Reason = reason.Trim(), DecidedAt = now,
                    CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(decision); await db.SaveChangesAsync(ct);
                var rule = await db.Set<RefundApprovalRule>().AsNoTracking().SingleAsync(x => x.Id == request.RuleId, ct);
                var count = await db.Set<RefundDecision>().CountAsync(x => x.RefundRequestId == requestId && x.Kind == "approve", ct);
                if (kind == "reject") request.State = "rejected";
                else if (count >= FinanceRefundMath.RequiredApprovals(FinanceLedgerMath.Pence(request.Amount),
                    FinanceLedgerMath.Pence(rule.SecondApprovalThreshold))) request.State = "approved";
                request.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                return Outcome(decision.Id, new { id = decision.Id, refundId = request.Id, state = request.State });
            }, token, IsolationLevel.Serializable);
    }

    public async Task<RefundView> DetailAsync(ActorContext actor, Guid requestId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var request = await db.Set<RefundRequest>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId, token)
            ?? throw new QuoteOperationException(404, "refund-not-found");
        await Authorize(db, actor, request.AgencyId, null, token, "finance-read");
        var rule = await db.Set<RefundApprovalRule>().AsNoTracking().SingleAsync(x => x.Id == request.RuleId, token);
        var reservations = await (from reservation in db.Set<RefundCashReservation>()
            join allocation in db.Set<Allocation>() on reservation.AllocationId equals allocation.Id
            where reservation.RefundRequestId == request.Id
            orderby reservation.Id
            select new RefundSourceView(allocation.Id, allocation.ReceiptId,
                FinanceLedgerMath.Money(reservation.Amount))).ToArrayAsync(token);
        var decisions = await db.Set<RefundDecision>().AsNoTracking().Where(x => x.RefundRequestId == request.Id)
            .OrderBy(x => x.DecidedAt).ThenBy(x => x.Id)
            .Select(x => new RefundDecisionView(x.Id, x.Kind, x.ActorId, x.Reason, x.DecidedAt)).ToArrayAsync(token);
        await tx.CommitAsync(token);
        return new RefundView(request.Id, request.AgencyId, request.PolicyId, request.CreditObligationId,
            request.DebtorKind, request.DebtorId, FinanceLedgerMath.Money(request.Amount), request.Currency,
            request.State, rule.Id, rule.Version, FinanceLedgerMath.Money(rule.SecondApprovalThreshold),
            FinanceRefundMath.RequiredApprovals(FinanceLedgerMath.Pence(request.Amount),
                FinanceLedgerMath.Pence(rule.SecondApprovalThreshold)), request.RequestedBy,
            request.RequestedAt, request.Reason, reservations, decisions,
            '"' + Convert.ToBase64String(request.RowVersion) + '"');
    }

    private static async Task Authorize(BackOfficeDbContext db, ActorContext actor, Guid agencyId,
        Guid? relationshipId, CancellationToken ct, string capability, bool write = false)
    {
        if (!actor.HasCapability(capability)) throw new QuoteOperationException(403, "refund-scope-denied");
        await FinanceLedgerService.Authorize(db, actor, agencyId, relationshipId, ct, write);
    }
    private static async Task<decimal> ApprovalAuthority(BackOfficeDbContext db, decimal amount, CancellationToken ct)
    {
        var authority = await db.Set<RefundRoleAuthority>()
            .FromSqlRaw("SELECT * FROM RefundRoleAuthority WITH(UPDLOCK,HOLDLOCK) WHERE RoleCode='finance'")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (authority is null || !authority.Active || authority.Limit < amount)
            throw new QuoteOperationException(403, "approval-limit");
        return authority.Limit;
    }
    private static Guid DebtorId(IssueFinancialObligation row)
        => row.DebtorKind == "agency" ? row.DebtorAgencyId ?? Guid.Empty :
            row.DebtorKind == "relationship" ? row.DebtorRelationshipId ?? Guid.Empty : Guid.Empty;
    private static long Amount(string value)
    {
        try { return FinanceReceiptMath.PositivePence(value); }
        catch (Exception error) when (error is ArgumentOutOfRangeException or OverflowException)
        { throw new QuoteOperationException(400, "refund-amount-invalid"); }
    }
    private static bool Reason(string? reason) => reason is { Length: >= 10 and <= 1000 } && !string.IsNullOrWhiteSpace(reason);
    private static CommandOutcome Outcome(Guid id, object value)
        => new(id, 201, JsonSerializer.Serialize(value, Json));
}
