using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class CancellationOperationsWorker(IDbContextFactory<BackOfficeDbContext> factory, TaskService tasks, TimeProvider time)
{
    public async Task<bool> Apply(JobLease lease, CancellationToken token = default)
    {
        if (lease.Kind is not ("cancellation-certificate-withdrawal" or "cancellation-task-close")) throw CancellationOperationsAuthority.Invalid();
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var source = await CancellationOperationsAuthority.Hold(db, lease.WorkId, token);
        var subject = await db.Set<OperationalSubject>().AsNoTracking().SingleOrDefaultAsync(x => x.PolicyId == source.Policy.Id, token);
        var held = subject is null ? null : await OperationalScope.HoldSubjects(db, source.Actor, [subject.Id], "task-write", token);
        // Task heads precede the original outbox fence, matching task mutations.
        // Closure reuses these transaction-held locks after lease validation.
        if(source.Consequence.Kind=="task-close" && held is not null)
        {
            var taskIds=await db.Set<WorkflowTaskBinding>().AsNoTracking().Where(x=>x.SubjectId==subject!.Id&&x.SourceKind=="policy-term"&&x.PolicyTermId==source.Consequence.TermId)
                .Select(x=>x.TaskId).Distinct().Order().ToArrayAsync(token);
            foreach(var id in taskIds)await db.Set<OperationalTask>().FromSqlInterpolated($"SELECT * FROM OperationalTask WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={id}").SingleAsync(token);
        }
        var now = time.GetUtcNow();
        var work = await SqlJobLeases.OwnedAsync(db, lease, now, token);
        if (work is null) return false;
        if (!CancellationOperationsAuthority.Matches(source, work, lease)) throw CancellationOperationsAuthority.Invalid();
        if (!CancellationOperationsRules.CanApply(source.Consequence.Kind, source.Decision.EffectiveAt, now)) return false;
        var prior = await db.Set<CancellationOperationalReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.ConsequenceId == source.Consequence.Id, token);
        if (prior is not null) throw CancellationOperationsAuthority.Invalid();
        Guid[] closed = [];
        if (source.Consequence.Kind == "certificate-withdrawal")
            db.Add(new CertificateWithdrawal { ConsequenceId = source.Consequence.Id, TermId = source.Consequence.TermId, CertificateKind = "policy-certificate", EffectiveAt = source.Decision.EffectiveAt, CreatedBy = source.Actor.UserId, CreatedAt = now });
        else if (held is not null) closed = await tasks.CloseCancelledTerm(db, held, source.Consequence, token);
        var result = JsonSerializer.Serialize(new { consequenceId = source.Consequence.Id, effectiveAt = source.Decision.EffectiveAt, closedTaskIds = closed }, CommunicationScope.Json);
        db.Add(new CancellationOperationalReceipt { ConsequenceId = source.Consequence.Id, WorkId = work.Id, EffectiveAt = source.Decision.EffectiveAt,
            Outcome = source.Consequence.Kind == "certificate-withdrawal" ? "certificate-withdrawn" : "eligible-tasks-closed", PayloadHash = Convert.ToHexStringLower(source.Consequence.PayloadHash), ResultJson = result, CreatedBy = source.Actor.UserId, CreatedAt = now });
        var attempt = await db.Set<AdapterAttempt>().SingleAsync(x => x.WorkId == work.Id && x.AttemptNumber == lease.Attempt, token);
        attempt.EndedAt = now; attempt.Outcome = "succeeded"; attempt.Response = result;
        work.State = "succeeded"; work.Result = result; work.CompletedAt = now; work.LeaseToken = null; work.LeaseExpiresAt = null; work.ErrorCode = null;
        db.Add(new AuditEvent { SubjectRecordId = source.Policy.Id, EventType = "policy.cancellation-" + source.Consequence.Kind + "-completed", ActorId = source.Actor.UserId,
            CreatedBy = source.Actor.UserId, CreatedAt = now, OccurredAt = now, CorrelationId = work.CorrelationId, After = result });
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return true;
    }
}
