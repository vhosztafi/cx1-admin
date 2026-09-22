using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class TaskService(IDbContextFactory<BackOfficeDbContext> factory, SqlCommandBoundary commands, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public async Task<CommandOutcome> Read(ActorContext actor, Guid taskId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var row = await HoldRead(db, actor, taskId, token);
        var result = await Outcome(db, row, 200, token); await transaction.CommitAsync(token); return result;
    }

    public static async Task<OperationalTask> HoldRead(BackOfficeDbContext db, ActorContext actor, Guid taskId, CancellationToken token)
    {
        var subjectId = await db.Set<OperationalTask>().AsNoTracking().Where(x => x.Id == taskId).Select(x => (Guid?)x.SubjectId).SingleOrDefaultAsync(token)
            ?? throw new OperationalAccessException(404, "task-not-found");
        await OperationalScope.HoldSubjects(db, actor, [subjectId], "task-read", token);
        var row = await db.Set<OperationalTask>().FromSqlInterpolated($"SELECT * FROM OperationalTask WITH(HOLDLOCK,ROWLOCK) WHERE Id={taskId}").AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw new OperationalAccessException(404, "task-not-found");
        if (row.SubjectId != subjectId) throw new OperationalAccessException(404, "task-not-found");
        return row;
    }

    public async Task<CommandOutcome> Register(ActorContext actor, OperationalParent parent, string key, CancellationToken token)
    {
        HeldOperationalScope? held = null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId, "/api/v1/operational-subjects", key, Guid.NewGuid()), parent, "operational.subject-registered",
            async (db, ct) => held = await OperationalScope.HoldParents(db, actor, [parent], "subject-read", ct),
            async (db, ct) =>
            {
                // Different request keys still register one parent. This lock is acquired
                // after the already held parent graph, never before parent authorization.
                await using var command = db.Database.GetDbConnection().CreateCommand(); command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
                command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; SELECT @result;";
                var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = $"CoverMGA.Subject.{parent.Kind}.{parent.ParentId:N}"; command.Parameters.Add(parameter);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0) throw new CommandBusyException();
                var query = db.Set<OperationalSubject>().Where(x => x.Kind == parent.Kind);
                query = parent.Kind switch
                {
                    "agency" => query.Where(x => x.AgencyId == parent.ParentId), "relationship" => query.Where(x => x.RelationshipId == parent.ParentId),
                    "quote" => query.Where(x => x.QuoteId == parent.ParentId), "policy" => query.Where(x => x.PolicyId == parent.ParentId),
                    "servicing-draft" => query.Where(x => x.ServicingDraftId == parent.ParentId), _ => throw new OperationalAccessException(404, "operational-subject-not-found")
                };
                var subject = await query.SingleOrDefaultAsync(ct);
                if (subject is null)
                {
                    subject = new OperationalSubject { Kind = parent.Kind, CreatedBy = held!.Actor.UserId, CreatedAt = time.GetUtcNow(),
                        AgencyId = parent.Kind == "agency" ? parent.ParentId : null, RelationshipId = parent.Kind == "relationship" ? parent.ParentId : null,
                        QuoteId = parent.Kind == "quote" ? parent.ParentId : null, PolicyId = parent.Kind == "policy" ? parent.ParentId : null,
                        ServicingDraftId = parent.Kind == "servicing-draft" ? parent.ParentId : null };
                    db.Add(subject); await db.SaveChangesAsync(ct);
                }
                var label = parent.Kind switch
                {
                    "agency" => await db.Set<Agency>().Where(x => x.Id == parent.ParentId).Select(x => x.Reference).SingleAsync(ct),
                    "relationship" => await (from r in db.Set<ClientAgencyRelationship>() join c in db.Set<ClientAccount>() on r.ClientId equals c.Id where r.Id == parent.ParentId select c.Reference).SingleAsync(ct),
                    "quote" => await db.Set<Quote>().Where(x => x.Id == parent.ParentId).Select(x => x.Reference).SingleAsync(ct),
                    "policy" => await db.Set<Policy>().Where(x => x.Id == parent.ParentId).Select(x => x.Reference).SingleAsync(ct),
                    _ => "Servicing draft"
                };
                var href = parent.Kind == "relationship"
                    ? "/clients/" + await db.Set<ClientAgencyRelationship>().Where(x => x.Id == parent.ParentId).Select(x => x.ClientId).SingleAsync(ct)
                    : SubjectHref(parent);
                return new(subject.Id, 201, Serialize(new { id = subject.Id, kind = parent.Kind, parentId = parent.ParentId, label, href }));
            }, token);
    }

    public async Task<CommandOutcome> Create(ActorContext actor, Guid subjectId, TaskWrite input, string key, CancellationToken token)
    {
        TaskRules.ValidateWrite(input); HeldOperationalScope? held = null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId, "/api/v1/tasks", key, Guid.NewGuid()), new { subjectId, input }, "task.created",
            async (db, ct) =>
            {
                held = await OperationalScope.HoldSubjects(db, actor, [subjectId], "task-write", ct);
                if (input.Assignment.Kind != "unassigned" && !held.Actor.HasCapability("task-assign")) throw new OperationalAccessException(403, "task-assignment-denied");
                await Assignment(db, input.Assignment, held.Subjects, ct);
            }, async (db, ct) =>
            {
                var row = await Insert(db, held!, subjectId, input, "open", [], ct);
                return await Outcome(db, row, 201, ct);
            }, token);
    }

    // Shared atomic creation primitive. The caller owns the transaction through
    // its task binding/audit/receipt; no nested command boundary is opened here.
    internal async Task<OperationalTask> Insert(BackOfficeDbContext db, HeldOperationalScope held, Guid subjectId,
        TaskWrite input, string initialState, IReadOnlyList<WorkflowChecklistDefinition> checklist, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null || !held.Actor.HasCapability("task-write") || !held.Subjects.Any(x => x.Id == subjectId))
            throw new InvalidOperationException("Task insertion requires a held authorized subject.");
        TaskRules.ValidateWrite(input);
        if (initialState is not ("open" or "awaiting-information")) throw new TaskRuleException("invalid-initial-task-state");
        if (input.Assignment.Kind != "unassigned" && !held.Actor.HasCapability("task-assign")) throw new OperationalAccessException(403, "task-assignment-denied");
        await Assignment(db, input.Assignment, held.Subjects, token);
        var now = time.GetUtcNow();
        var row = new OperationalTask { SubjectId = subjectId, TypeCode = input.TypeCode, Title = input.Title, Priority = input.Priority,
            State = initialState, OwnerId = input.Assignment.OwnerId, TeamId = input.Assignment.TeamId, DueOn = input.DueOn,
            CreatedBy = held.Actor.UserId, CreatedAt = now, UpdatedAt = now, EventSequence = 1 };
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction.GetDbTransaction(); command.CommandText = "SELECT NEXT VALUE FOR TaskReferenceSequence";
        row.Reference = "TSK-" + Convert.ToInt64(await command.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture).ToString("D7", System.Globalization.CultureInfo.InvariantCulture);
        db.Add(row);
        foreach (var (item, index) in checklist.Select((item, index) => (item, index)))
        {
            TaskRules.RequireText(item.Label, 300, "invalid-workflow-checklist");
            db.Add(new OperationalTaskChecklist { TaskId = row.Id, Ordinal = index, Label = item.Label, Required = item.Required, CreatedBy = held.Actor.UserId, CreatedAt = now, UpdatedAt = now });
        }
        // Persist within the same transaction so the first event captures the
        // entire initial checklist, including unsatisfied required items.
        await db.SaveChangesAsync(token);
        await AddEvent(db, row, held, "created", null, token); await db.SaveChangesAsync(token);
        return row;
    }

    public Task<CommandOutcome> Transition(ActorContext actor, Guid taskId, string etag, string key, string state, string reason, CancellationToken token)
        => Mutate(actor, [new(taskId, etag)], key, $"/api/v1/tasks/{taskId}/transition", "task.transitioned", new { state, reason }, "task-write", null,
            async (db, row, ct) => { TaskRules.ValidateTransition(row.State, state, reason, await ChecklistState(db, row.Id, ct)); row.State = state; row.CompletionReason = TaskRules.IsTerminal(state) ? reason : null; }, reason, false, token);

    public Task<CommandOutcome> BulkComplete(ActorContext actor, IReadOnlyList<TaskSelection> tasks, string key, string reason, CancellationToken token)
        => Mutate(actor, tasks, key, "/api/v1/tasks/bulk-completion", "task.bulk-completed", new { reason }, "task-write", null,
            async (db, row, ct) => { TaskRules.ValidateTransition(row.State, "completed", reason, await ChecklistState(db, row.Id, ct)); row.State = "completed"; row.CompletionReason = reason; }, reason, true, token);

    private async Task<CommandOutcome> Mutate<T>(ActorContext actor, IReadOnlyList<TaskSelection> tasks, string key, string route, string eventType, T input, string capability,
        TaskAssignment? assignment, Func<BackOfficeDbContext, OperationalTask, CancellationToken, Task> change, string? reason, bool bulk, CancellationToken token,
        Func<BackOfficeDbContext, OperationalTask, CancellationToken, Task<CommandOutcome>>? project = null,
        Func<BackOfficeDbContext, IReadOnlyList<OperationalSubject>, CancellationToken, Task>? authorize = null)
    {
        TaskRules.ValidateSelection(tasks); if (reason is not null) TaskRules.RequireText(reason, 1000, "task-reason-required");
        HeldOperationalScope? held = null; Dictionary<Guid, Guid>? subjects = null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId, route, key, Guid.NewGuid()), new { tasks, input }, eventType,
            async (db, ct) =>
            {
                var ids = tasks.Select(x => x.Id).ToArray();
                subjects = await db.Set<OperationalTask>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.SubjectId, ct);
                if (subjects.Count != ids.Length) throw new OperationalAccessException(404, "task-not-found");
                held = await OperationalScope.HoldSubjects(db, actor, subjects.Values.Distinct().ToArray(), capability, ct);
                if (assignment is not null)
                {
                    if (!held.Actor.HasCapability("task-assign")) throw new OperationalAccessException(403, "task-assignment-denied");
                    await Assignment(db, assignment, held.Subjects, ct);
                }
                if (authorize is not null) await authorize(db, held.Subjects, ct);
            }, async (db, ct) =>
            {
                var rows = new List<OperationalTask>();
                foreach (var selection in tasks.OrderBy(x => x.Id))
                {
                    var row = await db.Set<OperationalTask>().FromSqlInterpolated($"SELECT * FROM OperationalTask WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={selection.Id}").SingleOrDefaultAsync(ct)
                        ?? throw new OperationalAccessException(404, "task-not-found");
                    if (row.SubjectId != subjects![row.Id]) throw new OperationalAccessException(404, "task-not-found");
                    if (Etag(row.RowVersion) != selection.Etag) throw new OperationalAccessException(412, "stale-task");
                    rows.Add(row);
                }
                // All heads/versions were checked before any effect. Later rule failure
                // still rolls back the entire command, including events and receipt.
                foreach (var row in rows)
                {
                    await change(db, row, ct); row.EventSequence++;
                    await AddEvent(db, row, held!, eventType, reason, ct);
                }
                await db.SaveChangesAsync(ct);
                if (project is not null) return await project(db, rows[0], ct);
                return bulk ? new(rows[0].Id, 200, Serialize(new { updatedIds = tasks.Select(x => x.Id).ToArray() })) : await Outcome(db, rows[0], 200, ct);
            }, token);
    }

    private static async Task Assignment(BackOfficeDbContext db, TaskAssignment assignment, IReadOnlyList<OperationalSubject> subjects, CancellationToken token)
    {
        TaskRules.ValidateAssignment(assignment);
        if (assignment.Kind == "unassigned") return;
        var ids = assignment.OwnerId is Guid id ? new[] { id } : await db.Set<StaffUser>().Where(x => x.TeamId == assignment.TeamId && x.AgencyId == null && x.State == "active").Select(x => x.Id).ToArrayAsync(token);
        if (assignment.TeamId is Guid teamId && !await db.Set<Team>().FromSqlInterpolated($"SELECT * FROM Team WITH(HOLDLOCK,ROWLOCK) WHERE Id={teamId}").AsNoTracking().AnyAsync(token)) throw InvalidAssignee();
        foreach (var userId in ids.Order())
        {
            var identity = await IdentitySnapshot.Lock(db, new(userId, null), token);
            if (identity is null || assignment.TeamId is Guid expectedTeam && identity.User.TeamId != expectedTeam) continue;
            var assignee = new ActorContext(userId, identity.User.TeamId, null, identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal));
            try { await OperationalScope.HoldSubjects(db, assignee, subjects.Select(x => x.Id).ToArray(), "task-read", token); return; }
            catch (OperationalAccessException) { }
            catch (BackOffice.Infrastructure.Quotes.QuoteOperationException) { }
        }
        throw InvalidAssignee();
    }

    private async Task AddEvent(BackOfficeDbContext db, OperationalTask row, HeldOperationalScope held, string kind, string? reason, CancellationToken token)
    {
        // Tracking preserves checklist changes pending in this same command.
        var checklist = await db.Set<OperationalTaskChecklist>().Where(x => x.TaskId == row.Id).OrderBy(x => x.Ordinal).ToArrayAsync(token);
        db.Add(new OperationalTaskEvent { TaskId = row.Id, Sequence = row.EventSequence, Kind = kind, Reason = reason, ActorLabel = held.ActorLabel,
            CreatedBy = held.Actor.UserId, CreatedAt = time.GetUtcNow(), SnapshotJson = Serialize(new { row.TypeCode, row.Title, row.Priority, row.State, row.OwnerId, row.TeamId, row.DueOn, row.CompletionReason,
                checklist = checklist.Select(x => new { x.Id, x.Label, x.Required, x.Completed }) }) });
    }

    private static async Task<TaskChecklistState[]> ChecklistState(BackOfficeDbContext db, Guid id, CancellationToken token)
        => await db.Set<OperationalTaskChecklist>().Where(x => x.TaskId == id).Select(x => new TaskChecklistState(x.Id, x.Required, x.Completed)).ToArrayAsync(token);

    private async Task<CommandOutcome> Outcome(BackOfficeDbContext db, OperationalTask row, int status, CancellationToken token)
    {
        var view = (await Views(db, [row], token))[0];
        return new(row.Id, status, view.GetRawText(), Etag: Etag(row.RowVersion));
    }

    public async Task<JsonElement[]> Views(BackOfficeDbContext db, IReadOnlyList<OperationalTask> rows, CancellationToken token)
    {
        var ids = rows.Select(x => x.Id).ToArray();
        var checklist = await db.Set<OperationalTaskChecklist>().AsNoTracking().Where(x => ids.Contains(x.TaskId)).OrderBy(x => x.Ordinal).ToArrayAsync(token);
        var presentation = await TaskPresentation.Load(db, rows, token);
        var workflows = await WorkflowTaskProvenanceReader.Read(db, ids, time.GetUtcNow(), token);
        return rows.Select(row => JsonSerializer.SerializeToElement(View(row, checklist.Where(x => x.TaskId == row.Id).Select(x => new { x.Id, x.Label, x.Required, x.Completed }).ToArray(), presentation, workflows.GetValueOrDefault(row.Id)), Json)).ToArray();
    }

    private object View(OperationalTask row, object checklist, TaskPresentation presentation, WorkflowTaskProvenance? workflow)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
        return new { row.Id, row.Reference, subjectRecordId = row.SubjectId, subject = presentation.Subjects[row.SubjectId],
            assignmentLabel = row.OwnerId is Guid owner ? presentation.Users[owner] : row.TeamId is Guid team ? presentation.Teams[team] : "Unassigned",
            createdByLabel = presentation.Users[row.CreatedBy!.Value], row.TypeCode, row.Title, row.Priority, etag = Etag(row.RowVersion),
            assignment = new TaskAssignment(row.OwnerId is not null ? "user" : row.TeamId is not null ? "team" : "unassigned", row.OwnerId, row.TeamId), row.DueOn, row.State,
            row.CreatedBy, row.CreatedAt, row.UpdatedAt, overdue = TaskRules.IsOverdue(row.State, row.DueOn, today), checklist, row.CompletionReason,
            sourceChanged = row.SourceChanged || workflow?.SourceChanged == true, workflow };
    }
    private static string SubjectHref(OperationalParent parent) => parent.Kind switch
    {
        "agency" => $"/agents/{parent.ParentId}", "quote" => $"/quotes/{parent.ParentId}", "policy" => $"/policies/{parent.ParentId}",
        _ => $"/drafts/{parent.ParentId}"
    };
    public static string Etag(byte[] rowVersion) => "\"" + Convert.ToBase64String(rowVersion) + "\"";
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static OperationalAccessException InvalidAssignee() => new(422, "task-assignee-unavailable");
}
