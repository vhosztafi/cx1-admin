using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed class CancellationOperationsService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public async Task<object[]> List(ActorContext actor, Guid versionId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var version = await db.Set<PolicyVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == versionId, token) ?? throw new OperationalAccessException(404, "policy-version-not-found");
        await OperationalScope.HoldParents(db, actor, [new("policy", version.PolicyId)], "document-read", token);
        var rows = await db.Set<CancellationConsequence>().AsNoTracking().Where(x => x.VersionId == versionId).OrderBy(x => x.Kind).ToArrayAsync(token);
        var results = new List<object>();
        foreach (var row in rows)
        {
            var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == row.WorkId, token);
            var receipt = await db.Set<CancellationOperationalReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.ConsequenceId == row.Id, token);
            var legacy = await db.Set<CancellationNoticeReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.ConsequenceId == row.Id, token);
            var dispatch = await db.Set<CancellationNoticeDispatch>().AsNoTracking().SingleOrDefaultAsync(x => x.ConsequenceId == row.Id, token);
            var delivery = dispatch is null ? null : await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync(x => x.Id == dispatch.DeliveryId, token);
            var documentId = await db.Set<DocumentVersion>().Where(x => x.CancellationConsequenceId == row.Id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(token);
            var closed = await db.Set<CancellationTaskClosure>().Where(x => x.ConsequenceId == row.Id).OrderBy(x => x.TaskId).Select(x => x.TaskId).ToArrayAsync(token);
            var mid = await db.Set<MidSubmission>().Where(x => x.CancellationConsequenceId == row.Id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(token);
            var effectiveAt = await db.Set<CancellationIssueDecision>().Where(x => x.Id == row.DecisionId).Select(x => x.EffectiveAt).SingleAsync(token);
            var failureWork=delivery?.WorkId??work.Id;
            var exceptionTaskId=await(from exception in db.Set<JobException>() join binding in db.Set<WorkflowTaskBinding>() on exception.Id equals binding.JobExceptionId
                where exception.WorkId==failureWork select (Guid?)binding.TaskId).FirstOrDefaultAsync(token);
            results.Add(new { row.Id, row.Kind, policyVersionId = versionId, effectiveAt, appliedAt = receipt?.CreatedAt,
                state = legacy is not null ? "legacy-" + legacy.Outcome : delivery is not null ? "delivery-" + delivery.State : receipt?.Outcome ??
                    (work.State == "failed" ? "failed" : row.Kind != "notice" && time.GetUtcNow() < effectiveAt ? "scheduled" : work.State),
                jobId = work.Id, work.ErrorCode, documentVersionId = documentId, deliveryId = delivery?.Id, legacyReceiptId = legacy?.Id, midSubmissionId = mid, closedTaskIds = closed,exceptionTaskId });
        }
        await tx.CommitAsync(token); return results.ToArray();
    }
}
