using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed record TaskAssigneeChoice(Guid Id, string Label);
public sealed record TaskAssigneeChoices(IReadOnlyList<TaskAssigneeChoice> Items, bool HasMore);
public sealed partial class TaskService
{
    public static async Task<TaskAssigneeChoices> Assignees(BackOfficeDbContext db, HeldOperationalScope held, string kind, string search, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null || !held.Actor.HasCapability("task-assign")) throw new OperationalAccessException(403, "task-assignment-denied");
        var candidates = kind == "user"
            ? await db.Set<StaffUser>().AsNoTracking().Where(x => x.State == "active" && x.AgencyId == null && x.DisplayName.Contains(search)).OrderBy(x => x.Id).Select(x => new TaskAssigneeChoice(x.Id, x.DisplayName)).ToArrayAsync(token)
            : await db.Set<Team>().AsNoTracking().Where(x => x.Name.Contains(search)).OrderBy(x => x.Id).Select(x => new TaskAssigneeChoice(x.Id, x.Name)).ToArrayAsync(token);
        var eligible = new List<TaskAssigneeChoice>();
        // Use the same current identity/parent checks as assignment commands.
        // No candidate names or counts are returned before scope is checked.
        foreach (var candidate in candidates)
        {
            try
            {
                await Assignment(db, kind == "user" ? new TaskAssignment(kind, candidate.Id) : new TaskAssignment(kind, TeamId: candidate.Id), held.Subjects, token);
                eligible.Add(candidate);
            }
            catch (OperationalAccessException error) when (error.Code == "task-assignee-unavailable") { }
        }
        return new(eligible.OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id).Take(50).ToArray(), eligible.Count > 50);
    }
}
