using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class TaskService
{
    internal async Task<Guid[]> CloseCancelledTerm(BackOfficeDbContext db, HeldOperationalScope held, CancellationConsequence consequence, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null || !held.Actor.HasCapability("task-write")) throw CancellationOperationsAuthority.Invalid();
        var subject = held.Subjects.Single(x => x.PolicyId == consequence.PolicyId);
        var bindings = await db.Set<WorkflowTaskBinding>().AsNoTracking().Where(x => x.SubjectId == subject.Id && x.SourceKind == "policy-term" && x.PolicyTermId == consequence.TermId).OrderBy(x => x.TaskId).ToArrayAsync(token);
        var closed = new List<Guid>();
        foreach (var binding in bindings)
        {
            var row = await db.Set<OperationalTask>().FromSqlInterpolated($"SELECT * FROM OperationalTask WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={binding.TaskId}").SingleAsync(token);
            if (row.SubjectId != subject.Id || !CancellationOperationsRules.CanCloseRenewal(consequence.TermId, binding.PolicyTermId, binding.SourceKind, row.TypeCode, row.State, row.SourceChanged)) continue;
            row.State = "cancelled"; row.CompletionReason = "Policy term cancelled; renewal task no longer applies."; row.EventSequence++;
            await AddEvent(db, row, held, "task.cancelled-by-policy", row.CompletionReason, token);
            var taskEvent = db.ChangeTracker.Entries<OperationalTaskEvent>().Single(x => x.Entity.TaskId == row.Id && x.Entity.Sequence == row.EventSequence).Entity;
            db.Add(new CancellationTaskClosure { ConsequenceId = consequence.Id, TaskId = row.Id, TaskEventId = taskEvent.Id, CreatedBy = held.Actor.UserId, CreatedAt = time.GetUtcNow() });
            closed.Add(row.Id);
        }
        return closed.ToArray();
    }
}
