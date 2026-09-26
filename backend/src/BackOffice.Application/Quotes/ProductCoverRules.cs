using System.Text.Json;

namespace BackOffice.Application.Quotes;

// These codes correspond to the existing proposal section contracts. Historic
// definitions without this field retain their original supported sections.
public static class ProductCoverRules
{
    public static string[] Supported(string product) => product switch
    {
        "motor-trade-road-risks" => ["road-risks", "tools-equipment"],
        "motor-trade-combined" => ["road-risks", "tools-equipment", "stock-custody", "premises"],
        "commercial-combined" => ["commercial-combined", "contract-works"],
        _ => []
    };

    public static bool Valid(string product, string[]? sections)
    {
        var supported = Supported(product);
        return supported.Length > 0 && sections is { Length: > 0 } &&
            sections.Contains(supported[0], StringComparer.Ordinal) &&
            sections.Distinct(StringComparer.Ordinal).Count() == sections.Length &&
            sections.All(x => supported.Contains(x, StringComparer.Ordinal));
    }

    public static string[] Read(string definition, string product)
    {
        using var doc = JsonDocument.Parse(definition);
        return doc.RootElement.TryGetProperty("coverSections", out var value)
            ? value.Deserialize<string[]>() ?? [] : Supported(product);
    }

    public static void EnsureProposal(string definition, string product, string proposal)
    {
        var allowed = Read(definition, product);
        if (!Valid(product, allowed)) throw new QuoteInputException("product-cover-configuration-invalid");
        using var doc = JsonDocument.Parse(proposal);
        if (!doc.RootElement.TryGetProperty("cover", out var cover)) return;
        var issues = new List<QuoteFieldIssue>();
        if (cover.TryGetProperty("requestedSections", out var sections))
        {
            var index = 0;
            foreach (var section in sections.EnumerateArray())
            {
                if (section.TryGetProperty("selected", out var selected) && selected.ValueKind == JsonValueKind.True &&
                    !allowed.Contains(section.GetProperty("code").GetString(), StringComparer.Ordinal))
                    issues.Add(new("section-not-offered", $"/cover/requestedSections/{index}/selected"));
                index++;
            }
        }
        if (cover.TryGetProperty("contractWorks", out var works) && works.TryGetProperty("selected", out var chosen) &&
            chosen.ValueKind == JsonValueKind.True && !allowed.Contains("contract-works", StringComparer.Ordinal))
            issues.Add(new("section-not-offered", "/cover/contractWorks/selected"));
        if (issues.Count > 0) throw new QuoteValidationException(issues);
    }
}
