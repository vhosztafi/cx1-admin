using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BackOffice.Application.Operations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowChecklistDefinition([property: JsonRequired] string Code, [property: JsonRequired] string Label, [property: JsonRequired] bool Required);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowTaskDefinition(
    [property: JsonRequired] string Format, [property: JsonRequired] string Publication, [property: JsonRequired] string Code,
    [property: JsonRequired] string Family, [property: JsonRequired] string TaskType, [property: JsonRequired] string Title,
    [property: JsonRequired] string Priority, [property: JsonRequired] string InitialState,
    [property: JsonRequired] int DueDays, [property: JsonRequired] int LeadDays,
    [property: JsonRequired] WorkflowChecklistDefinition[] Checklist);

// Published definitions are immutable SettingVersion values. The owning SQL
// service must also check effective version, source ownership and event identity.
public static partial class WorkflowTaskRules
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16
    };
    public static WorkflowTaskDefinition Parse(string json, string scope)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 16_384) throw Invalid();
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            UniqueProperties(document.RootElement);
            var definition = JsonSerializer.Deserialize<WorkflowTaskDefinition>(json, Json) ?? throw Invalid();
            Validate(definition, scope); return definition;
        }
        catch (JsonException) { throw Invalid(); }
    }

    public static void Validate(WorkflowTaskDefinition rule, string scope)
    {
        if (rule is null || rule.Format != "workflow-task-1" || rule.Publication != "published" || !ValidCode(rule.Code) || scope != "workflow-task/" + rule.Code ||
            rule.DueDays is < 0 or > 365 || rule.LeadDays is < 0 or > 365 || rule.InitialState is not ("open" or "awaiting-information") ||
            !AllowedType(rule.Family, rule.TaskType) || rule.Checklist is null || rule.Checklist.Length > 20) throw Invalid();
        TaskRules.ValidateWrite(new(rule.TaskType, rule.Title, rule.Priority, new("unassigned"), null));
        if (rule.Title.Length > 200) throw Invalid();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in rule.Checklist)
        {
            if (item is null || !ValidCode(item.Code) || !codes.Add(item.Code)) throw Invalid();
            TaskRules.RequireText(item.Label, 300, "invalid-workflow-rule");
        }
    }

    public static void DemandSource(WorkflowTaskDefinition rule, string sourceKind)
    {
        var family = sourceKind switch
        {
            "quote-referral" or "servicing-referral" => "referral",
            "quote-query" or "servicing-query" or "match-information-request" => "missing-information",
            "policy-term" => "renewal-reminder",
            "agency-follow-up" => "agency-follow-up",
            "job-exception" => "job-exception",
            _ => throw Invalid()
        };
        if (rule is null || rule.Family != family) throw Invalid();
    }

    public static string OperationKey(WorkflowTaskDefinition rule, string sourceKind, Guid sourceEventId)
    {
        if (rule is null || sourceEventId == Guid.Empty) throw Invalid();
        Validate(rule, "workflow-task/" + rule.Code); DemandSource(rule, sourceKind);
        // A new published version changes future snapshots, not event identity.
        // The first creation pins its version; reconciliation never reopens it.
        return $"workflow/{rule.Code}/{sourceKind}/{sourceEventId:N}";
    }

    private static bool AllowedType(string family, string type) => family switch
    {
        "referral" => type is "underwriting-referral" or "authority-referral",
        "missing-information" => type == "servicing",
        "renewal-reminder" => type == "renewal",
        "agency-follow-up" => type == "agency-onboarding",
        "job-exception" => type == "data-exception",
        _ => false
    };
    private static void UniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw Invalid(); UniqueProperties(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) UniqueProperties(item);
    }
    private static bool ValidCode(string value) => value is { Length: > 0 and <= 64 } && CodePattern().IsMatch(value);
    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)] private static partial Regex CodePattern();
    private static TaskRuleException Invalid() => new("invalid-workflow-rule");
}
