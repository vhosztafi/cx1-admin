using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackOffice.Application.Quotes;

public sealed record QuoteFieldIssue(string Code, string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? QuestionId = null);

// Runs after bounded JSON parsing and the closed capture shape validator.
// Item identities are global; links resolve against typed current-document sets,
// never database IDs, previous revisions or arbitrary nested history records.
public static class QuoteItemIdentity
{
    public static IReadOnlyList<QuoteFieldIssue> Validate(JsonElement proposal)
    {
        var issues = new List<QuoteFieldIssue>();
        var allIds = new HashSet<Guid>();
        void Visit(JsonElement item, string path)
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                if (item.TryGetProperty("id", out var value) && ReadId(value, path + "/id", issues) is { } id && !allIds.Add(id))
                    issues.Add(new("duplicate-item-id", path + "/id"));
                foreach (var property in item.EnumerateObject()) Visit(property.Value, path + "/" + Escape(property.Name));
            }
            else if (item.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var child in item.EnumerateArray()) Visit(child, path + "/" + index++);
            }
        }
        Visit(proposal, "");
        var risk = Property(proposal, "risk");
        var drivers = Rows(Property(risk, "drivers"));
        var vehicles = Rows(Property(risk, "vehicles"));
        var premises = Rows(Property(risk, "premises"));
        var driverIds = Ids(drivers); var vehicleIds = Ids(vehicles);
        var riskIds = new HashSet<Guid>(driverIds.Concat(vehicleIds).Concat(Ids(premises)));
        void Link(JsonElement value, HashSet<Guid> allowed, string path)
        {
            if (ReadId(value, path, issues) is { } id && !allowed.Contains(id))
                issues.Add(new("unknown-item-reference", path));
        }
        void Links(JsonElement values, HashSet<Guid> allowed, string path)
        {
            if (values.ValueKind == JsonValueKind.Undefined) return;
            if (values.ValueKind != JsonValueKind.Array) { issues.Add(new("invalid-item-reference-list", path)); return; }
            var seen = new HashSet<Guid>(); var index = 0;
            foreach (var value in values.EnumerateArray())
            {
                var itemPath = path + "/" + index++;
                if (TryId(value, out var id) && !seen.Add(id)) issues.Add(new("duplicate-item-reference", itemPath));
                Link(value, allowed, itemPath);
            }
        }
        Links(Property(risk, "specifiedVehicleIds"), vehicleIds, "/risk/specifiedVehicleIds");
        for (var i = 0; i < vehicles.Length; i++)
            if (Property(vehicles[i], "ownerDriverId") is { ValueKind: not JsonValueKind.Undefined } owner)
                Link(owner, driverIds, $"/risk/vehicles/{i}/ownerDriverId");
        var trips = Rows(Property(Property(proposal, "cover"), "temporaryEuropeanCover"));
        for (var i = 0; i < trips.Length; i++)
            Links(Property(trips[i], "driverIds"), driverIds, $"/cover/temporaryEuropeanCover/{i}/driverIds");
        for (var i = 0; i < drivers.Length; i++)
        {
            var losses = Rows(Property(drivers[i], "losses"));
            for (var j = 0; j < losses.Length; j++)
                if (Property(losses[j], "riskItemId") is { ValueKind: not JsonValueKind.Undefined } item)
                    Link(item, riskIds, $"/risk/drivers/{i}/losses/{j}/riskItemId");
        }
        return issues;
    }

    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static JsonElement Property(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) ? value : default;
    private static JsonElement[] Rows(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    private static bool TryId(JsonElement value, out Guid id)
    {
        id = default;
        return value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString(), "D", out id) && id != Guid.Empty;
    }
    private static Guid? ReadId(JsonElement value, string path, List<QuoteFieldIssue> issues)
    {
        if (TryId(value, out var id)) return id;
        issues.Add(new("invalid-item-id", path)); return null;
    }
    private static HashSet<Guid> Ids(IEnumerable<JsonElement> rows) => rows.Select(row => Property(row, "id"))
        .Where(value => TryId(value, out _)).Select(value => Guid.Parse(value.GetString()!)).ToHashSet();
}
