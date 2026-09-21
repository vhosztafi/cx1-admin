using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed record WorkflowTaskProvenance(string RuleCode, Guid RuleVersionId, int RuleVersion, string RuleTitle,
    string SourceKind, Guid SourceEventId, DateTimeOffset CreatedAt, string SourceCondition, bool SourceChanged);

internal static class WorkflowTaskProvenanceReader
{
    // Call only after the task's operational parent has been authorized. Source
    // changes are read separately; they do not mutate the human task or its ETag.
    internal static async Task<Dictionary<Guid, WorkflowTaskProvenance>> Read(BackOfficeDbContext db, Guid[] taskIds, DateTimeOffset now, CancellationToken token)
    {
        var rows = await (from binding in db.Set<WorkflowTaskBinding>().AsNoTracking()
                          join version in db.Set<SettingVersion>().AsNoTracking() on binding.RuleVersionId equals version.Id
                          join subject in db.Set<OperationalSubject>().AsNoTracking() on binding.SubjectId equals subject.Id
                          where taskIds.Contains(binding.TaskId) select new { Binding = binding, Version = version, Subject = subject }).ToListAsync(token);
        var result = new Dictionary<Guid, WorkflowTaskProvenance>();
        foreach (var row in rows)
        {
            var binding = row.Binding;
            var definition = WorkflowTaskRules.Parse(binding.RuleSnapshotJson, row.Version.Scope);
            var condition = "unavailable"; var changed = true;
            try
            {
                var source = await WorkflowTaskSources.Read(db, definition, binding.SourceKind, binding.SourceEventId, now, token);
                if (source.Parent == OperationalScope.Parent(row.Subject))
                {
                    condition = source.Resolved ? "resolved" : source.Eligible ? "outstanding" : "not-due";
                    changed = WorkflowTaskService.Hash(source.SnapshotJson) != binding.SourceHash;
                }
            }
            catch (OperationalAccessException) { /* Retain first provenance; current source is unavailable. */ }
            result.Add(binding.TaskId, new(binding.RuleCode, binding.RuleVersionId, row.Version.Version, definition.Title,
                binding.SourceKind, binding.SourceEventId, binding.CreatedAt, condition, changed));
        }
        return result;
    }
}
