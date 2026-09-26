using System.Data;
using System.Security.Cryptography;
using System.Text;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Operations;

public sealed class WorkflowTaskService(IDbContextFactory<BackOfficeDbContext> factory, TaskService tasks, TimeProvider time)
{
    public async Task<Guid?> Reconcile(Guid ruleVersionId, string sourceKind, Guid sourceEventId, CancellationToken token)
    {
        // Hints never authorize effects. Resolve current roles and then hold the
        // normal operational parent/identity graph again inside the transaction.
        await using var hints = await factory.CreateDbContextAsync(token);
        var initial = await Rule(hints, ruleVersionId, time.GetUtcNow(), token);
        var sourceHint = await WorkflowTaskSources.Read(hints, initial.Definition, sourceKind, sourceEventId, time.GetUtcNow(), token);
        var user = await hints.Set<StaffUser>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceHint.ActorId && x.State == "active" && x.AgencyId == null, token)
            ?? throw new OperationalAccessException(403, "workflow-source-actor-unavailable");
        var roles = await (from link in hints.Set<UserRole>() join role in hints.Set<Role>() on link.RoleId equals role.Id where link.UserId == user.Id select role.Code).ToListAsync(token);
        var actor = new ActorContext(user.Id, user.TeamId, null, roles.ToHashSet(StringComparer.Ordinal));
        var registration = await tasks.Register(actor, sourceHint.Parent, $"workflow-subject/{sourceHint.Parent.Kind}/{sourceHint.Parent.ParentId:N}", token);

        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var held = await OperationalScope.HoldSubjects(db, actor, [registration.ResourceId], "task-write", token);
        var current = await Rule(db, ruleVersionId, time.GetUtcNow(), token);
        var operation = WorkflowTaskRules.OperationKey(current.Definition, sourceKind, sourceEventId);
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; SELECT @result;";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = "CoverMGA." + operation; command.Parameters.Add(parameter);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) < 0) throw new CommandBusyException();
        }
        var source = await WorkflowTaskSources.Read(db, current.Definition, sourceKind, sourceEventId, time.GetUtcNow(), token);
        if (source.Parent != sourceHint.Parent || source.ActorId != actor.UserId || OperationalScope.Parent(held.Subjects.Single()) != source.Parent)
            throw new OperationalAccessException(409, "workflow-source-changed");
        var existing = await db.Set<WorkflowTaskBinding>().AsNoTracking().SingleOrDefaultAsync(x => x.RuleCode == current.Definition.Code && x.SourceKind == sourceKind && x.SourceEventId == sourceEventId, token);
        if (existing is not null)
        {
            if (existing.SubjectId != registration.ResourceId) throw new OperationalAccessException(409, "workflow-source-changed");
            await transaction.CommitAsync(token); return existing.TaskId;
        }
        if (!source.Eligible) { await transaction.CommitAsync(token); return null; }
        var rule = current.Definition;
        var row = await tasks.Insert(db, held, registration.ResourceId, new(rule.TaskType, rule.Title, rule.Priority, rule.AssignmentTeamId is Guid team ? new("team", TeamId: team) : new("unassigned"), source.DueOn), rule.InitialState, rule.Checklist, token);
        var binding = new WorkflowTaskBinding
        {
            TaskId = row.Id, SubjectId = row.SubjectId, RuleVersionId = current.Version.Id, RuleCode = rule.Code,
            SourceKind = sourceKind, SourceEventId = sourceEventId, SourceSnapshotJson = source.SnapshotJson, SourceHash = Hash(source.SnapshotJson),
            RuleSnapshotJson = current.Version.Values, RuleHash = Hash(current.Version.Values), CreatedBy = held.Actor.UserId, CreatedAt = time.GetUtcNow(),
            QuoteReferralId = sourceKind == "quote-referral" ? sourceEventId : null,
            ServicingReferralId = sourceKind == "servicing-referral" ? sourceEventId : null,
            QuoteQueryDecisionId = sourceKind == "quote-query" ? sourceEventId : null,
            ServicingQueryDecisionId = sourceKind == "servicing-query" ? sourceEventId : null,
            MatchInformationRequestId = sourceKind == "match-information-request" ? sourceEventId : null,
            PolicyTermId = sourceKind == "policy-term" ? sourceEventId : null,
            AgencyFollowUpId = sourceKind == "agency-follow-up" ? sourceEventId : null,
            JobExceptionId = sourceKind == "job-exception" ? sourceEventId : null
        };
        db.Add(binding);
        db.Add(new AuditEvent { ActorId = actor.UserId, SubjectRecordId = row.Id, EventType = "task.workflow-created", OccurredAt = time.GetUtcNow(), CreatedBy = actor.UserId,
            After = System.Text.Json.JsonSerializer.Serialize(new { binding.RuleVersionId, binding.RuleCode, binding.SourceKind, binding.SourceEventId, binding.SourceHash, binding.RuleHash }), CorrelationId = Guid.NewGuid() });
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return row.Id;
    }

    private static async Task<(SettingVersion Version, WorkflowTaskDefinition Definition)> Rule(BackOfficeDbContext db, Guid id, DateTimeOffset now, CancellationToken token)
    {
        var version = await db.Set<SettingVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new OperationalAccessException(409, "workflow-rule-unavailable");
        var latest = await db.Set<SettingVersion>().Where(x => x.Scope == version.Scope && x.EffectiveFrom <= now)
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version).Select(x => x.Id).FirstOrDefaultAsync(token);
        if (latest != version.Id) throw new OperationalAccessException(409, "workflow-rule-stale");
        return (version, WorkflowTaskRules.Parse(version.Values, version.Scope));
    }
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
