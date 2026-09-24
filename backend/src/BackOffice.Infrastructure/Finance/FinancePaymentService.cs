using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Finance;

public sealed record FinancePaymentView(Guid Id, Guid RefundRequestId, Guid AgencyId, Guid CreditObligationId,
    string DebtorKind, Guid DebtorId, string Amount, string Currency, Guid WorkId, string State,
    string? ProviderState, Guid? ProviderOperationId, DateTimeOffset? AppliedAt,
    Guid? PriorPaymentId, string ReviewReason, string Etag);

public sealed class FinancePaymentService(IDbContextFactory<BackOfficeDbContext> factory,
    SqlCommandBoundary commands, TimeProvider time)
{
    private const string ScenarioScope = "finance-refund-payment-demo";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<CommandOutcome> QueueAsync(ActorContext actor, Guid refundId, byte[] expectedVersion,
        Guid? priorPaymentId, string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        if (refundId == Guid.Empty || expectedVersion is not { Length: 8 } ||
            priorPaymentId == Guid.Empty || reason is not { Length: >= 10 and <= 1000 } ||
            reason.Trim().Length < 10) throw new QuoteOperationException(400, "refund-payment-input-invalid");
        reason = reason.Trim();
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
            $"/api/v1/finance/refunds/{refundId:N}/payments", key, correlationId),
            new { refundId, expectedVersion = Convert.ToBase64String(expectedVersion), priorPaymentId, reason },
            "finance.refund.payment-queued",
            async (db, ct) =>
            {
                var request = await db.Set<RefundRequest>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == refundId, ct)
                    ?? throw new QuoteOperationException(404, "refund-not-found");
                await Authorize(db, actor, request.AgencyId, ct, true);
                if (!request.RowVersion.SequenceEqual(expectedVersion))
                    throw new QuoteOperationException(412, "refund-version-stale");
            },
            async (db, ct) =>
            {
                var request = await db.Set<RefundRequest>()
                    .FromSqlInterpolated($"SELECT * FROM RefundRequest WITH(UPDLOCK,HOLDLOCK) WHERE Id={refundId}")
                    .AsNoTracking().SingleAsync(ct);
                if (!request.RowVersion.SequenceEqual(expectedVersion))
                    throw new QuoteOperationException(412, "refund-version-stale");
                if (request.State != "approved") throw new QuoteOperationException(409, "refund-not-approved");
                var previous = await db.Set<FinanceRefundPayment>()
                    .FromSqlInterpolated($"SELECT * FROM FinanceRefundPayment WITH(UPDLOCK,HOLDLOCK) WHERE RefundRequestId={refundId}")
                    .AsNoTracking().ToArrayAsync(ct);
                var latest = previous.SingleOrDefault(x => !previous.Any(y => y.PriorPaymentId == x.Id));
                if (priorPaymentId is null && previous.Length != 0 ||
                    priorPaymentId is not null && (latest?.Id != priorPaymentId ||
                        latest.State != "rejected" || latest.ProviderState != "rejected"))
                    throw new QuoteOperationException(409, "refund-payment-already-queued");
                var reserved = await db.Set<RefundCashReservation>().AsNoTracking()
                    .Where(x => x.RefundRequestId == refundId).Select(x => x.Amount).ToArrayAsync(ct);
                if (reserved.Sum() != request.Amount) throw new QuoteOperationException(409, "refund-reservation-invalid");
                var scenario = await db.Set<SettingVersion>().AsNoTracking()
                    .Where(x => x.Scope == ScenarioScope && x.EffectiveFrom <= time.GetUtcNow())
                    .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException("Demo refund payment scenario is unavailable.");
                _ = FinancePaymentWorker.Scenario(scenario);
                var now = time.GetUtcNow();
                var payment = new FinanceRefundPayment { RefundRequestId = refundId, AgencyId = request.AgencyId,
                    CreditObligationId = request.CreditObligationId, DebtorKind = request.DebtorKind,
                    DebtorId = request.DebtorId, Amount = request.Amount, Currency = request.Currency,
                    ScenarioVersionId = scenario.Id, PriorPaymentId = priorPaymentId,
                    ReviewReason = reason, CreatedAt = now, UpdatedAt = now, CreatedBy = actor.UserId };
                payment.OperationKey = $"finance-refund-payment/{payment.Id:N}";
                payment.RequestHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                { payment.Id, refundId, request.CreditObligationId, request.DebtorKind, request.DebtorId,
                    request.Amount, request.Currency, scenarioVersionId = scenario.Id, priorPaymentId }, Json));
                var work = new OutboxWork { Kind = FinancePaymentWorker.WorkKind,
                    SubjectRecordId = payment.Id, OperationKey = payment.OperationKey,
                    Payload = JsonSerializer.Serialize(new { paymentId = payment.Id, refundId }, Json),
                    ScenarioVersionId = scenario.Id, NextAttemptAt = now, CreatedAt = now, UpdatedAt = now,
                    CreatedBy = actor.UserId, CorrelationId = correlationId };
                payment.WorkId = work.Id;
                db.Add(work); db.Add(payment); await db.SaveChangesAsync(ct);
                return new CommandOutcome(payment.Id, 202, JsonSerializer.Serialize(new
                    { paymentId = payment.Id, refundId, state = payment.State }, Json));
            }, token, IsolationLevel.Serializable);
    }

    public Task<CommandOutcome> ResumeAsync(ActorContext actor, Guid paymentId, byte[] expectedVersion,
        string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        if (paymentId == Guid.Empty || expectedVersion is not { Length: 8 } ||
            reason is not { Length: >= 10 and <= 1000 } || reason.Trim().Length < 10)
            throw new QuoteOperationException(400, "refund-payment-resume-invalid");
        reason = reason.Trim();
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
            $"/api/v1/finance/payments/{paymentId:N}/resume", key, correlationId),
            new { paymentId, expectedVersion = Convert.ToBase64String(expectedVersion), reason },
            "finance.refund.payment-resumed",
            async (db, ct) =>
            {
                var payment = await db.Set<FinanceRefundPayment>().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == paymentId, ct)
                    ?? throw new QuoteOperationException(404, "refund-payment-not-found");
                await Authorize(db, actor, payment.AgencyId, ct, true);
                if (!payment.RowVersion.SequenceEqual(expectedVersion))
                    throw new QuoteOperationException(412, "refund-payment-version-stale");
            },
            async (db, ct) =>
            {
                var payment = await db.Set<FinanceRefundPayment>()
                    .FromSqlInterpolated($"SELECT * FROM FinanceRefundPayment WITH(UPDLOCK,HOLDLOCK) WHERE Id={paymentId}")
                    .SingleAsync(ct);
                if (!payment.RowVersion.SequenceEqual(expectedVersion))
                    throw new QuoteOperationException(412, "refund-payment-version-stale");
                var work = await db.Set<OutboxWork>()
                    .FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,HOLDLOCK) WHERE Id={payment.WorkId}")
                    .SingleAsync(ct);
                var operation = await db.Set<DemoProviderOperation>().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Kind == FinancePaymentWorker.WorkKind &&
                        x.OperationKey == payment.OperationKey, ct);
                if (payment.State != "failed" || work.State != "failed" ||
                    operation?.Result is not null || work.Kind != FinancePaymentWorker.WorkKind ||
                    work.OperationKey != payment.OperationKey)
                    throw new QuoteOperationException(409, "refund-payment-resume-unavailable");
                var original = await db.Set<StaffUser>().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == payment.CreatedBy, ct);
                if (original is null || original.State != "active" || original.AgencyId is not null ||
                    !await (from grant in db.Set<UserRole>() join role in db.Set<Role>()
                        on grant.RoleId equals role.Id where grant.UserId == original.Id &&
                        role.Code == "finance" && role.Scope == "internal" select grant).AnyAsync(ct))
                    throw new QuoteOperationException(403, "refund-payment-originator-authority-required");
                var nextLimit = work.Attempts < work.AttemptLimit ? work.AttemptLimit :
                    work.AttemptLimit == 6 ? 12 : work.AttemptLimit == 12 ? 18 : 0;
                if (nextLimit == 0) throw new QuoteOperationException(409, "refund-payment-retry-budget-exhausted");
                var now = time.GetUtcNow();
                work.AttemptLimit = nextLimit; work.State = "pending"; work.NextAttemptAt = now;
                work.CompletedAt = null; work.ErrorCode = null; work.LeaseToken = null; work.LeaseExpiresAt = null;
                await db.SaveChangesAsync(ct);
                payment.State = "queued"; payment.UpdatedAt = now;
                db.Add(new AuditEvent { ActorId = actor.UserId, SubjectRecordId = payment.Id,
                    EventType = "finance.refund.payment-resume-reviewed", Reason = reason,
                    OccurredAt = now, CorrelationId = correlationId,
                    After = JsonSerializer.Serialize(new { paymentId, workId = work.Id,
                        attemptLimit = nextLimit }, Json) });
                await db.SaveChangesAsync(ct);
                return new CommandOutcome(payment.Id, 202, JsonSerializer.Serialize(new
                { paymentId = payment.Id, refundId = payment.RefundRequestId, state = payment.State }, Json));
            }, token, IsolationLevel.Serializable);
    }

    public async Task<FinancePaymentView> DetailAsync(ActorContext actor, Guid paymentId, CancellationToken token = default)
    {
        if (paymentId == Guid.Empty) throw new QuoteOperationException(400, "refund-payment-input-invalid");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var payment = await db.Set<FinanceRefundPayment>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == paymentId, token)
            ?? throw new QuoteOperationException(404, "refund-payment-not-found");
        await Authorize(db, actor, payment.AgencyId, token, false);
        var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == payment.WorkId, token);
        var savedOperation = await db.Set<DemoProviderOperation>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Kind == FinancePaymentWorker.WorkKind &&
                x.OperationKey == payment.OperationKey, token);
        await tx.CommitAsync(token);
        var acknowledged = savedOperation?.Result is not null &&
            savedOperation.ScenarioVersionId == payment.ScenarioVersionId &&
            CryptographicOperations.FixedTimeEquals(savedOperation.RequestHash, payment.RequestHash) &&
            savedOperation.State is "accepted" or "rejected";
        var state = payment.State == "queued" && acknowledged
            ? "provider-acknowledged/application-pending" :
            payment.State == "queued" && work.ErrorCode == "provider-timeout" ? "uncertain" : payment.State;
        return new FinancePaymentView(payment.Id, payment.RefundRequestId, payment.AgencyId,
            payment.CreditObligationId, payment.DebtorKind, payment.DebtorId,
            FinanceLedgerMath.Money(payment.Amount), payment.Currency, work.Id, state,
            acknowledged ? savedOperation!.State : payment.ProviderState,
            acknowledged ? savedOperation!.Id : payment.ProviderOperationId, payment.AppliedAt,
            payment.PriorPaymentId, payment.ReviewReason,
            '"' + Convert.ToBase64String(payment.RowVersion) + '"');
    }

    private static async Task Authorize(BackOfficeDbContext db, ActorContext actor, Guid agencyId,
        CancellationToken ct, bool write)
    {
        if (!actor.HasCapability(write ? "finance-payment-execute" : "finance-read"))
            throw new QuoteOperationException(403, "refund-payment-scope-denied");
        await FinanceLedgerService.Authorize(db, actor, agencyId, null, ct, write);
    }
}
