using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BackOffice.Application.Quotes;

namespace BackOffice.Application.Operations;

internal static partial class DocumentServicingTermsRules
{
    internal static void Validate(DocumentRenderInput input, JsonElement root, JsonElement template)
    {
        string[] fields = ["format", "draftId", "cycleId", "revisionId", "baseVersionId", "baseTermId", "policyId", "ratingId", "templateVersionId", "inputHash", "ratingHash", "effectiveDates", "slices", "template", "ratingInput", "rating", "commercialTerms", "conditions", "expiresAt", "price", "documentState"];
        if (!Closed(root, fields) || root.GetProperty("format").GetString() is not ("renewal-contract-1" or "servicing-contract-1") ||
            root.GetProperty("documentState").GetString() != "structured-payload" || root.GetProperty("templateVersionId").GetGuid() != input.TemplateId ||
            !JsonElement.DeepEquals(root.GetProperty("template"), template)) throw Invalid();
        var renewal = root.GetProperty("format").GetString() == "renewal-contract-1";
        if (input.Kind != "statement-of-fact" && input.Kind != (renewal ? "renewal-invitation" : "quotation") ||
            input.TemplateKind != (renewal ? "renewal-invitation" : "servicing-terms")) throw Invalid();
        foreach (var key in new[] { "draftId", "cycleId", "revisionId", "baseVersionId", "baseTermId", "policyId", "ratingId" })
            if (root.GetProperty(key).GetGuid() == Guid.Empty) throw Invalid();
        foreach (var key in new[] { "inputHash", "ratingHash" })
        {
            var hash = root.GetProperty(key).GetString();
            if (hash is null || !Hash().IsMatch(hash)) throw Invalid();
        }
        _ = root.GetProperty("expiresAt").GetDateTimeOffset();
        var dates = root.GetProperty("effectiveDates"); var slices = root.GetProperty("slices");
        if (dates.ValueKind != JsonValueKind.Array || slices.ValueKind != JsonValueKind.Array || slices.GetArrayLength() is < 1 or > 100 ||
            slices.GetArrayLength() != dates.GetArrayLength() || renewal && slices.GetArrayLength() != 1) throw Invalid();
        DateTimeOffset? previous = null; var index = 0;
        foreach (var slice in slices.EnumerateArray())
        {
            if (!Closed(slice, ["effectiveAt", "proposal"])) throw Invalid();
            var date = slice.GetProperty("effectiveAt").GetDateTimeOffset();
            if (date.Offset != TimeSpan.Zero || dates[index++].GetDateTimeOffset() != date || previous is { } last && last >= date) throw Invalid();
            previous = date;
            var proposal = slice.GetProperty("proposal");
            if (proposal.GetProperty("productCode").GetString() != input.ProductCode ||
                (input.ProductCode == "commercial-combined" ? QuoteCaptureShape.ValidateCommercial(proposal) : QuoteCaptureShape.Validate(proposal)).Count != 0) throw Invalid();
        }
        var price = root.GetProperty("price");
        string[] money = ["premium", "tax", "fee", "brokerCommission", "grossPayable", "netDue"];
        if (!Closed(price, ["currency", .. money]) || price.GetProperty("currency").GetString() != "GBP") throw Invalid();
        foreach (var key in money)
        {
            var text = price.GetProperty(key).GetString();
            if (text is null || !Money().IsMatch(text) || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)) throw Invalid();
        }
        var conditions = root.GetProperty("conditions");
        if (conditions.ValueKind != JsonValueKind.Array || conditions.GetArrayLength() > 100) throw Invalid();
        foreach (var row in conditions.EnumerateArray())
        {
            if (!Closed(row, ["id", "code", "kind", "definition", "effectiveDates"]) || row.GetProperty("id").GetGuid() == Guid.Empty ||
                string.IsNullOrWhiteSpace(row.GetProperty("code").GetString()) || string.IsNullOrWhiteSpace(row.GetProperty("kind").GetString()) ||
                row.GetProperty("definition").ValueKind != JsonValueKind.Object) throw Invalid();
            var applies = row.GetProperty("effectiveDates");
            if (applies.ValueKind != JsonValueKind.Array || applies.GetArrayLength() is < 1 or > 100 ||
                applies.EnumerateArray().Any(x => !dates.EnumerateArray().Any(d => d.GetDateTimeOffset() == x.GetDateTimeOffset()))) throw Invalid();
        }
    }
    private static bool Closed(JsonElement value, string[] fields) => value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == fields.Length && value.EnumerateObject().All(x => fields.Contains(x.Name, StringComparer.Ordinal));
    private static DocumentRenderException Invalid() => new("document-servicing-terms-invalid");
    [GeneratedRegex("^-?(0|[1-9][0-9]{0,12})\\.[0-9]{2}$", RegexOptions.CultureInvariant)] private static partial Regex Money();
    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)] private static partial Regex Hash();
}
