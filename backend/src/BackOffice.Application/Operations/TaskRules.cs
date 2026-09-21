using System.Text.Json.Serialization;

namespace BackOffice.Application.Operations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaskAssignment([property: JsonRequired] string Kind, Guid? OwnerId = null, Guid? TeamId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaskWrite([property: JsonRequired] string TypeCode, [property: JsonRequired] string Title,
    [property: JsonRequired] string Priority, [property: JsonRequired] TaskAssignment Assignment, DateOnly? DueOn);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaskSelection([property: JsonRequired] Guid Id, [property: JsonRequired] string Etag);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaskChecklistChange([property: JsonRequired] Guid Id, [property: JsonRequired] bool Completed);
public sealed record TaskChecklistState(Guid Id, bool Required, bool Completed);
public sealed class TaskRuleException(string code) : Exception("The task command is invalid.")
{
    public string Code { get; } = code;
}

// Pure validation. Current identity, parent ownership, assignee eligibility and
// ETag comparison must be held in the caller's SQL transaction before application.
public static class TaskRules
{
    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal)
        { "servicing", "underwriting-referral", "authority-referral", "renewal", "data-exception", "agency-onboarding", "complaint", "underwriting" };
    public static readonly IReadOnlySet<string> States = new HashSet<string>(StringComparer.Ordinal)
        { "open", "in-progress", "awaiting-information", "blocked", "completed", "cancelled" };

    public static void ValidateWrite(TaskWrite input)
    {
        if (input is null || !Types.Contains(input.TypeCode) || input.Priority is not ("low" or "normal" or "high" or "urgent"))
            throw Invalid("invalid-task-fields");
        RequireText(input.Title, 300, "invalid-task-title");
        ValidateAssignment(input.Assignment);
    }

    public static void ValidateAssignment(TaskAssignment value)
    {
        if (value is null || !(value.Kind switch
        {
            "user" => value.OwnerId is Guid user && user != Guid.Empty && value.TeamId is null,
            "team" => value.TeamId is Guid team && team != Guid.Empty && value.OwnerId is null,
            "unassigned" => value.OwnerId is null && value.TeamId is null,
            _ => false
        })) throw Invalid("invalid-task-assignment");
    }

    public static void ValidateTransition(string before, string after, string reason, IReadOnlyList<TaskChecklistState> checklist)
    {
        RequireText(reason, 1000, "task-reason-required");
        if (!States.Contains(before) || !States.Contains(after) || before == after || IsTerminal(before) && after != "open")
            throw Invalid("invalid-task-transition");
        if (after == "completed" && checklist.Any(x => x.Required && !x.Completed))
            throw Invalid("task-checklist-incomplete");
    }

    public static void ValidateSelection(IReadOnlyList<TaskSelection> selection)
    {
        if (selection is null || selection.Count is < 1 or > 100) throw Invalid("invalid-task-selection");
        var ids = new HashSet<Guid>();
        foreach (var item in selection)
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || !StrongEtag(item.Etag))
                throw Invalid("invalid-task-selection");
    }

    public static void ValidateChecklist(IReadOnlyList<TaskChecklistState> current, IReadOnlyList<TaskChecklistChange> changes)
    {
        if (changes is null || changes.Count is < 1 or > 100) throw Invalid("invalid-task-checklist");
        var ids = new HashSet<Guid>();
        foreach (var item in changes)
            if (item is null || !ids.Add(item.Id) || !current.Any(x => x.Id == item.Id)) throw Invalid("invalid-task-checklist");
    }

    public static bool IsTerminal(string state) => state is "completed" or "cancelled";
    public static bool IsOverdue(string state, DateOnly? dueOn, DateOnly today) => !IsTerminal(state) && dueOn is { } due && due < today;
    public static void RequireText(string? value, int maximum, string code)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) throw Invalid(code);
    }
    private static bool StrongEtag(string? value) => value is { Length: >= 3 and <= 100 } && value[0] == '"' && value[^1] == '"'
        && value[1..^1].All(c => c >= 33 && c <= 126 && c != '"');
    private static TaskRuleException Invalid(string code) => new(code);
}
