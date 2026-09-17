using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public static class CapacitySeed
{
    public static readonly IReadOnlyList<string> Scenarios = ["approve-stock-150000", "conditional-security", "query-proof", "decline-trade", "transient-then-approve", "conflicting-duplicate"];
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Capacity seed requires held initialization.");
        foreach (var scenario in Scenarios)
        {
            var scope = "capacity-escalation/" + scenario;
            var existing = await db.Set<SettingVersion>().Where(x => x.Scope == scope).ToArrayAsync(token);
            // Upgrade only the original exact demo default, by appending a new
            // version. Retained submissions keep their original due date/scenario.
            if (existing.Length > 0 && !(existing.Length == 1 && existing[0].Version == 1 && Parse(existing[0]) is { ResponseDueHours: 48 })) continue;
            db.Add(new SettingVersion { Scope = scope, Version = existing.Length == 0 ? 1 : 2, EffectiveFrom = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                Values = JsonSerializer.Serialize(new { demo = true, kind = "capacity-escalation", schemaVersion = "2", scenario, responseDueWorkingDays = 2 }) });
        }
        await db.SaveChangesAsync(token);
    }
    internal static (string Scenario, int? ResponseDueHours, int? ResponseDueWorkingDays)? Parse(SettingVersion row)
    {
        try
        {
            using var document = JsonDocument.Parse(row.Values); var root = document.RootElement;
            var fields = root.EnumerateObject().Select(x => x.Name).ToArray();
            var legacy = root.GetProperty("schemaVersion").GetString() == "1";
            var deadlineField = legacy ? "responseDueHours" : "responseDueWorkingDays";
            if (fields.Length != 5 || fields.Distinct().Count() != 5 || fields.Except(["demo", "kind", "schemaVersion", "scenario", deadlineField]).Any() ||
                root.GetProperty("demo").ValueKind != JsonValueKind.True || root.GetProperty("kind").GetString() != "capacity-escalation" || (!legacy && root.GetProperty("schemaVersion").GetString() != "2")) return null;
            var scenario = root.GetProperty("scenario").GetString(); var duration = root.GetProperty(deadlineField).GetInt32();
            return scenario is not null && Scenarios.Contains(scenario) && row.Scope == "capacity-escalation/" + scenario && duration >= 1 && duration <= (legacy ? 168 : 10)
                ? (scenario, legacy ? duration : null, legacy ? null : duration) : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return null; }
    }
}
