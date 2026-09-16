using System.Collections.Frozen;
using System.Text.Json;

namespace BackOffice.Application.Quotes;

// Identity validation after canonical parsing and strict shape checks. Dynamic
// bindings accept only their configured candidate families here; selecting the
// eligible family from age/cover context belongs to semantic readiness.
public static class QuoteCatalogueIdentity
{
    private sealed record Binding(string[] Collections, string Rule);
    private sealed record Catalogue(string Version, FrozenDictionary<(string Product, string Scope, string Question), string> Questions,
        FrozenDictionary<(string Scope, string? Question), Binding> Bindings,
        FrozenDictionary<string, FrozenDictionary<long, string>> Collections, string[] Products);
    private static readonly Lazy<Catalogue> Data = new(Load);
    public static string Version => Data.Value.Version;

    internal static long? TrustedValue(JsonElement reference, string collection)
    {
        if (reference.ValueKind != JsonValueKind.Object ||
            !reference.TryGetProperty("collection", out var family) || family.GetString() != collection ||
            !reference.TryGetProperty("version", out var version) || version.GetString() != Data.Value.Version ||
            !reference.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var id) ||
            !Data.Value.Collections.TryGetValue(collection, out var options) || !options.TryGetValue(id, out var label) ||
            !reference.TryGetProperty("label", out var suppliedLabel) || suppliedLabel.GetString() != label) return null;
        return id;
    }

    public static IReadOnlyList<QuoteFieldIssue> ValidateQuestions(JsonElement proposal)
    {
        var data = Data.Value; var issues = new List<QuoteFieldIssue>();
        var product = proposal.GetProperty("productCode").GetString()!;
        if (!data.Products.Contains(product, StringComparer.Ordinal)) return [new("unsupported-capture-product", "/productCode")];
        Visit(proposal, (item, path, scope) =>
        {
            if (!item.TryGetProperty("answers", out var answers) && !item.TryGetProperty("questionSetVersion", out _)) return;
            if (!item.TryGetProperty("questionSetVersion", out var version) || version.GetString() != data.Version)
                Add(issues, "question-version-mismatch", path + "/questionSetVersion");
            if (answers.ValueKind == JsonValueKind.Undefined) return;
            var seen = new HashSet<string>(StringComparer.Ordinal); var index = 0;
            foreach (var answer in answers.EnumerateArray())
            {
                var answerPath = path + "/answers/" + index++;
                var id = answer.GetProperty("questionId").GetString()!;
                if (!seen.Add(id)) Add(issues, "duplicate-question-id", answerPath + "/questionId");
                if (!data.Questions.TryGetValue((product, scope, id), out var kind)) Add(issues, "unknown-question-id", answerPath + "/questionId");
                else if (answer.GetProperty("kind").GetString() != kind) Add(issues, "question-kind-mismatch", answerPath + "/kind");
            }
        });
        return issues;
    }

    public static IReadOnlyList<QuoteFieldIssue> ValidateReferences(JsonElement proposal)
    {
        var data = Data.Value; var issues = new List<QuoteFieldIssue>();
        void Selection(JsonElement reference, Binding? binding, string path)
        {
            if (binding is null) { Add(issues, "unbound-reference", path); return; }
            if (reference.GetProperty("version").GetString() != data.Version) { Add(issues, "reference-version-mismatch", path + "/version"); return; }
            var collection = reference.GetProperty("collection").GetString()!;
            if (!binding.Collections.Contains(collection, StringComparer.Ordinal)) { Add(issues, "reference-collection-mismatch", path + "/collection"); return; }
            var value = reference.GetProperty("value");
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var id) || !data.Collections[collection].TryGetValue(id, out var label))
            { Add(issues, "unknown-reference-value", path + "/value"); return; }
            if (reference.GetProperty("label").GetString() != label) Add(issues, "reference-label-mismatch", path + "/label");
        }
        Visit(proposal, (item, path, scope) =>
        {
            if (item.TryGetProperty("kind", out var kind) && kind.GetString() is "reference" or "references" && item.TryGetProperty("questionId", out var question))
            {
                data.Bindings.TryGetValue((scope, question.GetString()), out var binding);
                var value = item.GetProperty("value");
                if (kind.GetString() == "reference") Selection(value, binding, path + "/value");
                else
                {
                    var seen = new HashSet<(string?, JsonValueKind, string)>(); var index = 0;
                    foreach (var reference in value.EnumerateArray())
                    {
                        var referencePath = path + "/value/" + index++;
                        var identity = reference.GetProperty("value");
                        if (!seen.Add((reference.GetProperty("collection").GetString(), identity.ValueKind, identity.GetRawText())))
                            Add(issues, "duplicate-reference-selection", referencePath);
                        Selection(reference, binding, referencePath);
                    }
                }
            }
            else if (item.TryGetProperty("collection", out _) && !scope.Contains(".answers[].value", StringComparison.Ordinal))
            {
                data.Bindings.TryGetValue((scope, null), out var binding); Selection(item, binding, path);
            }
        });
        return issues;
    }

    private static Catalogue Load()
    {
        using var questions = Read("QuoteCapture.Questions"); using var references = Read("QuoteCapture.References");
        if (!QuoteCaptureShape.BuildBundled("QuoteCapture.QuestionSchema").Evaluate(questions.RootElement).IsValid ||
            !QuoteCaptureShape.BuildBundled("QuoteCapture.ReferenceSchema").Evaluate(references.RootElement).IsValid)
            throw new InvalidOperationException("Bundled quote catalogue shape is invalid.");
        var q = questions.RootElement; var r = references.RootElement;
        var version = q.GetProperty("version").GetString()!;
        if (version != r.GetProperty("version").GetString()) throw new InvalidOperationException("Quote catalogue versions disagree.");
        var definitions = new Dictionary<(string, string, string), string>();
        foreach (var row in q.GetProperty("mappings").EnumerateArray().Where(row => row.GetProperty("contractKind").GetString() == "Answer"))
        {
            var path = row.GetProperty("canonicalPath").GetString()!;
            if (!path.EndsWith(".answers[]", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid quote question scope.");
            foreach (var product in row.GetProperty("products").EnumerateArray())
            {
                var key = (product.GetString()!, path[..^10], row.GetProperty("questionId").GetString()!);
                var kind = row.GetProperty("answerKind").GetString()!;
                if (definitions.TryGetValue(key, out var prior) && prior != kind) throw new InvalidOperationException("Conflicting quote question definition.");
                definitions[key] = kind;
            }
        }
        var collections = new Dictionary<string, FrozenDictionary<long, string>>(StringComparer.Ordinal);
        foreach (var collection in r.GetProperty("collections").EnumerateObject())
        {
            var rows = new Dictionary<long, string>();
            foreach (var option in collection.Value.EnumerateArray())
                if (!rows.TryAdd(option.GetProperty("value").GetInt64(), option.GetProperty("text").GetString()!))
                    throw new InvalidOperationException("Duplicate quote reference identity.");
            collections.Add(collection.Name, rows.ToFrozenDictionary());
        }
        var bindings = new Dictionary<(string, string?), Binding>();
        foreach (var row in r.GetProperty("bindings").EnumerateArray())
        {
            var key = (row.GetProperty("canonicalPath").GetString()!, row.TryGetProperty("questionId", out var question) ? question.GetString() : null);
            var families = row.GetProperty("collections").EnumerateArray().Select(value => value.GetString()!).ToArray();
            var rule = row.GetProperty("selectionRule").GetString()!;
            if (families.Any(family => !collections.ContainsKey(family)) || (rule == "fixed" && families.Length != 1))
                throw new InvalidOperationException("Invalid quote reference binding.");
            if (bindings.TryGetValue(key, out var prior) && (prior.Rule != rule || !prior.Collections.SequenceEqual(families)))
                throw new InvalidOperationException("Conflicting quote reference binding.");
            bindings[key] = new(families, rule);
        }
        return new(version, definitions.ToFrozenDictionary(), bindings.ToFrozenDictionary(), collections.ToFrozenDictionary(StringComparer.Ordinal),
            q.GetProperty("products").EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

    private static JsonDocument Read(string name)
    {
        using var stream = typeof(QuoteCatalogueIdentity).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Bundled quote catalogue is missing.");
        return JsonDocument.Parse(stream);
    }
    private static void Add(List<QuoteFieldIssue> issues, string code, string path)
    {
        if (issues.Count < QuoteCaptureShape.MaximumIssues) issues.Add(new(code, path.Length <= 1024 ? path : ""));
    }
    private static void Visit(JsonElement value, Action<JsonElement, string, string> action, string path = "", string scope = "")
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0; foreach (var item in value.EnumerateArray()) Visit(item, action, path + "/" + index++, scope + "[]");
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            action(value, path, scope);
            foreach (var property in value.EnumerateObject())
                Visit(property.Value, action, path + "/" + property.Name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal), scope.Length == 0 ? property.Name : scope + "." + property.Name);
        }
    }
}
