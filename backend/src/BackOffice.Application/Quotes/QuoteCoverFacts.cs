using System.Globalization;
using System.Text.Json;

namespace BackOffice.Application.Quotes;

internal static class QuoteReferenceMetadata
{
    private static readonly Lazy<JsonElement> Data = new(() => {
        _ = QuoteCatalogueIdentity.Version;
        using var stream = typeof(QuoteReferenceMetadata).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    });
    internal static JsonElement Root => Data.Value;
    internal static JsonElement Collection(string name) => Root.GetProperty("collections").TryGetProperty(name, out var rows) ? rows : default;
    internal static JsonElement Trusted(JsonElement reference, string collection)
    {
        var value = QuoteCatalogueIdentity.TrustedValue(reference, collection);
        return value is null ? default : Collection(collection).EnumerateArray().First(row => row.GetProperty("value").GetInt64() == value);
    }
    internal static decimal? Numeric(JsonElement row) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty("numericValue", out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetDecimal(out var value) ? value : null;
}

public sealed record QuoteCoverFactsResult(IReadOnlyDictionary<string,string> Facts, IReadOnlyList<QuoteFieldIssue> Issues);

// Derive common business facts from independently retained source selections.
// This establishes dynamic-option context, not pricing or complete cover readiness.
public static class QuoteCoverFacts
{
    private sealed record Group(string Fact,string Source,string Prototype,string? Collection,string[] Values);
    private static readonly Group[] Groups = [
        new("coverLevel","MTS-05-Q01","prototype.quote.2beaf3b8d546","coverLevels",["comprehensive","third-party-fire-theft","third-party-only"]),
        new("ownVehicleLimit","MTS-05-Q02","prototype.quote.d9dd069a314c","indemnityOwnVehicles",["7500.00","10000.00","12500.00","15000.00","20000.00","25000.00","30000.00"]),
        new("customerVehicleLimit","MTS-05-Q03","prototype.quote.b4c7e25f7781","indemnityCustomerVehicles",["7500.00","10000.00","12500.00","15000.00","20000.00","25000.00","30000.00"]),
        new("excess","MTS-05-Q04","prototype.quote.00216de47ab5",null,["250.00","500.00","750.00","1000.00"])
    ];
    public static QuoteCoverFactsResult Reconcile(JsonElement proposal)
    {
        var facts = new Dictionary<string,string>(); var issues = new List<QuoteFieldIssue>();
        var answers = At(proposal,"cover.responses.answers");
        foreach (var group in Groups)
        {
            var declarations = new List<(string Value,string Path,string Question)>(); var unresolved = false; var index = 0;
            foreach (var answer in answers.ValueKind == JsonValueKind.Array ? answers.EnumerateArray() : [])
            {
                var path = $"/cover/responses/answers/{index++}/value"; var question = Text(At(answer,"questionId"));
                if (question != group.Source && question != group.Prototype) continue;
                var reference = At(answer,"value"); var family = Text(At(reference,"collection")); var prototype = question == group.Prototype;
                var correct = prototype ? family == group.Prototype : group.Collection is not null ? family == group.Collection :
                    family is not null && family.StartsWith("indemnityOwnVehicles/",StringComparison.Ordinal) && family.EndsWith("/excesses",StringComparison.Ordinal);
                var row = correct && family is not null && Text(At(answer,"kind")) == "reference" ? QuoteReferenceMetadata.Trusted(reference,family) : default;
                string? value = null;
                if (row.ValueKind != JsonValueKind.Undefined)
                {
                    if (prototype || group.Fact == "coverLevel") { var id = row.GetProperty("value").GetInt64(); if (id >= 1 && id <= group.Values.Length) value = group.Values[(int)id - 1]; }
                    else if (QuoteReferenceMetadata.Numeric(row) is { } numeric && numeric >= 0) value = numeric.ToString("F2",CultureInfo.InvariantCulture);
                }
                if (value is null) { unresolved = true; issues.Add(new("unresolved-cover-selection",path,question)); }
                else declarations.Add((value,path,question!));
            }
            if (declarations.Select(item => item.Value).Distinct(StringComparer.Ordinal).Skip(1).Any())
                issues.AddRange(declarations.Select(item => new QuoteFieldIssue("conflicting-cover-declarations",item.Path,item.Question)));
            else if (declarations.Count > 0 && !unresolved) facts[group.Fact] = declarations[0].Value;
        }
        return new(facts,issues.Take(QuoteCaptureShape.MaximumIssues).ToArray());
    }
    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static JsonElement At(JsonElement value,string path) { foreach (var part in path.Split('.')) if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part,out value)) return default; return value; }
}
