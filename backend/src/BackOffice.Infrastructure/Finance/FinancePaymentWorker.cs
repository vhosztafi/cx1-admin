using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Finance;

public sealed record FinancePaymentProviderOutcome(Guid OperationId, string EventId, Guid PaymentId,
    Guid RefundRequestId, string RequestHash, string State, string? ProviderReference,
    DateTimeOffset CompletedAt);
public sealed class FinancePaymentWorkerException(JobFailure failure) : Exception("Demo refund payment did not complete.")
{
    public JobFailure Failure { get; } = failure;
}

public sealed class FinancePaymentWorker(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public const string WorkKind = "finance-refund-payment";
    private const string InboxProvider = "finance-refund-demo";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static string Scenario(SettingVersion setting)
    {
        if (setting.Scope != "finance-refund-payment-demo") throw Failure(JobFailure.InvalidPayload);
        try
        {
            using var document = JsonDocument.Parse(setting.Values);
            var value = document.RootElement.GetProperty("scenario").GetString();
            if (value is "success" or "reject" or "fail-once" or "timeout-after-success") return value;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException) { }
        throw Failure(JobFailure.InvalidPayload);
    }

    public async Task<FinancePaymentProviderOutcome?> ExecuteProviderAsync(JobLease lease,
        CancellationToken token = default)
    {
        if (lease.Kind != WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var payment = await db.Set<FinanceRefundPayment>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token) ?? throw Failure(JobFailure.InvalidPayload);
        var owned = await SqlJobLeases.OwnedAsync(db, lease, time.GetUtcNow(), token);
        if (owned is null) return null;
        if (owned.OperationKey != payment.OperationKey || owned.ScenarioVersionId != payment.ScenarioVersionId)
            throw Failure(JobFailure.ProviderConflict);
        var request = await db.Set<RefundRequest>().AsNoTracking()
            .SingleAsync(x => x.Id == payment.RefundRequestId, token);
        if (request.State != "approved" || request.AgencyId != payment.AgencyId ||
            request.CreditObligationId != payment.CreditObligationId || request.DebtorKind != payment.DebtorKind ||
            request.DebtorId != payment.DebtorId || request.Amount != payment.Amount ||
            request.Currency != payment.Currency) throw Failure(JobFailure.Superseded);
        var prior = await db.Set<DemoProviderOperation>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.Kind == WorkKind && x.OperationKey == payment.OperationKey, token);
        var recoverable = prior?.Result is not null && prior.ScenarioVersionId == payment.ScenarioVersionId &&
            CryptographicOperations.FixedTimeEquals(prior.RequestHash, payment.RequestHash);
        if (!recoverable) await CurrentActor(db, payment.CreatedBy!.Value, token);
        var setting = await db.Set<SettingVersion>().AsNoTracking()
            .SingleAsync(x => x.Id == payment.ScenarioVersionId, token);
        var scenario = Scenario(setting);
        await tx.CommitAsync(token);
        return await ProviderAsync(payment, lease, scenario, token);
    }

    private async Task<FinancePaymentProviderOutcome> ProviderAsync(FinanceRefundPayment payment,
        JobLease lease, string scenario, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var operation = await db.Set<DemoProviderOperation>()
            .FromSqlInterpolated($"SELECT * FROM DemoProviderOperation WITH(UPDLOCK,HOLDLOCK) WHERE Kind={WorkKind} AND OperationKey={lease.OperationKey}")
            .SingleOrDefaultAsync(token);
        var existed = operation is not null;
        if (operation is not null && (operation.ScenarioVersionId != payment.ScenarioVersionId ||
            !CryptographicOperations.FixedTimeEquals(operation.RequestHash, payment.RequestHash)))
            throw Failure(JobFailure.ProviderConflict);
        if (operation?.Result is not null)
        {
            var saved = Read(operation.Result);
            Validate(operation, payment, saved);
            await tx.CommitAsync(token); return saved;
        }
        await CurrentActor(db, payment.CreatedBy!.Value, token);
        var request = await db.Set<RefundRequest>()
            .FromSqlInterpolated($"SELECT * FROM RefundRequest WITH(UPDLOCK,HOLDLOCK) WHERE Id={payment.RefundRequestId}")
            .AsNoTracking().SingleAsync(token);
        if (request.State != "approved") throw Failure(JobFailure.Superseded);
        if (operation is null)
        {
            operation = new DemoProviderOperation { Kind = WorkKind, OperationKey = lease.OperationKey,
                RequestHash = payment.RequestHash, ScenarioVersionId = payment.ScenarioVersionId,
                CreatedAt = time.GetUtcNow(), UpdatedAt = time.GetUtcNow() };
            db.Add(operation);
        }
        if (!existed && scenario == "fail-once")
        {
            operation.State = "transient-failed";
            await db.SaveChangesAsync(token); await tx.CommitAsync(token);
            throw Failure(JobFailure.ProviderUnavailable);
        }
        var now = time.GetUtcNow();
        var state = scenario == "reject" ? "rejected" : "accepted";
        var outcome = new FinancePaymentProviderOutcome(operation.Id,
            $"finance-refund-demo/{operation.Id:N}", payment.Id, payment.RefundRequestId,
            Convert.ToHexString(payment.RequestHash), state,
            state == "accepted" ? $"PAY-DEMO-{payment.Id:N}" : null, now);
        operation.State = state; operation.CompletedAt = now;
        operation.Result = JsonSerializer.Serialize(outcome, Json);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        if (!existed && scenario == "timeout-after-success") throw Failure(JobFailure.ProviderTimeout);
        return outcome;
    }

    public async Task<InboxApplication> ApplyAsync(JobLease lease, FinancePaymentProviderOutcome outcome,
        CancellationToken token = default)
    {
        if (lease.Kind != WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var payment = await db.Set<FinanceRefundPayment>().SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token)
            ?? throw Failure(JobFailure.InvalidPayload);
        var work = await db.Set<OutboxWork>()
            .FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,ROWLOCK) WHERE Id={lease.WorkId}")
            .SingleAsync(token);
        if (work.Kind != WorkKind || work.OperationKey != payment.OperationKey ||
            work.ScenarioVersionId != payment.ScenarioVersionId) throw Failure(JobFailure.ProviderConflict);
        var operation = await db.Set<DemoProviderOperation>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == outcome.OperationId, token) ?? throw Failure(JobFailure.ProviderConflict);
        if (operation.Kind != WorkKind || operation.OperationKey != payment.OperationKey ||
            operation.ScenarioVersionId != payment.ScenarioVersionId || operation.Result is null ||
            !CryptographicOperations.FixedTimeEquals(operation.RequestHash, payment.RequestHash))
            throw Failure(JobFailure.ProviderConflict);
        var saved = Read(operation.Result);
        Validate(operation, payment, saved);
        if (outcome != saved) throw Failure(JobFailure.ProviderConflict);
        var responseHash = SHA256.HashData(Encoding.UTF8.GetBytes(operation.Result));
        var inbox = await db.Set<AdapterInbox>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.Provider == InboxProvider && x.EventId == saved.EventId, token);
        if (inbox is not null)
        {
            if (inbox.WorkId != work.Id || !CryptographicOperations.FixedTimeEquals(inbox.ContentHash, responseHash))
                throw Failure(JobFailure.ProviderConflict);
            await tx.CommitAsync(token); return InboxApplication.Duplicate;
        }
        var now = time.GetUtcNow();
        if (work.State != "leased" || work.LeaseToken != lease.Token || work.Attempts != lease.Attempt ||
            work.LeaseExpiresAt is null || work.LeaseExpiresAt <= now) return InboxApplication.StaleLease;
        var attempt = await db.Set<AdapterAttempt>().SingleAsync(x => x.WorkId == work.Id &&
            x.AttemptNumber == lease.Attempt, token);
        attempt.EndedAt = now;
        attempt.Outcome = saved.State == "accepted" ? "succeeded" : "rejected";
        attempt.Response = operation.Result;
        payment.ProviderState = saved.State;
        payment.ProviderOperationId = operation.Id;
        payment.ProviderEventId = saved.EventId;
        payment.AppliedAt = now;
        if (saved.State == "accepted")
        {
            var period = await AccountingPeriods.HoldAsync(db, now, token);
            var credit = await db.Set<IssueFinancialObligation>().AsNoTracking()
                .SingleAsync(x => x.Id == payment.CreditObligationId, token);
            await db.SaveChangesAsync(token);
            db.Add(new FinancePosting { SourceKind = "refund", SourceId = payment.Id,
                AgencyId = payment.AgencyId, RelationshipId = credit.RelationshipId,
                PolicyId = credit.PolicyId, TransactionId = credit.TransactionId,
                DebtorKind = payment.DebtorKind, AccountingPeriodId = period.PeriodId,
                PostingDate = period.PostingDate, EffectiveAt = now, PostedAt = now,
                Currency = payment.Currency, DebtorDelta = payment.Amount, CashDelta = -payment.Amount,
                Reason = "Accepted demo refund payment to original payee", CreatedAt = now,
                CreatedBy = payment.CreatedBy });
            await db.SaveChangesAsync(token);
            payment.State = "paid";
            work.State = "succeeded"; work.CompletedAt = now; work.ErrorCode = null;
            work.LeaseToken = null; work.LeaseExpiresAt = null;
            work.Result = JsonSerializer.Serialize(new { paymentId = payment.Id }, Json);
        }
        else
        {
            payment.State = "rejected";
            attempt.ErrorCode = "provider-rejected";
            await SqlJobLeases.MarkTerminalAsync(db, work, "provider-rejected", now, token);
        }
        db.Add(new AdapterInbox { Provider = InboxProvider, EventId = saved.EventId,
            ContentHash = responseHash, WorkId = work.Id, State = "applied", AppliedAt = now,
            CreatedAt = now, UpdatedAt = now });
        db.Add(new AuditEvent { ActorId = payment.CreatedBy, SubjectRecordId = payment.RefundRequestId,
            EventType = "finance.refund.payment-applied", OccurredAt = now,
            CorrelationId = work.CorrelationId,
            After = JsonSerializer.Serialize(new { paymentId = payment.Id, state = payment.State }, Json) });
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        return InboxApplication.Applied;
    }

    private static FinancePaymentProviderOutcome Read(string json)
    {
        try { return JsonSerializer.Deserialize<FinancePaymentProviderOutcome>(json, Json) ??
            throw Failure(JobFailure.ProviderConflict); }
        catch (JsonException) { throw Failure(JobFailure.ProviderConflict); }
    }
    private static void Validate(DemoProviderOperation operation, FinanceRefundPayment payment,
        FinancePaymentProviderOutcome value)
    {
        if (value.OperationId != operation.Id || value.EventId != $"finance-refund-demo/{operation.Id:N}" ||
            value.PaymentId != payment.Id || value.RefundRequestId != payment.RefundRequestId ||
            value.RequestHash != Convert.ToHexString(payment.RequestHash) ||
            value.CompletedAt != operation.CompletedAt || value.State is not ("accepted" or "rejected") ||
            operation.State != value.State || value.ProviderReference !=
            (value.State == "accepted" ? $"PAY-DEMO-{payment.Id:N}" : null))
            throw Failure(JobFailure.ProviderConflict);
    }
    private static async Task CurrentActor(BackOfficeDbContext db, Guid actorId, CancellationToken token)
    {
        var identity = await IdentitySnapshot.Lock(db, new IdentityReference(actorId, null), token);
        if (identity is null || identity.User.AgencyId is not null ||
            !identity.Roles.Any(x => x.Code == "finance")) throw Failure(JobFailure.Superseded);
    }
    private static FinancePaymentWorkerException Failure(JobFailure failure) => new(failure);
}
