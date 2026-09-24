using System.Data;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Platform;

public sealed record JobLease(Guid WorkId,Guid Token,int Attempt,string Kind,string OperationKey,Guid ScenarioVersionId,string Payload);
public enum JobFailure { ProviderUnavailable, ProviderTimeout, ProviderRejected, InvalidPayload, ProviderConflict, Superseded }

public sealed class SqlJobLeases(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public const string DiagnosticKind="diagnostic-probe";
    public static readonly TimeSpan LeaseDuration=TimeSpan.FromSeconds(30);

    public async Task<JobLease?> ClaimAsync(CancellationToken cancellationToken = default)
        =>await ClaimKindAsync(DiagnosticKind,cancellationToken);

    public async Task<JobLease?> ClaimKindAsync(string kind,CancellationToken cancellationToken = default)
        =>await ClaimCoreAsync(kind,null,cancellationToken);

    public async Task<JobLease?> ClaimWorkAsync(string kind,Guid workId,CancellationToken cancellationToken=default)
        =>await ClaimCoreAsync(kind,workId,cancellationToken);

    private async Task<JobLease?> ClaimCoreAsync(string kind,Guid? workId,CancellationToken cancellationToken)
    {
        if(kind is not (DiagnosticKind or "agency-notification" or "quote-lookup" or "quote-rating" or "servicing-rating" or "servicing-capacity" or "servicing-delivery" or "renewal-lapse-notification" or "cancellation-notice" or "capacity-escalation" or "quote-delivery" or "operational-delivery" or "operational-claims" or "mid-update" or "cancellation-mid-removal" or "cancellation-certificate-withdrawal" or "cancellation-task-close" or "finance-bordereau-submit"))throw new ArgumentException("Unsupported job kind.");
        await using var db=await factory.CreateDbContextAsync(cancellationToken);
        // REPEATABLE READ permits READPAST even when the database uses read-committed snapshots.
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,cancellationToken);
        var now=time.GetUtcNow();
        var job=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT TOP (1) * FROM [OutboxWork] WITH (UPDLOCK,READPAST,ROWLOCK) WHERE [Kind]={kind} AND ([Kind] NOT IN ('cancellation-certificate-withdrawal','cancellation-task-close') OR EXISTS(SELECT 1 FROM CancellationConsequence c JOIN CancellationIssueDecision d ON d.Id=c.DecisionId WHERE c.WorkId=[OutboxWork].Id AND d.EffectiveAt<={now})) AND ([Kind] NOT IN ('mid-update','cancellation-mid-removal') OR EXISTS(SELECT 1 FROM MidSubmission m WHERE m.WorkId=[OutboxWork].Id)) AND ({workId} IS NULL OR [Id]={workId}) AND (([State]='pending' AND [NextAttemptAt]<={now}) OR ([State]='leased' AND [LeaseExpiresAt]<={now})) ORDER BY [NextAttemptAt],[CreatedAt],[Id]")
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null) return null;
        if (job.State=="leased")
        {
            var abandoned=await db.Set<AdapterAttempt>().SingleOrDefaultAsync(x => x.WorkId==job.Id && x.AttemptNumber==job.Attempts && x.EndedAt==null,cancellationToken);
            if (abandoned is not null) {abandoned.EndedAt=now;abandoned.Outcome="lease-expired";abandoned.ErrorCode="lease-expired";}
        }
        if (job.Attempts>=job.AttemptLimit)
        {
            await MarkTerminalAsync(db,job,"attempts-exhausted",now,cancellationToken);
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return null;
        }
        job.State="leased"; job.LeaseToken=Guid.NewGuid(); job.LeaseExpiresAt=now+LeaseDuration; job.Attempts++;
        db.Add(new AdapterAttempt {WorkId=job.Id,AttemptNumber=job.Attempts,StartedAt=now,Outcome="started",
            Request=JsonSerializer.Serialize(new {kind=job.Kind,operationKey=job.OperationKey})});
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new JobLease(job.Id,job.LeaseToken.Value,job.Attempts,job.Kind,job.OperationKey,job.ScenarioVersionId!.Value,job.Payload);
    }

    public async Task<bool> FailAsync(JobLease lease,JobFailure failure,CancellationToken cancellationToken = default)
    {
        var code=failure switch
        {
            JobFailure.ProviderUnavailable => "provider-unavailable",
            JobFailure.ProviderTimeout => "provider-timeout",
            JobFailure.ProviderRejected => "provider-rejected",
            JobFailure.InvalidPayload => "invalid-payload",
            JobFailure.ProviderConflict => "provider-conflict",
            JobFailure.Superseded => lease.Kind == "operational-delivery" ? "delivery-context-unavailable" : lease.Kind == "operational-claims" ? "claims-context-unavailable" : lease.Kind is "mid-update" or "cancellation-mid-removal" ? "mid-context-unavailable" : lease.Kind == "finance-bordereau-submit" ? "submission-context-unavailable" : lease.Kind.StartsWith("cancellation-",StringComparison.Ordinal) ? "cancellation-context-unavailable" : "invitation-superseded",
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        var transient=failure is JobFailure.ProviderUnavailable or JobFailure.ProviderTimeout;
        await using var db=await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction=await db.Database.BeginTransactionAsync(cancellationToken);
        var now=time.GetUtcNow();
        var job=await OwnedAsync(db,lease,now,cancellationToken);
        if (job is null) return false;
        var attempt=await db.Set<AdapterAttempt>().SingleAsync(x => x.WorkId==job.Id && x.AttemptNumber==lease.Attempt,cancellationToken);
        attempt.EndedAt=now; attempt.Outcome=transient ? "transient-failure" : "rejected"; attempt.ErrorCode=code;
        job.ErrorCode=code;
        if (!transient || job.Attempts>=job.AttemptLimit) await MarkTerminalAsync(db,job,code,now,cancellationToken);
        else
        {
            job.State="pending"; job.NextAttemptAt=now+JobRetryBudget.Delay(job.Attempts,job.OperationKey);
            job.LeaseToken=null; job.LeaseExpiresAt=null;
        }
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return true;
    }

    // Lock and fence every final state change, including provider-result application.
    internal static Task<OutboxWork?> OwnedAsync(BackOfficeDbContext db,JobLease lease,DateTimeOffset now,CancellationToken cancellationToken) =>
        db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM [OutboxWork] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={lease.WorkId} AND [State]='leased' AND [LeaseToken]={lease.Token} AND [LeaseExpiresAt]>{now} AND [Attempts]={lease.Attempt}")
            .SingleOrDefaultAsync(cancellationToken);

    internal static async Task MarkTerminalAsync(BackOfficeDbContext db,OutboxWork job,string code,DateTimeOffset now,CancellationToken cancellationToken)
    {
        job.State="failed"; job.CompletedAt=now; job.ErrorCode=code; job.LeaseToken=null; job.LeaseExpiresAt=null;
        if (job.Kind == "finance-bordereau-submit")
        {
            var submission = await db.Set<FinanceBordereauSubmission>().SingleOrDefaultAsync(x => x.WorkId == job.Id, cancellationToken);
            if (submission is { State: not ("submitted" or "rejected") }) submission.State = "failed";
        }
        if(job.Kind=="operational-claims")
        {
            var request=await db.Set<ClaimsRequest>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==job.Id,cancellationToken);
            if(request is{Purpose:"handoff"})
            {
                var handoff=await db.Set<ClaimsHandoff>().SingleAsync(x=>x.Id==request.HandoffId,cancellationToken);
                if(handoff.State=="queued")
                {
                    handoff.State=code=="provider-rejected"?"rejected":code=="claims-context-unavailable"?"superseded":"failed";handoff.OutcomeCode=code;handoff.CompletedAt=now;
                    var incident=await db.Set<OperationalIncident>().SingleAsync(x=>x.Id==handoff.IncidentId,cancellationToken);
                    if(incident.CurrentRevisionId==handoff.RevisionId){incident.State="failed";incident.UpdatedAt=now;}
                }
            }
        }
        if (job.Kind == "operational-delivery")
        {
            var delivery = await db.Set<OperationalDelivery>().SingleOrDefaultAsync(x => x.WorkId == job.Id, cancellationToken);
            if (delivery is { State: "queued" })
            {
                delivery.State = code == "delivery-context-unavailable" ? "superseded" : "failed"; delivery.CompletedAt = now; delivery.OutcomeCode = code;
                await Operations.DeliverySnapshots.UpdateMessageState(db, delivery, cancellationToken);
            }
        }
        if (job.Kind == "quote-delivery")
        {
            var delivery = await db.Set<QuoteTermsDelivery>().SingleOrDefaultAsync(x => x.WorkId == job.Id, cancellationToken);
            if (delivery is { State: "queued" }) { delivery.State = "failed"; delivery.CompletedAt = now; delivery.OutcomeCode = code; }
        }
        if(job.Kind=="servicing-delivery")
        {
            var delivery=await db.Set<ServicingTermsDelivery>().SingleOrDefaultAsync(x=>x.WorkId==job.Id,cancellationToken);
            if(delivery is {State:"queued"})
            {
                delivery.State="failed";delivery.CompletedAt=now;delivery.OutcomeCode=code;delivery.UpdatedAt=now;
                delivery.AttemptId=await db.Set<AdapterAttempt>().Where(x=>x.WorkId==job.Id).OrderByDescending(x=>x.AttemptNumber).Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
            }
        }
        if (job.Kind == "capacity-escalation")
        {
            var submission = await db.Set<CapacitySubmission>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == job.Id, cancellationToken);
            if (submission is not null)
            {
                var escalation = await db.Set<CapacityEscalation>().SingleAsync(x => x.Id == submission.EscalationId, cancellationToken);
                if (escalation.CurrentSubmissionId == submission.Id && escalation.CurrentResponseId is null && escalation.State == "queued") { escalation.State = "failed"; escalation.UpdatedAt = now; }
            }
        }
        if (job.Kind == "quote-rating")
        {
            var cycle = await db.Set<UnderwritingCycle>().SingleOrDefaultAsync(x => x.WorkId == job.Id, cancellationToken);
            if (cycle is { State: "rating-pending" }) { cycle.State = "failed"; cycle.UpdatedAt = now; }
        }
        if (job.Kind == "servicing-rating")
        {
            var cycle = await db.Set<ServicingCycle>().SingleOrDefaultAsync(x => x.WorkId == job.Id, cancellationToken);
            if (cycle is { State: "rating-pending" }) { cycle.State = "failed"; cycle.UpdatedAt = now; }
        }
        if (job.Kind == "quote-lookup")
        {
            // Final lease expiry and explicit failures must complete the private
            // outcome in the same transaction as the queue entry.
            var lookup = await db.Set<QuoteLookup>().SingleOrDefaultAsync(x => x.WorkId == job.Id, cancellationToken);
            if (lookup is { State: "pending" })
            {
                lookup.State = "failed"; lookup.CompletedAt = now;
                lookup.ResultJson = JsonSerializer.Serialize(new { state = "failed", code, candidates = Array.Empty<object>() });
            }
        }
        if (!await db.Set<JobException>().AnyAsync(x => x.WorkId==job.Id,cancellationToken))
            db.Add(new JobException {WorkId=job.Id,Code=code,OccurredAt=now});
    }
}
