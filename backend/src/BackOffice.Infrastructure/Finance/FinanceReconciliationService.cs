using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Finance;

public sealed record BankLineView(Guid Id, Guid AgencyId, string ImportKey, DateOnly ValueDate,
    string Reference, string SignedAmount, string Currency, string RawJson, DateTimeOffset ImportedAt,
    IReadOnlyList<Guid> DuplicateCandidateIds);
public sealed record BankLinePage(int Page, int PageSize, int Total, IReadOnlyList<BankLineView> Items);
public sealed record ReconciliationMatchView(Guid Id, Guid BankLineId, Guid FinancePostingId,
    string SignedAmount, Guid? ReversalOfId, string Reason, DateTimeOffset MatchedAt);
public sealed record ReconciliationLineView(Guid BankLineId, string ImportKey, DateOnly ValueDate,
    string SignedAmount, string Residual, bool Excluded, Guid? DuplicateOfBankLineId,
    string? Explanation, bool Addressed);
public sealed record ReconciliationTargetView(Guid FinancePostingId, DateOnly PostingDate,
    string SourceKind, Guid SourceId, string SignedAmount, string Residual,
    string? Explanation, bool Addressed);
public sealed record ReconciliationView(Guid Id, Guid AgencyId, DateOnly From, DateOnly To,
    DateTimeOffset? CompletedAt, string NetVariance, string AbsoluteVariance, int UnaddressedCount,
    IReadOnlyList<ReconciliationLineView> Lines, IReadOnlyList<ReconciliationTargetView> Targets,
    IReadOnlyList<ReconciliationMatchView> Matches);

public sealed class FinanceReconciliationService(IDbContextFactory<BackOfficeDbContext> factory,
    SqlCommandBoundary commands, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    // Paid refund joins receipt after its own durable outcome and SQL source guard.
    private static readonly string[] CashSources = ["receipt", "refund", "correction"];

    public Task<CommandOutcome> ImportAsync(ActorContext actor, Guid agencyId, string importKey,
        DateOnly valueDate, string reference, string signedAmount, string currency, string rawJson,
        string key, Guid correlationId, CancellationToken token = default)
    {
        var amount = Signed(signedAmount);
        if (agencyId == Guid.Empty || valueDate == default || currency != "GBP" || !Bounded(importKey, 200) ||
            !Bounded(reference, 200) || rawJson is null || rawJson.Length is < 2 or > 5000)
            throw new QuoteOperationException(400, "bank-import-invalid");
        reference = reference.Trim();
        string normalized;
        try { using var document = JsonDocument.Parse(rawJson); normalized = JsonSerializer.Serialize(document.RootElement); }
        catch (JsonException) { throw new QuoteOperationException(400, "bank-import-invalid"); }
        if (normalized.Length > 5000) throw new QuoteOperationException(400, "bank-import-invalid");
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(normalized));
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
                $"/api/v1/finance/agencies/{agencyId:N}/bank-lines", key, correlationId),
            new { agencyId, importKey, valueDate, reference, signedAmount, currency, normalized },
            "finance.bank-line.imported",
            (db, ct) => Authorize(db, actor, agencyId, ct, true),
            async (db, ct) =>
            {
                var existing = await db.Set<BankLine>().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.AgencyId == agencyId && x.ImportKey == importKey, ct);
                if (existing is not null)
                {
                    if (existing.ValueDate != valueDate || existing.Reference != reference ||
                        FinanceLedgerMath.Pence(existing.SignedAmount) != amount || existing.Currency != currency ||
                        !CryptographicOperations.FixedTimeEquals(existing.RawHash, hash))
                        throw new QuoteOperationException(409, "bank-import-conflict");
                    return Outcome(existing.Id, new { id = existing.Id, reimported = true });
                }
                if (await db.Set<Reconciliation>().AnyAsync(x => x.AgencyId == agencyId && x.CompletedAt != null &&
                    x.From <= valueDate && valueDate < x.To, ct))
                    throw new QuoteOperationException(409, "reconciliation-period-completed");
                var now = time.GetUtcNow();
                var line = new BankLine { AgencyId = agencyId, ImportKey = importKey,
                    ValueDate = valueDate, Reference = reference.Trim(), SignedAmount = amount / 100m,
                    Currency = currency, RawJson = normalized, RawHash = hash,
                    ImportedAt = now, CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(line); await db.SaveChangesAsync(ct);
                return Outcome(line.Id, new { id = line.Id, reimported = false });
            }, token, IsolationLevel.Serializable);
    }

    public Task<CommandOutcome> CreateAsync(ActorContext actor, Guid agencyId, DateOnly from, DateOnly to,
        string key, Guid correlationId, CancellationToken token = default)
    {
        if (agencyId == Guid.Empty || from == default || to <= from)
            throw new QuoteOperationException(400, "reconciliation-window-invalid");
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
                $"/api/v1/finance/agencies/{agencyId:N}/reconciliations", key, correlationId),
            new { agencyId, from, to }, "finance.reconciliation.created",
            (db, ct) => Authorize(db, actor, agencyId, ct, true),
            async (db, ct) =>
            {
                var existing = await db.Set<Reconciliation>().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.AgencyId == agencyId && x.From == from && x.To == to, ct);
                if (existing is not null) return Outcome(existing.Id, new { id = existing.Id, existing = true });
                if (await db.Set<Reconciliation>().AnyAsync(x => x.AgencyId == agencyId && x.From < to && from < x.To, ct))
                    throw new QuoteOperationException(409, "reconciliation-window-overlap");
                var now = time.GetUtcNow();
                var row = new Reconciliation { AgencyId = agencyId, From = from, To = to,
                    CreatedAt = now, UpdatedAt = now, CreatedBy = actor.UserId };
                db.Add(row); await db.SaveChangesAsync(ct);
                return Outcome(row.Id, new { id = row.Id, existing = false });
            }, token, IsolationLevel.Serializable);
    }

    public Task<CommandOutcome> MatchAsync(ActorContext actor, Guid reconciliationId, Guid bankLineId,
        Guid postingId, string signedAmount, string reason, string key, Guid correlationId,
        CancellationToken token = default)
    {
        var amount = Signed(signedAmount);
        if (reconciliationId == Guid.Empty || bankLineId == Guid.Empty || postingId == Guid.Empty || !Bounded(reason, 1000, 10))
            throw new QuoteOperationException(400, "bank-match-invalid");
        return Within(actor, reconciliationId, $"matches", key, correlationId,
            new { reconciliationId, bankLineId, postingId, signedAmount, reason }, "finance.reconciliation.matched",
            async (db, reconciliation, ct) =>
            {
                Open(reconciliation);
                var line = await HoldLine(db, bankLineId, ct);
                if (line.AgencyId != reconciliation.AgencyId || line.ValueDate < reconciliation.From || line.ValueDate >= reconciliation.To)
                    throw new QuoteOperationException(404, "bank-line-not-in-reconciliation");
                if (await db.Set<BankLineExclusion>().AnyAsync(x => x.BankLineId == bankLineId, ct))
                    throw new QuoteOperationException(409, "excluded-line-cannot-match");
                var posting = await HoldPosting(db, postingId, ct);
                if (posting.AgencyId != reconciliation.AgencyId || posting.Currency != line.Currency ||
                    !CashSources.Contains(posting.SourceKind, StringComparer.Ordinal) || posting.CashDelta == 0 ||
                    Math.Sign(posting.CashDelta) != Math.Sign(line.SignedAmount) || Math.Sign(amount) != Math.Sign(line.SignedAmount))
                    throw new QuoteOperationException(409, "bank-match-source-mismatch");
                var lineResidual = await Residual(db, bankLineId, FinanceLedgerMath.Pence(line.SignedAmount), ct);
                var postingResidual = await TargetResidual(db, postingId, FinanceLedgerMath.Pence(posting.CashDelta), ct);
                if (Math.Abs((decimal)amount) > Math.Abs((decimal)lineResidual) ||
                    Math.Abs((decimal)amount) > Math.Abs((decimal)postingResidual))
                    throw new QuoteOperationException(409, "match-exceeds-residual");
                var now = await MatchEventTime(db, posting, time.GetUtcNow(), ct);
                var match = new ReconciliationMatch { ReconciliationId = reconciliationId,
                    BankLineId = bankLineId, FinancePostingId = postingId, SignedAmount = amount / 100m,
                    Reason = reason.Trim(), MatchedAt = now, CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(match); await db.SaveChangesAsync(ct);
                return Outcome(match.Id, new { id = match.Id, bankLineId, postingId });
            }, token);
    }

    public Task<CommandOutcome> ReverseAsync(ActorContext actor, Guid matchId, string reason,
        string key, Guid correlationId, CancellationToken token = default)
    {
        if (matchId == Guid.Empty || !Bounded(reason, 1000, 10))
            throw new QuoteOperationException(400, "bank-match-reversal-invalid");
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
                $"/api/v1/finance/reconciliation-matches/{matchId:N}/reversals", key, correlationId),
            new { matchId, reason }, "finance.reconciliation.match-reversed",
            async (db, ct) =>
            {
                var agencyId = await (from match in db.Set<ReconciliationMatch>()
                    join reconciliation in db.Set<Reconciliation>() on match.ReconciliationId equals reconciliation.Id
                    where match.Id == matchId && match.ReversalOfId == null
                    select (Guid?)reconciliation.AgencyId).SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "bank-match-not-found");
                await Authorize(db, actor, agencyId, ct, true);
            },
            async (db, ct) =>
            {
                var original = await db.Set<ReconciliationMatch>().AsNoTracking()
                    .SingleAsync(x => x.Id == matchId, ct);
                var reconciliation = await HoldReconciliation(db, original.ReconciliationId, ct);
                Open(reconciliation);
                await HoldLine(db, original.BankLineId, ct);
                var posting = await HoldPosting(db, original.FinancePostingId, ct);
                if (await db.Set<ReconciliationMatch>().AnyAsync(x => x.ReversalOfId == matchId, ct))
                    throw new QuoteOperationException(409, "bank-match-already-reversed");
                var now = await MatchEventTime(db, posting, time.GetUtcNow(), ct);
                var reversal = new ReconciliationMatch { ReconciliationId = original.ReconciliationId,
                    BankLineId = original.BankLineId, FinancePostingId = original.FinancePostingId,
                    SignedAmount = original.SignedAmount, ReversalOfId = matchId,
                    Reason = reason.Trim(), MatchedAt = now, CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(reversal); await db.SaveChangesAsync(ct);
                return Outcome(reversal.Id, new { id = reversal.Id, matchId });
            }, token, IsolationLevel.Serializable);
    }

    public Task<CommandOutcome> ExcludeAsync(ActorContext actor, Guid reconciliationId, Guid bankLineId,
        Guid duplicateOfBankLineId, string evidenceReference, string reason, string key, Guid correlationId,
        CancellationToken token = default)
    {
        if (bankLineId == Guid.Empty || duplicateOfBankLineId == Guid.Empty || bankLineId == duplicateOfBankLineId ||
            !Bounded(evidenceReference, 300, 10) || !Bounded(reason, 1000, 10))
            throw new QuoteOperationException(400, "bank-exclusion-invalid");
        return Within(actor, reconciliationId, "exclusions", key, correlationId,
            new { reconciliationId, bankLineId, duplicateOfBankLineId, evidenceReference, reason },
            "finance.reconciliation.line-excluded", async (db, reconciliation, ct) =>
            {
                Open(reconciliation);
                var ids = new[] { bankLineId, duplicateOfBankLineId }.OrderBy(x => x).ToArray();
                var lines = new List<BankLine>(2);
                foreach (var id in ids) lines.Add(await HoldLine(db, id, ct));
                var line = lines.Single(x => x.Id == bankLineId);
                var candidate = lines.Single(x => x.Id == duplicateOfBankLineId);
                if (line.AgencyId != reconciliation.AgencyId || candidate.AgencyId != reconciliation.AgencyId ||
                    line.ValueDate < reconciliation.From || line.ValueDate >= reconciliation.To ||
                    line.ValueDate != candidate.ValueDate || line.Reference != candidate.Reference ||
                    line.Currency != candidate.Currency || line.SignedAmount != candidate.SignedAmount)
                    throw new QuoteOperationException(409, "bank-duplicate-evidence-mismatch");
                if (await db.Set<ReconciliationMatch>().AnyAsync(x => x.BankLineId == bankLineId, ct))
                    throw new QuoteOperationException(409, "matched-line-cannot-exclude");
                if (await db.Set<BankLineExclusion>().AnyAsync(x => x.BankLineId == bankLineId || x.BankLineId == duplicateOfBankLineId, ct))
                    throw new QuoteOperationException(409, "bank-exclusion-conflict");
                var now = time.GetUtcNow();
                var exclusion = new BankLineExclusion { ReconciliationId = reconciliationId,
                    BankLineId = bankLineId, DuplicateOfBankLineId = duplicateOfBankLineId,
                    EvidenceReference = evidenceReference.Trim(), Reason = reason.Trim(),
                    ExcludedAt = now, CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(exclusion); await db.SaveChangesAsync(ct);
                return Outcome(exclusion.Id, new { id = exclusion.Id, bankLineId });
            }, token);
    }

    public Task<CommandOutcome> ExplainAsync(ActorContext actor, Guid reconciliationId, Guid bankLineId,
        string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        if (bankLineId == Guid.Empty || !Bounded(reason, 1000, 10))
            throw new QuoteOperationException(400, "variance-explanation-invalid");
        return Within(actor, reconciliationId, "variances", key, correlationId,
            new { reconciliationId, bankLineId, reason }, "finance.reconciliation.variance-explained",
            async (db, reconciliation, ct) =>
            {
                Open(reconciliation);
                var line = await HoldLine(db, bankLineId, ct);
                if (line.AgencyId != reconciliation.AgencyId || line.ValueDate < reconciliation.From || line.ValueDate >= reconciliation.To)
                    throw new QuoteOperationException(404, "bank-line-not-in-reconciliation");
                if (await db.Set<BankLineExclusion>().AnyAsync(x => x.BankLineId == bankLineId, ct))
                    throw new QuoteOperationException(409, "excluded-line-has-no-variance");
                var residual = await Residual(db, bankLineId, FinanceLedgerMath.Pence(line.SignedAmount), ct);
                if (residual == 0) throw new QuoteOperationException(409, "variance-already-matched");
                var now = time.GetUtcNow();
                var explanation = new ReconciliationVariance { ReconciliationId = reconciliationId,
                    BankLineId = bankLineId, SignedResidual = residual / 100m, Reason = reason.Trim(),
                    ExplainedAt = now, CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(explanation); await db.SaveChangesAsync(ct);
                return Outcome(explanation.Id, new { id = explanation.Id, bankLineId, residual = Money(residual) });
            }, token);
    }

    public Task<CommandOutcome> ExplainTargetAsync(ActorContext actor, Guid reconciliationId, Guid postingId,
        string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        if (postingId == Guid.Empty || !Bounded(reason, 1000, 10))
            throw new QuoteOperationException(400, "cash-variance-explanation-invalid");
        return Within(actor, reconciliationId, "target-variances", key, correlationId,
            new { reconciliationId, postingId, reason }, "finance.reconciliation.cash-variance-explained",
            async (db, reconciliation, ct) =>
            {
                Open(reconciliation);
                var posting = await HoldPosting(db, postingId, ct);
                if (posting.AgencyId != reconciliation.AgencyId ||
                    posting.PostingDate < reconciliation.From || posting.PostingDate >= reconciliation.To ||
                    !CashSources.Contains(posting.SourceKind, StringComparer.Ordinal) || posting.CashDelta == 0)
                    throw new QuoteOperationException(404, "cash-source-not-in-reconciliation");
                var residual = await TargetResidual(db, postingId, FinanceLedgerMath.Pence(posting.CashDelta), ct);
                if (residual == 0) throw new QuoteOperationException(409, "cash-variance-already-matched");
                var now = time.GetUtcNow();
                var explanation = new ReconciliationTargetVariance { ReconciliationId = reconciliationId,
                    FinancePostingId = postingId, SignedResidual = residual / 100m, Reason = reason.Trim(),
                    ExplainedAt = now, CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(explanation); await db.SaveChangesAsync(ct);
                return Outcome(explanation.Id, new { id = explanation.Id, financePostingId = postingId,
                    residual = Money(residual) });
            }, token);
    }

    public Task<CommandOutcome> CompleteAsync(ActorContext actor, Guid reconciliationId,
        string key, Guid correlationId, CancellationToken token = default)
        => Within(actor, reconciliationId, "complete", key, correlationId,
            new { reconciliationId }, "finance.reconciliation.completed",
            async (db, reconciliation, ct) =>
            {
                Open(reconciliation);
                var view = await Build(db, reconciliation, ct);
                if (view.UnaddressedCount != 0) throw new QuoteOperationException(409, "variance-unexplained");
                reconciliation.CompletedAt = time.GetUtcNow(); reconciliation.CompletedBy = actor.UserId;
                await db.SaveChangesAsync(ct);
                return Outcome(reconciliation.Id, new { id = reconciliation.Id, actualVariance = view.NetVariance,
                    absoluteVariance = view.AbsoluteVariance, completedAt = reconciliation.CompletedAt });
            }, token);

    public async Task<BankLineView> BankLineAsync(ActorContext actor, Guid id, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var line = await db.Set<BankLine>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new QuoteOperationException(404, "bank-line-not-found");
        await Authorize(db, actor, line.AgencyId, token);
        var view = await BankView(db, line, token);
        await tx.CommitAsync(token); return view;
    }

    public async Task<BankLinePage> BankLinesAsync(ActorContext actor, Guid agencyId, int page, int pageSize,
        CancellationToken token = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new QuoteOperationException(400, "bank-line-query-invalid");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await Authorize(db, actor, agencyId, token);
        var query = db.Set<BankLine>().AsNoTracking().Where(x => x.AgencyId == agencyId);
        var count = await query.CountAsync(token);
        var lines = await query.OrderByDescending(x => x.ValueDate).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        var views = new List<BankLineView>(lines.Length);
        foreach (var line in lines) views.Add(await BankView(db, line, token));
        await tx.CommitAsync(token); return new BankLinePage(page, pageSize, count, views);
    }

    public async Task<ReconciliationView> DetailAsync(ActorContext actor, Guid id, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var row = await db.Set<Reconciliation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new QuoteOperationException(404, "reconciliation-not-found");
        await Authorize(db, actor, row.AgencyId, token);
        var view = await Build(db, row, token);
        await tx.CommitAsync(token); return view;
    }

    private Task<CommandOutcome> Within<T>(ActorContext actor, Guid reconciliationId, string action,
        string key, Guid correlationId, T request, string eventType,
        Func<BackOfficeDbContext, Reconciliation, CancellationToken, Task<CommandOutcome>> handler,
        CancellationToken token)
    {
        if (reconciliationId == Guid.Empty) throw new QuoteOperationException(400, "reconciliation-id-invalid");
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
                $"/api/v1/finance/reconciliations/{reconciliationId:N}/{action}", key, correlationId),
            request, eventType,
            async (db, ct) =>
            {
                var agencyId = await db.Set<Reconciliation>().AsNoTracking()
                    .Where(x => x.Id == reconciliationId).Select(x => (Guid?)x.AgencyId).SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "reconciliation-not-found");
                await Authorize(db, actor, agencyId, ct, true);
            },
            async (db, ct) => await handler(db, await HoldReconciliation(db, reconciliationId, ct), ct),
            token, IsolationLevel.Serializable);
    }

    private static async Task Authorize(BackOfficeDbContext db, ActorContext actor, Guid agencyId,
        CancellationToken token, bool write = false)
    {
        if (!actor.HasCapability(write ? "finance-reconcile" : "finance-read"))
            throw new QuoteOperationException(403, "reconciliation-scope-denied");
        await FinanceLedgerService.Authorize(db, actor, agencyId, null, token, write);
    }
    private static Task<Reconciliation> HoldReconciliation(BackOfficeDbContext db, Guid id, CancellationToken ct)
        => db.Set<Reconciliation>().FromSqlInterpolated($"SELECT * FROM Reconciliation WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}")
            .SingleAsync(ct);
    private static Task<BankLine> HoldLine(BackOfficeDbContext db, Guid id, CancellationToken ct)
        => db.Set<BankLine>().FromSqlInterpolated($"SELECT * FROM BankLine WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}")
            .AsNoTracking().SingleAsync(ct);
    private static Task<FinancePosting> HoldPosting(BackOfficeDbContext db, Guid id, CancellationToken ct)
        => db.Set<FinancePosting>().FromSqlInterpolated($"SELECT * FROM FinancePosting WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}")
            .AsNoTracking().SingleAsync(ct);
    private static async Task<DateTimeOffset> MatchEventTime(BackOfficeDbContext db, FinancePosting posting,
        DateTimeOffset now, CancellationToken ct)
    {
        var completed = await db.Set<Reconciliation>().AsNoTracking()
            .Where(x => x.AgencyId == posting.AgencyId && x.From <= posting.PostingDate &&
                posting.PostingDate < x.To && x.CompletedAt != null)
            .MaxAsync(x => x.CompletedAt, ct);
        return completed is not null && now <= completed ? completed.Value.AddTicks(1) : now;
    }
    private static void Open(Reconciliation row)
    {
        if (row.CompletedAt is not null) throw new QuoteOperationException(409, "reconciliation-completed");
    }
    private static long Signed(string text)
    {
        try { return FinanceReconciliationMath.SignedPence(text); }
        catch (Exception error) when (error is ArgumentOutOfRangeException or OverflowException)
        { throw new QuoteOperationException(400, "bank-amount-invalid"); }
    }
    private static bool Bounded(string? value, int max, int min = 1)
        => value is not null && value.Trim().Length >= min && value.Length <= max;
    private static string Money(long pence) => FinanceLedgerMath.Money(pence / 100m);
    private static CommandOutcome Outcome(Guid id, object body)
        => new(id, 201, JsonSerializer.Serialize(body, Json));

    private static async Task<long> Residual(BackOfficeDbContext db, Guid lineId, long source, CancellationToken ct)
    {
        var rows = await db.Set<ReconciliationMatch>().AsNoTracking().Where(x => x.BankLineId == lineId).ToArrayAsync(ct);
        return FinanceReconciliationMath.Residual(source,
            rows.Where(x => x.ReversalOfId is null).Select(x => FinanceLedgerMath.Pence(x.SignedAmount)),
            rows.Where(x => x.ReversalOfId is not null).Select(x => FinanceLedgerMath.Pence(x.SignedAmount)));
    }
    private static async Task<long> TargetResidual(BackOfficeDbContext db, Guid postingId, long source, CancellationToken ct)
    {
        var rows = await db.Set<ReconciliationMatch>().AsNoTracking().Where(x => x.FinancePostingId == postingId).ToArrayAsync(ct);
        return FinanceReconciliationMath.Residual(source,
            rows.Where(x => x.ReversalOfId is null).Select(x => FinanceLedgerMath.Pence(x.SignedAmount)),
            rows.Where(x => x.ReversalOfId is not null).Select(x => FinanceLedgerMath.Pence(x.SignedAmount)));
    }
    private static async Task<BankLineView> BankView(BackOfficeDbContext db, BankLine line, CancellationToken ct)
    {
        var candidates = await db.Set<BankLine>().AsNoTracking().Where(x => x.AgencyId == line.AgencyId &&
            x.Id != line.Id && x.ValueDate == line.ValueDate && x.Reference == line.Reference &&
            x.SignedAmount == line.SignedAmount && x.Currency == line.Currency)
            .OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(ct);
        return new BankLineView(line.Id, line.AgencyId, line.ImportKey, line.ValueDate, line.Reference,
            FinanceLedgerMath.Money(line.SignedAmount), line.Currency, line.RawJson, line.ImportedAt, candidates);
    }
    private static async Task<ReconciliationView> Build(BackOfficeDbContext db, Reconciliation row, CancellationToken ct)
    {
        var lines = await db.Set<BankLine>().AsNoTracking().Where(x => x.AgencyId == row.AgencyId &&
            x.ValueDate >= row.From && x.ValueDate < row.To).OrderBy(x => x.ValueDate).ThenBy(x => x.Id).ToArrayAsync(ct);
        var ids = lines.Select(x => x.Id).ToArray();
        var matches = await db.Set<ReconciliationMatch>().AsNoTracking().Where(x => x.ReconciliationId == row.Id && ids.Contains(x.BankLineId))
            .OrderBy(x => x.MatchedAt).ThenBy(x => x.Id).ToArrayAsync(ct);
        var exclusions = await db.Set<BankLineExclusion>().AsNoTracking().Where(x => x.ReconciliationId == row.Id && ids.Contains(x.BankLineId))
            .ToDictionaryAsync(x => x.BankLineId, ct);
        var explanations = await db.Set<ReconciliationVariance>().AsNoTracking()
            .Where(x => x.ReconciliationId == row.Id && ids.Contains(x.BankLineId))
            .OrderByDescending(x => x.ExplainedAt).ThenByDescending(x => x.Id).ToArrayAsync(ct);
        var latest = explanations.GroupBy(x => x.BankLineId).ToDictionary(x => x.Key, x => x.First());
        var postings = await db.Set<FinancePosting>().AsNoTracking().Where(x => x.AgencyId == row.AgencyId &&
            x.PostingDate >= row.From && x.PostingDate < row.To && CashSources.Contains(x.SourceKind) && x.CashDelta != 0)
            .OrderBy(x => x.PostingDate).ThenBy(x => x.Id).ToArrayAsync(ct);
        var postingIds = postings.Select(x => x.Id).ToArray();
        var completionCutoff = row.CompletedAt;
        var targetMatches = await db.Set<ReconciliationMatch>().AsNoTracking()
            .Where(x => postingIds.Contains(x.FinancePostingId) &&
                (completionCutoff == null || x.MatchedAt <= completionCutoff)).ToArrayAsync(ct);
        var targetExplanations = await db.Set<ReconciliationTargetVariance>().AsNoTracking()
            .Where(x => x.ReconciliationId == row.Id).OrderByDescending(x => x.ExplainedAt)
            .ThenByDescending(x => x.Id).ToArrayAsync(ct);
        var latestTarget = targetExplanations.GroupBy(x => x.FinancePostingId)
            .ToDictionary(x => x.Key, x => x.First());
        long net = 0, absolute = 0; int unaddressed = 0;
        var views = new List<ReconciliationLineView>(lines.Length);
        foreach (var line in lines)
        {
            var source = FinanceLedgerMath.Pence(line.SignedAmount);
            var related = matches.Where(x => x.BankLineId == line.Id).ToArray();
            var residual = FinanceReconciliationMath.Residual(source,
                related.Where(x => x.ReversalOfId is null).Select(x => FinanceLedgerMath.Pence(x.SignedAmount)),
                related.Where(x => x.ReversalOfId is not null).Select(x => FinanceLedgerMath.Pence(x.SignedAmount)));
            var excluded = exclusions.TryGetValue(line.Id, out var exclusion);
            if (!excluded) { net = checked(net + residual); absolute = checked(absolute + (long)Math.Abs((decimal)residual)); }
            var explained = latest.TryGetValue(line.Id, out var explanation) &&
                FinanceLedgerMath.Pence(explanation.SignedResidual) == residual && residual != 0;
            var addressed = excluded || residual == 0 || explained;
            if (!addressed) unaddressed++;
            views.Add(new ReconciliationLineView(line.Id, line.ImportKey, line.ValueDate,
                FinanceLedgerMath.Money(line.SignedAmount), Money(residual), excluded,
                exclusion?.DuplicateOfBankLineId, explained ? explanation!.Reason : null, addressed));
        }
        var targets = new List<ReconciliationTargetView>(postings.Length);
        foreach (var posting in postings)
        {
            var source = FinanceLedgerMath.Pence(posting.CashDelta);
            var related = targetMatches.Where(x => x.FinancePostingId == posting.Id).ToArray();
            var residual = FinanceReconciliationMath.Residual(source,
                related.Where(x => x.ReversalOfId is null).Select(x => FinanceLedgerMath.Pence(x.SignedAmount)),
                related.Where(x => x.ReversalOfId is not null).Select(x => FinanceLedgerMath.Pence(x.SignedAmount)));
            var explained = latestTarget.TryGetValue(posting.Id, out var explanation) && residual != 0 &&
                FinanceLedgerMath.Pence(explanation.SignedResidual) == residual;
            var addressed = residual == 0 || explained;
            if (!addressed) unaddressed++;
            targets.Add(new ReconciliationTargetView(posting.Id, posting.PostingDate,
                posting.SourceKind, posting.SourceId, Money(source), Money(residual),
                explained ? explanation!.Reason : null, addressed));
        }
        return new ReconciliationView(row.Id, row.AgencyId, row.From, row.To, row.CompletedAt,
            Money(net), Money(absolute), unaddressed, views, targets,
            matches.Select(x => new ReconciliationMatchView(x.Id, x.BankLineId, x.FinancePostingId,
                FinanceLedgerMath.Money(x.SignedAmount), x.ReversalOfId, x.Reason, x.MatchedAt)).ToArray());
    }
}
