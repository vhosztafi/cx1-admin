using System.Globalization;
using System.Text.Json;

namespace BackOffice.Application.Quotes;

internal sealed record QuoteSectionField(JsonElement Value,string Path,string? QuestionId = null);
internal static class QuoteSectionValues
{
    internal static JsonElement At(JsonElement value,string path) { foreach (var part in path.Split('.')) if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part,out value)) return default; return value; }
    internal static IEnumerable<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    internal static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static long? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
    internal static bool? Boolean(JsonElement value) => value.ValueKind == JsonValueKind.True ? true : value.ValueKind == JsonValueKind.False ? false : null;
    internal static bool Present(JsonElement value) => value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) && (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));
    internal static bool Date(string? value,out DateOnly date) => DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date);
    internal static QuoteSectionField Answer(JsonElement holder,string root,string id)
    {
        var index = 0;
        foreach (var row in Items(At(holder,"responses.answers")))
        { if (Text(At(row,"questionId")) == id) return new(At(row,"value"),$"{root}/responses/answers/{index}/value",id); index++; }
        return new(default,root + "/responses/answers",id);
    }
    internal static JsonElement Reference(QuoteSectionField field,string collection) => QuoteReferenceMetadata.Trusted(field.Value,collection);
}
