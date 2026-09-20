using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record ServicingRatingSettings(decimal AdjustmentFee);
public static class ServicingRatingConfiguration
{
    public static ServicingRatingSettings? Parse(string json, string scope = "servicing-rating")
    {
        if (scope is not ("servicing-rating" or "commercial-servicing-rating")) return null;
        if (json is null || json.Length > 4096) return null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var keys = new HashSet<string>(["demo", "kind", "schemaVersion", "currency", "adjustmentFee", "earningBasis"], StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject()) if (!keys.Remove(property.Name)) return null;
            if (keys.Count != 0 || root.GetProperty("demo").ValueKind != JsonValueKind.True ||
                Text(root, "kind") != scope || Text(root, "schemaVersion") != "1" ||
                Text(root, "currency") != "GBP" || Text(root, "earningBasis") != "london-calendar-days") return null;
            var text = Text(root, "adjustmentFee");
            if (text is null || text.Length > 16 || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fee) ||
                fee < 0 || fee > QuoteRatingRules.MaximumMoney || text != fee.ToString("0.00", CultureInfo.InvariantCulture)) return null;
            return new(fee);
        }
        catch (JsonException) { return null; }
    }
    private static string? Text(JsonElement value, string name) => value.GetProperty(name) is var field && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
}
