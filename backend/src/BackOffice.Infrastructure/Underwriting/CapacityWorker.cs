using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed record CapacityProviderOutcome(Guid OperationId, string EventId, string Outcome, string Body, DateTimeOffset ReceivedAt, string DefinitionJson);
public sealed class CapacityProviderException(JobFailure failure) : Exception("The fictional capacity provider did not complete.")
{ public JobFailure Failure { get; } = failure; }

public sealed class CapacityWorker(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = QuoteRatingService.Json;
    public async Task<CapacityProviderOutcome> ExecuteProviderAsync(JobLease lease, CancellationToken token = default)
    {
        if (lease.Kind != CapacityService.WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        var submission = await db.Set<CapacitySubmission>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token) ?? throw Failure(JobFailure.InvalidPayload);
        if (submission.ScenarioVersionId != lease.ScenarioVersionId || lease.OperationKey != $"capacity/{submission.Id:N}") throw Failure(JobFailure.ProviderConflict);
        var setting = await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Id == submission.ScenarioVersionId, token);
        var scenario = CapacitySeed.Parse(setting)?.Scenario ?? throw Failure(JobFailure.InvalidPayload);
        var requestHash = Convert.FromHexString(submission.ContextHash);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var operation = await db.Set<DemoProviderOperation>().FromSqlInterpolated($"SELECT * FROM DemoProviderOperation WITH(UPDLOCK,HOLDLOCK) WHERE Kind={lease.Kind} AND OperationKey={lease.OperationKey}").SingleOrDefaultAsync(token);
        var existed = operation is not null;
        if (operation is not null && (operation.ScenarioVersionId != lease.ScenarioVersionId || !CryptographicOperations.FixedTimeEquals(operation.RequestHash, requestHash))) throw Failure(JobFailure.ProviderConflict);
        if (operation?.Result is not null)
        {
            var stored = JsonSerializer.Deserialize<CapacityProviderOutcome>(operation.Result, Json) ?? throw Failure(JobFailure.ProviderConflict);
            await tx.CommitAsync(token); return stored;
        }
        if (operation is null)
        {
            operation = new DemoProviderOperation { Kind = lease.Kind, OperationKey = lease.OperationKey, RequestHash = requestHash, ScenarioVersionId = lease.ScenarioVersionId, CreatedAt = time.GetUtcNow() };
            db.Add(operation);
        }
        if (!existed && scenario == "transient-then-approve")
        { operation.State = "transient-failed"; await db.SaveChangesAsync(token); await tx.CommitAsync(token); throw Failure(JobFailure.ProviderUnavailable); }
        using var request = JsonDocument.Parse(submission.ContextJson); var context = request.RootElement;
        var dimension = BackOffice.Application.Underwriting.CapacityRules.Dimension(context.GetProperty("ruleCode").GetString()!, context.GetProperty("dimension").GetString()!);
        var targets = context.GetProperty("conditionTargetIds").EnumerateArray().Select(x => x.GetGuid()).ToArray();
        var outcome = scenario switch { "query-proof" => "query", "decline-trade" => "decline", "conditional-security" when dimension == "stock-limit" && targets.Length is > 0 and <= 20 => "approve-with-conditions",
            "conditional-security" => "query", _ when dimension == "stock-limit" => "approve", _ => "query" };
        var approving = outcome is "approve" or "approve-with-conditions";
        var conditions = outcome == "approve-with-conditions" ? targets.Select(x => JsonSerializer.SerializeToElement(new { code = "overnight-security", premisesId = x, wordingVersion = "1" })).ToArray() : [];
        var now = time.GetUtcNow(); var startsAt = context.GetProperty("startsAt").GetDateTimeOffset();
        var definition = JsonSerializer.Serialize(new { quoteId = submission.QuoteId, cycleId = submission.CycleId, submissionId = submission.Id, submissionHash = submission.ContextHash,
            outcome, validFrom = approving ? (DateTimeOffset?)(startsAt < now ? startsAt : now) : null, validTo = approving ? (DateTimeOffset?)context.GetProperty("endsAt").GetDateTimeOffset() : null,
            authorisedLimits = approving ? new[] { JsonSerializer.SerializeToElement(new { dimension = "stock-limit", maximumAmount = "150000.00" }) } : [], conditions }, Json);
        var result = new CapacityProviderOutcome(operation.Id, "capacity-" + operation.Id.ToString("N"), outcome,
            "Fictional demo provider: " + outcome + ". Scenario: " + scenario + ". Applies only to the retained submission.", now, definition);
        operation.State = "succeeded"; operation.CompletedAt = now; operation.Result = JsonSerializer.Serialize(result, Json);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token); return result;
    }

    public async Task<bool> ApplyAsync(JobLease lease, CapacityProviderOutcome outcome, CancellationToken token = default)
    {
        if (lease.Kind != CapacityService.WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        var submission = await db.Set<CapacitySubmission>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token) ?? throw Failure(JobFailure.InvalidPayload);
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == submission.CycleId, token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={cycle.AgencyId}").AsNoTracking().SingleAsync(token);
        var identity = await IdentitySnapshot.Lock(db, new IdentityReference(submission.SubmittedBy, null), token);
        var quote = await db.Set<Quote>().FromSqlInterpolated($"SELECT * FROM Quote WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={submission.QuoteId}").AsNoTracking().SingleAsync(token);
        var now = time.GetUtcNow(); var work = await SqlJobLeases.OwnedAsync(db, lease, now, token);
        var operation = await db.Set<DemoProviderOperation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == outcome.OperationId && x.OperationKey == lease.OperationKey && x.Kind == lease.Kind, token);
        if (operation?.Result is null || operation.ScenarioVersionId != submission.ScenarioVersionId || lease.ScenarioVersionId != submission.ScenarioVersionId ||
            lease.OperationKey != $"capacity/{submission.Id:N}" || outcome.EventId != "capacity-" + operation.Id.ToString("N") ||
            !CryptographicOperations.FixedTimeEquals(operation.RequestHash, Convert.FromHexString(submission.ContextHash))) throw Failure(JobFailure.ProviderConflict);
        var serialized = JsonSerializer.Serialize(outcome, Json); var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(serialized));
        var provider = "demo-capacity";
        var inbox = await db.Set<AdapterInbox>().FromSqlInterpolated($"SELECT * FROM AdapterInbox WITH(UPDLOCK,HOLDLOCK) WHERE Provider={provider} AND EventId={outcome.EventId}").SingleOrDefaultAsync(token);
        if (inbox is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(inbox.ContentHash, hash) && !await db.Set<AdapterQuarantine>().AnyAsync(x => x.InboxId == inbox.Id && x.ObservedHash == hash, token))
            {
                db.Add(new AdapterQuarantine { InboxId = inbox.Id, ObservedHash = hash, ReceivedAt = now, Reason = "conflicting-capacity-event", CreatedAt = now });
                await db.SaveChangesAsync(token);
            }
            await tx.CommitAsync(token); return false;
        }
        if (work is null) return false;
        if (work.SubjectRecordId != submission.Id || work.OperationKey != lease.OperationKey || operation.Result != serialized) throw Failure(JobFailure.ProviderConflict);
        var escalation = await db.Set<CapacityEscalation>().FromSqlInterpolated($"SELECT * FROM CapacityEscalation WITH(UPDLOCK,HOLDLOCK) WHERE Id={submission.EscalationId}").SingleAsync(token);
        UnderwritingDecisionContext? held = null;
        if (identity is not null && escalation.State is not ("draft" or "superseded") && escalation.CurrentSubmissionId == submission.Id && escalation.CurrentResponseId is null && quote.CurrentUnderwritingCycleId == cycle.Id)
        {
            try
            {
                var actor = new ActorContext(identity.User.Id, identity.User.TeamId, identity.User.AgencyId, identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal));
                held = await UnderwritingDecisionContext.Hold(db, actor, quote.Id, cycle.Id, "underwriting-escalate", now, true, token);
                held.Current(quote.RowVersion, now, currentPrice: true);
                await CapacityService.HoldEscalation(db, held, escalation.Id, token);
                if (!await CapacityAuthority.SubmissionEvidenceCurrent(db, submission.Id, token)) held = null;
            }
            catch (QuoteOperationException) { held = null; }
        }
        inbox = new AdapterInbox { Provider = provider, EventId = outcome.EventId, ContentHash = hash, WorkId = work.Id,
            // Inbox application means consumed into history. Applicability to the
            // quote is separately recorded on the message and attempt.
            State = "applied", AppliedAt = now, CreatedAt = now, UpdatedAt = now };
        db.Add(inbox); await db.SaveChangesAsync(token);
        var message = new CapacityMessage { QuoteId = quote.Id, CycleId = cycle.Id, EscalationId = escalation.Id, SubmissionId = submission.Id, ReferralId = escalation.ReferralId,
            ProviderId = escalation.ProviderId, Sequence = await CapacityService.NextMessage(db, escalation.Id, token), Direction = "inbound", Provenance = "demo-provider",
            ProviderUnderwriter = "Fictional demo capacity underwriter", ProviderReference = outcome.EventId, ProviderEventId = outcome.EventId, InboxId = inbox.Id,
            Outcome = outcome.Outcome, Body = outcome.Body, DefinitionJson = outcome.DefinitionJson, ContentHash = hash, ReceivedAt = outcome.ReceivedAt,
            RecordedAt = now, RecordedBy = submission.SubmittedBy, CreatedAt = now, CreatedBy = submission.SubmittedBy, ApplicationState = held is null ? "superseded" : "applied" };
        if (held is not null)
        {
            using var definition = JsonDocument.Parse(outcome.DefinitionJson); using var proposal = JsonDocument.Parse(held.Revision.ProposalJson);
            var conditions = definition.RootElement.GetProperty("conditions").EnumerateArray().Select(x => BackOffice.Application.Underwriting.ReferralRules.Condition(x, proposal.RootElement)).ToArray();
            var referral = await db.Set<QuoteReferral>().SingleAsync(x => x.Id == escalation.ReferralId, token);
            await CapacityService.AddCarrierConditions(db, held, referral, message, conditions, now, token);
        }
        db.Add(message); await db.SaveChangesAsync(token);
        if (held is not null)
        {
            escalation.CurrentResponseId = message.Id; escalation.State = CapacityService.ResponseState(outcome.Outcome); escalation.UpdatedAt = now;
            await db.SaveChangesAsync(token); await QuoteReferralService.RefreshState(db, held, now, token);
            await held.Receipt(db, message.Id, 201, "capacity.demo-response", now, token);
        }
        else if (escalation.State != "draft" && escalation.CurrentSubmissionId == submission.Id && escalation.CurrentResponseId is null) { escalation.State = "superseded"; escalation.UpdatedAt = now; }
        var attempt = await db.Set<AdapterAttempt>().SingleAsync(x => x.WorkId == work.Id && x.AttemptNumber == lease.Attempt, token);
        attempt.EndedAt = now; attempt.Outcome = held is null ? "superseded" : "succeeded";
        attempt.Response = JsonSerializer.Serialize(new { escalationId = escalation.Id, messageId = message.Id, applicable = held is not null });
        work.State = "succeeded"; work.CompletedAt = now; work.LeaseToken = null; work.LeaseExpiresAt = null; work.ErrorCode = null; work.Result = attempt.Response;
        db.Add(new AuditEvent { ActorId = submission.SubmittedBy, SubjectRecordId = quote.Id, EventType = "capacity.demo-response-completed", OccurredAt = now, CorrelationId = work.CorrelationId, After = attempt.Response });
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        var setting = await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Id == submission.ScenarioVersionId, token);
        if (CapacitySeed.Parse(setting)?.Scenario == "conflicting-duplicate")
            await ApplyAsync(lease, outcome with { Body = "Conflicting duplicate content" }, token);
        return true;
    }
    private static CapacityProviderException Failure(JobFailure failure) => new(failure);
}
