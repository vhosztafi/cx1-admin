using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class TaskService
{
    public Task<CommandOutcome> Update(ActorContext actor, Guid taskId, string etag, string key, TaskWrite input, CancellationToken token)
    {
        TaskRules.ValidateWrite(input);
        return Mutate(actor, [new(taskId, etag)], key, $"/api/v1/tasks/{taskId}", "task.updated", input, "task-write", input.Assignment,
            (db, row, ct) =>
            {
                Editable(row); row.TypeCode = input.TypeCode; row.Title = input.Title; row.Priority = input.Priority;
                row.OwnerId = input.Assignment.OwnerId; row.TeamId = input.Assignment.TeamId; row.DueOn = input.DueOn; return Task.CompletedTask;
            }, null, false, token);
    }

    public Task<CommandOutcome> BulkAssign(ActorContext actor, IReadOnlyList<TaskSelection> tasks, string key, TaskAssignment assignment, string reason, CancellationToken token)
    {
        TaskRules.ValidateAssignment(assignment);
        return Mutate(actor, tasks, key, "/api/v1/tasks/bulk-assignment", "task.bulk-assigned", new { assignment, reason }, "task-assign", assignment,
            (db, row, ct) => { Editable(row); row.OwnerId = assignment.OwnerId; row.TeamId = assignment.TeamId; return Task.CompletedTask; }, reason, true, token);
    }

    public Task<CommandOutcome> BulkDue(ActorContext actor, IReadOnlyList<TaskSelection> tasks, string key, DateOnly dueOn, string reason, CancellationToken token)
        => Mutate(actor, tasks, key, "/api/v1/tasks/bulk-due-date", "task.bulk-due-changed", new { dueOn, reason }, "task-assign", null,
            (db, row, ct) => { Editable(row); row.DueOn = dueOn; return Task.CompletedTask; }, reason, true, token);

    public Task<CommandOutcome> Checklist(ActorContext actor, Guid taskId, string etag, string key, IReadOnlyList<TaskChecklistChange> items, string reason, CancellationToken token)
        => Mutate(actor, [new(taskId, etag)], key, $"/api/v1/tasks/{taskId}/checklist", "task.checklist-changed", new { items, reason }, "task-write", null,
            async (db, row, ct) =>
            {
                Editable(row);
                var current = await db.Set<OperationalTaskChecklist>().Where(x => x.TaskId == row.Id).ToArrayAsync(ct);
                TaskRules.ValidateChecklist(current.Select(x => new TaskChecklistState(x.Id, x.Required, x.Completed)).ToArray(), items);
                foreach (var change in items) current.Single(x => x.Id == change.Id).Completed = change.Completed;
            }, reason, false, token);

    public Task<CommandOutcome> Comment(ActorContext actor, Guid taskId, string etag, string key, string body, CancellationToken token)
    {
        TaskRules.RequireText(body, 8000, "task-comment-required");
        OperationalTaskComment? comment = null;
        return Mutate(actor, [new(taskId, etag)], key, $"/api/v1/tasks/{taskId}/comments", "task.comment-added", new { body }, "task-write", null,
            async (db, row, ct) =>
            {
                var label = await db.Set<StaffUser>().Where(x => x.Id == actor.UserId).Select(x => x.DisplayName).SingleAsync(ct);
                comment = new OperationalTaskComment { TaskId = row.Id, Body = body, AuthorLabel = label, CreatedBy = actor.UserId, CreatedAt = time.GetUtcNow() }; db.Add(comment);
            }, null, false, token, (db, row, ct) => Task.FromResult(new CommandOutcome(comment!.Id, 201,
                Serialize(new { comment.Id, comment.TaskId, comment.Body, comment.AuthorLabel, comment.CreatedAt }), Etag: Etag(row.RowVersion))));
    }

    private static void Editable(OperationalTask task)
    {
        if (TaskRules.IsTerminal(task.State)) throw new OperationalAccessException(409, "task-reopen-required");
    }
}
