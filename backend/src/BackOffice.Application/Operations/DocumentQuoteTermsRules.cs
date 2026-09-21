using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Operations;

internal static partial class DocumentQuoteTermsRules
{
    internal static JsonElement Validate(DocumentRenderInput input, JsonElement source, JsonElement template)
    {
        var terms = input.QuoteTerms;
        if (terms is null || terms.Id == Guid.Empty || input.QuotePins is null || string.IsNullOrEmpty(terms.Json) || Encoding.UTF8.GetByteCount(terms.Json) > DocumentRenderContract.MaximumSourceBytes)
            throw Invalid();
        using var parsed = DocumentRenderContract.Read(terms.Json, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(terms.Json))), DocumentRenderContract.MaximumSourceBytes);
        var root = parsed.RootElement;
        string[] fields = ["format", "quoteId", "cycleId", "revisionId", "revisionHash", "ratingId", "templateVersionId", "template", "clientId", "relationshipId", "insuredName", "agencyName", "productVersionId", "agencyTermsVersionId", "productCode", "risk", "cover", "termIntent", "startsAt", "endsAt", "expiresAt", "conditions", "rating", "price", "settlement", "documentState"];
        if (!Closed(root, fields) || root.GetProperty("format").GetString() != "quote-contract-1" || root.GetProperty("documentState").GetString() != "structured-payload" ||
            root.GetProperty("revisionId").GetGuid() != input.SourceId || root.GetProperty("revisionHash").GetString() != input.SourceHash ||
            root.GetProperty("templateVersionId").GetGuid() != input.TemplateId || root.GetProperty("productCode").GetString() != input.ProductCode ||
            root.GetProperty("productVersionId").GetGuid() != input.QuotePins.ProductVersionId || root.GetProperty("agencyTermsVersionId").GetGuid() != input.QuotePins.AgencyTermsVersionId ||
            !JsonElement.DeepEquals(root.GetProperty("template"), template)) throw Invalid();
        foreach (var key in new[] { "quoteId", "cycleId", "ratingId", "clientId", "relationshipId" }) if (root.GetProperty(key).GetGuid() == Guid.Empty) throw Invalid();
        foreach (var key in new[] { "risk", "cover", "termIntent" }) if (!JsonElement.DeepEquals(root.GetProperty(key), source.GetProperty(key))) throw Invalid();
        foreach (var key in new[] { "insuredName", "agencyName" }) if (string.IsNullOrWhiteSpace(root.GetProperty(key).GetString())) throw Invalid();
        if (root.GetProperty("endsAt").GetDateTimeOffset() <= root.GetProperty("startsAt").GetDateTimeOffset()) throw Invalid();
        _ = root.GetProperty("expiresAt").GetDateTimeOffset();
        if (UnderwritingHashes.Terms(root.GetProperty("cycleId").GetGuid(), root.GetProperty("ratingId").GetGuid(), input.QuotePins, root) != terms.Hash) throw Invalid();
        var price = root.GetProperty("price");
        string[] money = ["annualPremium", "termPremium", "tax", "fee", "grossPayable", "brokerCommission"];
        if (!Closed(price, ["currency", .. money]) || price.GetProperty("currency").GetString() != "GBP") throw Invalid();
        foreach (var key in money)
        {
            var text = price.GetProperty(key).GetString();
            if (text is null || !Money().IsMatch(text) || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)) throw Invalid();
        }
        var conditions = root.GetProperty("conditions");
        if (conditions.ValueKind != JsonValueKind.Array || conditions.GetArrayLength() > 100) throw Invalid();
        foreach (var row in conditions.EnumerateArray())
            if (!Closed(row, ["id", "code", "kind", "wording", "endorsementCode", "decisionId", "definition"]) ||
                row.GetProperty("id").GetGuid() == Guid.Empty || row.GetProperty("decisionId").GetGuid() == Guid.Empty ||
                string.IsNullOrWhiteSpace(row.GetProperty("wording").GetString()) || string.IsNullOrWhiteSpace(row.GetProperty("code").GetString())) throw Invalid();
        return root.Clone();
    }
    private static bool Closed(JsonElement value, string[] fields) => value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == fields.Length && value.EnumerateObject().All(x => fields.Contains(x.Name, StringComparer.Ordinal));
    private static DocumentRenderException Invalid() => new("document-quote-terms-invalid");
    [GeneratedRegex("^(0|[1-9][0-9]{0,12})\\.[0-9]{2}$", RegexOptions.CultureInvariant)] private static partial Regex Money();
}
