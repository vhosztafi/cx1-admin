using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackOffice.Application.Quotes;

public sealed record QuoteRevisionSide(string Path, string Json);
public sealed record QuoteRevisionChange(string Kind, string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ItemId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] QuoteRevisionSide? Before,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] QuoteRevisionSide? After);

public static class QuoteRevisionDiff
{
    // Scope and pagination belong to the held revision reader. Never truncate
    // sensitive values here: authorized callers receive complete typed JSON.
    public static IReadOnlyList<QuoteRevisionChange> Compare(JsonElement before, JsonElement after)
    {
        var changes = new List<QuoteRevisionChange>();
        static string Escape(string key) => key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
        static bool Keyed(JsonElement array) => array.ValueKind == JsonValueKind.Array && array.EnumerateArray().All(x =>
            x.ValueKind == JsonValueKind.Object && x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParseExact(id.GetString(), "D", out _));
        static QuoteRevisionSide? Side(JsonElement value, string path) => value.ValueKind == JsonValueKind.Undefined ? null : new(path.Length == 0 ? "/" : path, value.GetRawText());
        void Visit(JsonElement left, JsonElement right, string leftPath, string rightPath, Guid? itemId)
        {
            if (left.ValueKind == JsonValueKind.Object && right.ValueKind == JsonValueKind.Object)
            {
                var names = left.EnumerateObject().Select(x => x.Name).Concat(right.EnumerateObject().Select(x => x.Name)).Distinct().Order(StringComparer.Ordinal);
                foreach (var name in names)
                {
                    left.TryGetProperty(name, out var l); right.TryGetProperty(name, out var r);
                    Visit(l, r, leftPath + "/" + Escape(name), rightPath + "/" + Escape(name), itemId);
                }
                return;
            }
            if (Keyed(left) && Keyed(right))
            {
                var l = left.EnumerateArray().Select((value, index) => (value, index)).ToDictionary(x => x.value.GetProperty("id").GetGuid());
                var r = right.EnumerateArray().Select((value, index) => (value, index)).ToDictionary(x => x.value.GetProperty("id").GetGuid());
                foreach (var id in l.Keys.Concat(r.Keys).Distinct().Order())
                {
                    var hasLeft = l.TryGetValue(id, out var lv); var hasRight = r.TryGetValue(id, out var rv);
                    var lp = leftPath + "/" + lv.index; var rp = rightPath + "/" + rv.index;
                    if (hasLeft && hasRight && lv.index != rv.index)
                        changes.Add(new("reordered", rp, id, Side(lv.value, lp), Side(rv.value, rp)));
                    Visit(lv.value, rv.value, lp, rp, id);
                }
                return;
            }
            if (left.ValueKind == JsonValueKind.Array && right.ValueKind == JsonValueKind.Array)
            {
                for (var i = 0; i < Math.Max(left.GetArrayLength(), right.GetArrayLength()); i++)
                    Visit(i < left.GetArrayLength() ? left[i] : default, i < right.GetArrayLength() ? right[i] : default, leftPath + "/" + i, rightPath + "/" + i, itemId);
                return;
            }
            if (left.ValueKind == right.ValueKind && (left.ValueKind == JsonValueKind.Undefined || JsonElement.DeepEquals(left, right))) return;
            var kind = left.ValueKind == JsonValueKind.Undefined ? "added" : right.ValueKind == JsonValueKind.Undefined ? "removed" : "changed";
            var path = kind == "removed" ? leftPath : rightPath;
            changes.Add(new(kind, path.Length == 0 ? "/" : path, itemId, Side(left, leftPath), Side(right, rightPath)));
        }
        Visit(before, after, "", "", null);
        return changes;
    }
}
