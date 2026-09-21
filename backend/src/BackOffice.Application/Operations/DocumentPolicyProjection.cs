using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BackOffice.Application.Operations;

public sealed record DocumentField(string Label, string Value);
public sealed record DocumentSection(string Title, IReadOnlyList<DocumentField> Fields);

// The renderer receives only text and tables. Neither stored text nor templates
// are interpreted as markup, images, links, paths, scripts or executable layout.
public static partial class DocumentPolicyProjection
{
    public const string Version = "policy-projection-1";
    public const string QuestionLabelsHash = "243dd1fdb22477f46562c015156c2c9aa36e8e52b1d3e696c9e17cc289613d82";
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Questions = new(() =>
    {
        using var stream = typeof(DocumentPolicyProjection).Assembly.GetManifestResourceStream("Document.QuestionLabelsV1")!;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))) != QuestionLabelsHash)
            throw new DocumentRenderException("document-question-catalogue-hash");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
    });

    public static IReadOnlyList<DocumentSection> Create(DocumentRenderContract contract)
    {
        var source = contract.Source; var kind = DocumentRenderContract.CanonicalKind(contract.Input.Kind);
        var sections = new List<DocumentSection>();
        void Add(string title, JsonElement value)
        {
            var fields = new List<DocumentField>(); Flatten(value, "", fields);
            if (fields.Count == 0) fields.Add(new("Recorded items", "None"));
            sections.Add(new(title, fields.AsReadOnly()));
        }
        Add("Insured", source.GetProperty("insured"));
        Add("Policy period", source.GetProperty("term"));
        var risk = source.GetProperty("risk"); var cover = source.GetProperty("cover");
        if (kind == "cancellation-notice")
        {
            Add("Cancellation", source.GetProperty("cancellation"));
            sections.Add(new("Effect of cancellation", [new("Cover", "Cover ends at the recorded cancellation effective time. Historical declarations and premiums are retained separately; this notice does not calculate a refund.")]));
        }
        else if (kind == "policy-certificate")
        {
            if (contract.Input.ProductCode == "commercial-combined")
            {
                Add("Employers' liability cover", cover.GetProperty("sections").EnumerateArray().Single(x => x.GetProperty("code").GetString() == "employers-liability"));
                if (risk.GetProperty("liability").TryGetProperty("employersReferenceNumber", out var number))
                    Add("Employers' reference number", number);
            }
            else
            {
                Add("Motor cover", cover.GetProperty("sections"));
                foreach (var field in new[] { "driverBasis", "drivers", "vehicles" })
                    if (risk.TryGetProperty(field, out var value)) Add(Label(field), value);
            }
            Add("Selected endorsements", cover.GetProperty("endorsements"));
            Add("Warranties", cover.GetProperty("warranties"));
        }
        else if (kind == "endorsement")
        {
            Add("Selected endorsements", cover.GetProperty("endorsements"));
            Add("Warranties", cover.GetProperty("warranties"));
        }
        else
        {
            foreach (var property in risk.EnumerateObject()) Add(Label(property.Name), property.Value);
            // Requested options remain declarations; only the selected sections
            // constitute issued cover. Give them distinct printed headings.
            foreach (var property in cover.EnumerateObject())
                Add(property.Name switch { "sections" => "Selected cover", "requestedSections" => "Requested options (declarations)", "endorsements" => "Selected endorsements", _ => Label(property.Name) }, property.Value);
            if (kind == "policy-schedule") Add("Premium and charges", source.GetProperty("premium"));
        }
        if (sections.Sum(x => x.Fields.Count) > 12000) throw new DocumentRenderException("document-render-row-limit");
        return sections.AsReadOnly();
    }

    private static void Flatten(JsonElement value, string path, List<DocumentField> fields)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("questionId", out var question))
            {
                var id = question.GetString()!;
                if (!Questions.Value.TryGetValue(id, out var label)) throw new DocumentRenderException("document-question-label-unavailable");
                Flatten(value.GetProperty("value"), Join(path, label), fields); return;
            }
            if (value.TryGetProperty("collection", out _) && value.TryGetProperty("label", out var referenceLabel))
            {
                fields.Add(new(path.Length == 0 ? "Declared value" : path, referenceLabel.GetString()!)); return;
            }
            if (!value.EnumerateObject().Any()) fields.Add(new(path, "None recorded"));
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name is "clientId" or "clientAgencyRelationshipId" or "questionSetVersion") continue;
                Flatten(property.Value, property.Name is "responses" or "answers" ? path : Join(path, Label(property.Name)), fields);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() == 0) fields.Add(new(path.Length == 0 ? "Recorded items" : path, "None"));
            var index = 0;
            foreach (var item in value.EnumerateArray()) Flatten(item, Join(path, "Item " + ++index), fields);
        }
        else
        {
            var text = value.ValueKind switch { JsonValueKind.True => "Yes", JsonValueKind.False => "No", JsonValueKind.Null => "Not recorded",
                JsonValueKind.String => value.GetString()!, _ => value.GetRawText() };
            string[] moneyFields = ["Limit", "Excess", "Sum insured", "Turnover", "Wage roll", "Annual premium", "Term premium", "Premium", "Tax", "Fee", "Gross payable", "Net due", "Broker commission", "Declared value", "Employers limit", "Public limit", "Products limit", "Buildings", "Contents", "Stock", "Maximum estimated loss"];
            if (moneyFields.Contains(path.Split(" / ")[^1], StringComparer.OrdinalIgnoreCase) && Money().IsMatch(text) && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
                text = "£" + amount.ToString("N2", CultureInfo.GetCultureInfo("en-GB"));
            fields.Add(new(path.Length == 0 ? "Recorded value" : path, text));
        }
    }

    private static string Join(string left, string right) => left.Length == 0 ? right : left + " / " + right;
    private static string Label(string name)
    {
        var text = WordBoundary().Replace(name, "$1 $2").Replace('-', ' ');
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)] private static partial Regex WordBoundary();
    [GeneratedRegex("^-?[0-9]+\\.[0-9]{2}$", RegexOptions.CultureInvariant)] private static partial Regex Money();
}
