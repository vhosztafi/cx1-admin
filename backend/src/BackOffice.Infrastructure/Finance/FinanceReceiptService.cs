using System.Data;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Finance;

public sealed record ReceiptAllocationInput(Guid InvoiceId, string Amount);
public sealed record ReceiptAllocationView(Guid Id, Guid InvoiceId, string Amount, Guid? ReversalOfId,
    string Reason, DateTimeOffset AppliedAt, Guid AccountingPeriodId, DateOnly PostingDate);
public sealed record ReceiptView(Guid Id, Guid AgencyId, string OriginKind, Guid OriginId, string BankReference,
    string Amount, string Residual, string Currency, DateOnly ReceivedOn, Guid AccountingPeriodId,
    DateOnly PostingDate, DateTimeOffset PostedAt, Guid AssignmentId, string PayerKind, Guid? PayerId,
    int AssignmentOrdinal, IReadOnlyList<ReceiptAllocationView> Allocations);
public sealed record ReceiptListItem(Guid Id, string Amount, string Residual, string Currency, DateOnly ReceivedOn,
    string BankReference, string PayerKind, Guid? PayerId, Guid AssignmentId);
public sealed record ReceiptPage(int Page, int PageSize, int Total, IReadOnlyList<ReceiptListItem> Items);

public sealed class FinanceReceiptService(IDbContextFactory<BackOfficeDbContext> factory,
    SqlCommandBoundary commands, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<CommandOutcome> RecordAsync(ActorContext actor, Guid agencyId, string amount, string currency,
        DateOnly receivedOn, string bankReference, string originKind, Guid originId,
        string payerKind, Guid? payerId, string key, Guid correlationId, CancellationToken token = default)
    {
        var pence = Amount(amount);
        if (agencyId == Guid.Empty || originId == Guid.Empty || receivedOn == default ||
            currency != "GBP" || originKind is not ("manual" or "bank-import") ||
            bankReference is null || bankReference.Trim().Length is < 1 or > 200 ||
            !PayerShape(payerKind, payerId))
            throw new QuoteOperationException(400, "receipt-input-invalid");
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
            $"/api/v1/finance/agencies/{agencyId:N}/receipts", key, correlationId),
            new { agencyId, amount, currency, receivedOn, bankReference, originKind, originId, payerKind, payerId },
            "finance.receipt.recorded",
            async (db, ct) => await Authorize(db, actor, agencyId, payerKind == "relationship" ? payerId : null, ct, true),
            async (db, ct) =>
            {
                if (originKind == "bank-import")
                {
                    var line = await db.Set<BankLine>().FromSqlInterpolated($"SELECT * FROM BankLine WITH(UPDLOCK,HOLDLOCK) WHERE Id={originId}")
                        .AsNoTracking().SingleOrDefaultAsync(ct);
                    if (line is null || line.AgencyId != agencyId || line.SignedAmount != FinanceReceiptMath.Money(pence) ||
                        line.Currency != currency || line.ValueDate != receivedOn || line.Reference != bankReference.Trim())
                        throw new QuoteOperationException(409, "receipt-bank-origin-invalid");
                }
                if (await db.Set<Receipt>().AnyAsync(x => x.OriginKind == originKind && x.OriginId == originId, ct))
                    throw new QuoteOperationException(409, "receipt-origin-conflict");
                await ValidatePayer(db, agencyId, payerKind, payerId, ct);
                var now = time.GetUtcNow();
                var period = await AccountingPeriods.HoldAsync(db, now, ct);
                var receipt = new Receipt { AgencyId = agencyId, OriginKind = originKind, OriginId = originId,
                    BankReference = bankReference.Trim(), Amount = FinanceReceiptMath.Money(pence), Currency = "GBP",
                    ReceivedOn = receivedOn, AccountingPeriodId = period.PeriodId, PostingDate = period.PostingDate,
                    PostedAt = now, CreatedAt = now, CreatedBy = actor.UserId };
                var assignment = new ReceiptPayerAssignment { ReceiptId = receipt.Id, Ordinal = 1,
                    PayerKind = payerKind, PayerAgencyId = payerKind == "agency" ? payerId : null,
                    PayerRelationshipId = payerKind == "relationship" ? payerId : null,
                    Reason = "Receipt payer recorded", AssignedAt = now, OperationId = originId,
                    CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(receipt); db.Add(assignment);
                await db.SaveChangesAsync(ct);
                // The receipt insert trigger writes its one balanced suspense
                // posting in this transaction; direct SQL inserts get the same guard.
                return new CommandOutcome(receipt.Id, 201,
                    JsonSerializer.Serialize(new { id = receipt.Id, assignmentId = assignment.Id }, Json),
                    false, Etag(assignment.Id));
            }, token, IsolationLevel.Serializable);
    }

    public Task<CommandOutcome> AssignAsync(ActorContext actor, Guid receiptId, Guid expectedAssignmentId,
        string payerKind, Guid? payerId, string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        if (receiptId == Guid.Empty || expectedAssignmentId == Guid.Empty || !PayerShape(payerKind, payerId) || !Reason(reason))
            throw new QuoteOperationException(400, "payer-assignment-invalid");
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
            $"/api/v1/finance/receipts/{receiptId:N}/payer", key, correlationId),
            new { receiptId, expectedAssignmentId, payerKind, payerId, reason }, "finance.receipt.payer-assigned",
            async (db, ct) =>
            {
                var agencyId = await ReceiptAgency(db, receiptId, ct);
                await Authorize(db, actor, agencyId, payerKind == "relationship" ? payerId : null, ct, true);
            },
            async (db, ct) =>
            {
                var receipt = await HoldReceipt(db, receiptId, ct);
                await ValidatePayer(db, receipt.AgencyId, payerKind, payerId, ct);
                var current = await Assignment(db, receiptId, ct);
                if (current.Id != expectedAssignmentId) throw new QuoteOperationException(412, "payer-assignment-stale");
                var rows = await db.Set<Allocation>().AsNoTracking().Where(x => x.ReceiptId == receiptId).ToArrayAsync(ct);
                if (Net(rows) != 0) throw new QuoteOperationException(409, "payer-has-active-allocations");
                var now = time.GetUtcNow();
                var assignment = new ReceiptPayerAssignment { ReceiptId = receiptId,
                    Ordinal = current.Ordinal + 1, PayerKind = payerKind,
                    PayerAgencyId = payerKind == "agency" ? payerId : null,
                    PayerRelationshipId = payerKind == "relationship" ? payerId : null,
                    Reason = reason.Trim(), AssignedAt = now, OperationId = Guid.NewGuid(),
                    CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(assignment); await db.SaveChangesAsync(ct);
                return new CommandOutcome(receiptId, 201,
                    JsonSerializer.Serialize(new { id = receiptId, assignmentId = assignment.Id }, Json),
                    false, Etag(assignment.Id));
            }, token, IsolationLevel.Serializable);
    }

    public Task<CommandOutcome> AllocateAsync(ActorContext actor, Guid receiptId, Guid expectedAssignmentId,
        IReadOnlyList<ReceiptAllocationInput> items, string key, Guid correlationId, CancellationToken token = default)
    {
        if (receiptId == Guid.Empty || expectedAssignmentId == Guid.Empty || items is null || items.Count is < 1 or > 100 ||
            items.Any(x => x.InvoiceId == Guid.Empty) || items.Select(x => x.InvoiceId).Distinct().Count() != items.Count)
            throw new QuoteOperationException(400, "allocation-input-invalid");
        var amounts = items.Select(x => Amount(x.Amount)).ToArray();
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
            $"/api/v1/finance/receipts/{receiptId:N}/allocations", key, correlationId),
            new { receiptId, expectedAssignmentId, items }, "finance.receipt.allocated",
            async (db, ct) =>
            {
                var agencyId = await ReceiptAgency(db, receiptId, ct);
                var assignment = await db.Set<ReceiptPayerAssignment>().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == expectedAssignmentId && x.ReceiptId == receiptId, ct)
                    ?? throw new QuoteOperationException(412, "payer-assignment-stale");
                await Authorize(db, actor, agencyId, assignment.PayerRelationshipId, ct, true);
            },
            async (db, ct) =>
            {
                var receipt = await HoldReceipt(db, receiptId, ct);
                var payer = await Assignment(db, receiptId, ct);
                if (payer.Id != expectedAssignmentId) throw new QuoteOperationException(412, "payer-assignment-stale");
                if (payer.PayerKind == "unidentified") throw new QuoteOperationException(409, "payer-unidentified");
                var all = await db.Set<Allocation>().AsNoTracking().Where(x => x.ReceiptId == receiptId).ToArrayAsync(ct);
                var receiptRemaining = FinanceReceiptMath.Residual(FinanceLedgerMath.Pence(receipt.Amount),
                    all.Where(x => x.ReversalOfId is null).Select(x => FinanceLedgerMath.Pence(x.Amount)),
                    all.Where(x => x.ReversalOfId is not null).Select(x => FinanceLedgerMath.Pence(x.Amount)));
                if (amounts.Aggregate(0L, checked((sum, value) => sum + value)) > receiptRemaining)
                    throw new QuoteOperationException(409, "allocation-exceeds-residual");
                var invoices = new List<IssueFinancialObligation>(items.Count);
                foreach (var id in items.Select(x => x.InvoiceId).OrderBy(x => x))
                {
                    var invoice = await db.Set<IssueFinancialObligation>()
                        .FromSqlInterpolated($"SELECT * FROM IssueFinancialObligation WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}")
                        .AsNoTracking().SingleOrDefaultAsync(ct)
                        ?? throw new QuoteOperationException(404, "allocation-invoice-not-found");
                    if (invoice.AgencyId != receipt.AgencyId || invoice.Currency != receipt.Currency ||
                        invoice.DebtorKind != payer.PayerKind ||
                        (payer.PayerKind == "agency" && invoice.DebtorAgencyId != payer.PayerAgencyId) ||
                        (payer.PayerKind == "relationship" && invoice.DebtorRelationshipId != payer.PayerRelationshipId))
                        throw new QuoteOperationException(409, "debtor-mismatch");
                    if (invoice.InvoiceDue <= 0) throw new QuoteOperationException(409, "invoice-not-eligible");
                    invoices.Add(invoice);
                }
                var invoiceById = invoices.ToDictionary(x => x.Id);
                foreach (var (input, index) in items.Select((value, index) => (value, index)))
                {
                    var invoice = invoiceById[input.InvoiceId];
                    var applied = await db.Set<Allocation>().AsNoTracking().Where(x => x.ObligationId == invoice.Id).ToArrayAsync(ct);
                    var remaining = FinanceReceiptMath.Residual(FinanceLedgerMath.Pence(invoice.InvoiceDue),
                        applied.Where(x => x.ReversalOfId is null).Select(x => FinanceLedgerMath.Pence(x.Amount)),
                        applied.Where(x => x.ReversalOfId is not null).Select(x => FinanceLedgerMath.Pence(x.Amount)));
                    if (amounts[index] > remaining) throw new QuoteOperationException(409, "allocation-exceeds-residual");
                }
                var now = time.GetUtcNow();
                var period = await AccountingPeriods.HoldAsync(db, now, ct);
                var operationId = Guid.NewGuid();
                var allocations = items.Select((input, index) => new Allocation
                {
                    ReceiptId = receiptId, ObligationId = input.InvoiceId, Amount = FinanceReceiptMath.Money(amounts[index]),
                    Reason = "Receipt applied to invoice", AppliedAt = now, OperationId = operationId,
                    Ordinal = index + 1, AccountingPeriodId = period.PeriodId, PostingDate = period.PostingDate,
                    CreatedAt = now, CreatedBy = actor.UserId
                }).ToArray();
                db.AddRange(allocations); await db.SaveChangesAsync(ct);
                // SQL writes one noncash application posting per saved row.
                return new CommandOutcome(allocations[0].Id, 201,
                    JsonSerializer.Serialize(new { receiptId, allocationIds = allocations.Select(x => x.Id).ToArray() }, Json));
            }, token, IsolationLevel.Serializable);
    }

    public Task<CommandOutcome> ReverseAsync(ActorContext actor, Guid allocationId, string reason,
        string key, Guid correlationId, CancellationToken token = default)
    {
        if (allocationId == Guid.Empty || !Reason(reason)) throw new QuoteOperationException(400, "allocation-reversal-invalid");
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
            $"/api/v1/finance/allocations/{allocationId:N}/reversals", key, correlationId),
            new { allocationId, reason }, "finance.receipt.allocation-reversed",
            async (db, ct) =>
            {
                var source = await (from a in db.Set<Allocation>().AsNoTracking()
                    join r in db.Set<Receipt>().AsNoTracking() on a.ReceiptId equals r.Id
                    join o in db.Set<IssueFinancialObligation>().AsNoTracking() on a.ObligationId equals o.Id
                    where a.Id == allocationId && a.ReversalOfId == null
                    select new { r.AgencyId, o.RelationshipId }).SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "allocation-not-found");
                await Authorize(db, actor, source.AgencyId, source.RelationshipId, ct, true);
            },
            async (db, ct) =>
            {
                var original = await db.Set<Allocation>().AsNoTracking().SingleAsync(x => x.Id == allocationId, ct);
                var receipt = await HoldReceipt(db, original.ReceiptId, ct);
                var invoice = await db.Set<IssueFinancialObligation>()
                    .FromSqlInterpolated($"SELECT * FROM IssueFinancialObligation WITH(UPDLOCK,HOLDLOCK) WHERE Id={original.ObligationId}")
                    .AsNoTracking().SingleAsync(ct);
                if (await db.Set<Allocation>().AnyAsync(x => x.ReversalOfId == allocationId, ct))
                    throw new QuoteOperationException(409, "allocation-already-reversed");
                var now = time.GetUtcNow();
                var period = await AccountingPeriods.HoldAsync(db, now, ct);
                var reversal = new Allocation { ReceiptId = original.ReceiptId, ObligationId = original.ObligationId,
                    Amount = original.Amount, ReversalOfId = original.Id, Reason = reason.Trim(), AppliedAt = now,
                    OperationId = Guid.NewGuid(), Ordinal = 1, AccountingPeriodId = period.PeriodId,
                    PostingDate = period.PostingDate, CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(reversal); await db.SaveChangesAsync(ct);
                // The reversal insert trigger writes its noncash counter-posting.
                return new CommandOutcome(reversal.Id, 201,
                    JsonSerializer.Serialize(new { receiptId = receipt.Id, allocationId, reversalId = reversal.Id }, Json));
            }, token, IsolationLevel.Serializable);
    }

    public async Task<ReceiptView> DetailAsync(ActorContext actor, Guid receiptId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var agencyId = await ReceiptAgency(db, receiptId, token);
        await Authorize(db, actor, agencyId, null, token);
        var receipt = await db.Set<Receipt>().AsNoTracking().SingleAsync(x => x.Id == receiptId, token);
        var payer = await Assignment(db, receiptId, token);
        var rows = await db.Set<Allocation>().AsNoTracking().Where(x => x.ReceiptId == receiptId)
            .OrderBy(x => x.AppliedAt).ThenBy(x => x.Id).ToArrayAsync(token);
        var residual = FinanceReceiptMath.Residual(FinanceLedgerMath.Pence(receipt.Amount),
            rows.Where(x => x.ReversalOfId is null).Select(x => FinanceLedgerMath.Pence(x.Amount)),
            rows.Where(x => x.ReversalOfId is not null).Select(x => FinanceLedgerMath.Pence(x.Amount)));
        var view = new ReceiptView(receipt.Id, receipt.AgencyId, receipt.OriginKind, receipt.OriginId,
            receipt.BankReference, FinanceLedgerMath.Money(receipt.Amount), FinanceLedgerMath.Money(FinanceReceiptMath.Money(residual)),
            receipt.Currency, receipt.ReceivedOn, receipt.AccountingPeriodId, receipt.PostingDate, receipt.PostedAt,
            payer.Id, payer.PayerKind, payer.PayerAgencyId ?? payer.PayerRelationshipId, payer.Ordinal,
            rows.Select(x => new ReceiptAllocationView(x.Id, x.ObligationId, FinanceLedgerMath.Money(x.Amount),
                x.ReversalOfId, x.Reason, x.AppliedAt, x.AccountingPeriodId, x.PostingDate)).ToArray());
        await tx.CommitAsync(token);
        return view;
    }

    public async Task<ReceiptPage> ListAsync(ActorContext actor, Guid agencyId, int page = 1, int pageSize = 50,
        CancellationToken token = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new QuoteOperationException(400, "receipt-query-invalid");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await Authorize(db, actor, agencyId, null, token);
        var query = db.Set<Receipt>().AsNoTracking().Where(x => x.AgencyId == agencyId);
        var total = await query.CountAsync(token);
        var receipts = await query.OrderByDescending(x => x.PostedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        var items = new List<ReceiptListItem>(receipts.Length);
        foreach (var receipt in receipts)
        {
            var assignment = await Assignment(db, receipt.Id, token);
            var allocations = await db.Set<Allocation>().AsNoTracking().Where(x => x.ReceiptId == receipt.Id).ToArrayAsync(token);
            var residual = FinanceReceiptMath.Residual(FinanceLedgerMath.Pence(receipt.Amount),
                allocations.Where(x => x.ReversalOfId is null).Select(x => FinanceLedgerMath.Pence(x.Amount)),
                allocations.Where(x => x.ReversalOfId is not null).Select(x => FinanceLedgerMath.Pence(x.Amount)));
            items.Add(new(receipt.Id, FinanceLedgerMath.Money(receipt.Amount),
                FinanceLedgerMath.Money(FinanceReceiptMath.Money(residual)), receipt.Currency,
                receipt.ReceivedOn, receipt.BankReference, assignment.PayerKind,
                assignment.PayerAgencyId ?? assignment.PayerRelationshipId, assignment.Id));
        }
        await tx.CommitAsync(token);
        return new ReceiptPage(page, pageSize, total, items);
    }

    private static async Task Authorize(BackOfficeDbContext db, ActorContext actor, Guid agencyId,
        Guid? relationshipId, CancellationToken token, bool write = false)
    {
        if (!actor.HasCapability(write ? "finance-cash-write" : "finance-read"))
            throw new QuoteOperationException(403, "receipt-scope-denied");
        await FinanceLedgerService.Authorize(db, actor, agencyId, relationshipId, token, write);
    }

    private static async Task<Guid> ReceiptAgency(BackOfficeDbContext db, Guid receiptId, CancellationToken token)
        => await db.Set<Receipt>().AsNoTracking().Where(x => x.Id == receiptId).Select(x => (Guid?)x.AgencyId)
            .SingleOrDefaultAsync(token) ?? throw new QuoteOperationException(404, "receipt-not-found");
    private static async Task<Receipt> HoldReceipt(BackOfficeDbContext db, Guid id, CancellationToken token)
        => await db.Set<Receipt>().FromSqlInterpolated($"SELECT * FROM Receipt WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}")
            .AsNoTracking().SingleOrDefaultAsync(token) ?? throw new QuoteOperationException(404, "receipt-not-found");
    private static async Task<ReceiptPayerAssignment> Assignment(BackOfficeDbContext db, Guid receiptId, CancellationToken token)
        => await db.Set<ReceiptPayerAssignment>().AsNoTracking().Where(x => x.ReceiptId == receiptId)
            .OrderByDescending(x => x.Ordinal).FirstOrDefaultAsync(token)
            ?? throw new InvalidOperationException("Receipt has no payer assignment history.");
    private static async Task ValidatePayer(BackOfficeDbContext db, Guid agencyId, string kind, Guid? payerId, CancellationToken token)
    {
        if (kind == "agency" && payerId != agencyId) throw new QuoteOperationException(409, "debtor-mismatch");
        if (kind == "relationship" && !await db.Set<ClientAgencyRelationship>()
            .FromSqlInterpolated($"SELECT * FROM ClientAgencyRelationship WITH(HOLDLOCK) WHERE Id={payerId} AND AgencyId={agencyId}")
            .AnyAsync(token)) throw new QuoteOperationException(409, "debtor-mismatch");
    }
    private static long Net(IEnumerable<Allocation> rows) => rows.Aggregate(0L, (sum, x) =>
        checked(sum + (x.ReversalOfId is null ? FinanceLedgerMath.Pence(x.Amount) : -FinanceLedgerMath.Pence(x.Amount))));
    private static long Amount(string text)
    {
        try { return FinanceReceiptMath.PositivePence(text); }
        catch (Exception error) when (error is ArgumentOutOfRangeException or OverflowException)
        { throw new QuoteOperationException(400, "finance-amount-invalid"); }
    }
    private static bool Reason(string reason) => reason is { Length: >= 10 and <= 1000 } && !string.IsNullOrWhiteSpace(reason);
    private static bool PayerShape(string kind, Guid? id) => kind switch
    {
        "unidentified" => id is null,
        "agency" or "relationship" => id is Guid value && value != Guid.Empty,
        _ => false
    };
    private static string Etag(Guid id) => '"' + id.ToString("N") + '"';
}
