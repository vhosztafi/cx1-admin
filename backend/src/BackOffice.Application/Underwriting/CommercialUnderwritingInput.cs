using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Quotes;

namespace BackOffice.Application.Underwriting;

public sealed record ProjectedCommercialUnderwritingInput(CommercialRatingFacts Rating, ResolvedQuoteTerm Term, JsonElement Pricing);

public static class CommercialUnderwritingInput
{
    public static ProjectedCommercialUnderwritingInput Project(JsonElement proposal, QuoteVersionPins pins, JsonElement configuration, DateOnly asOf)
    {
        var prepared = CommercialCaptureRules.Prepare(proposal.GetRawText(), pins);
        if (!CommercialUnderwritingConfiguration.Valid(configuration, "rating")) throw new ArgumentException("Published Commercial Combined rating configuration is required.");
        var issues = CommercialCaptureReadiness.Assess(proposal, asOf).Where(x => x.Severity == "error").ToArray();
        if (issues.Length != 0) throw new QuoteValidationException(issues.Select(x => new QuoteFieldIssue(x.Code, x.Path)).ToArray());
        var term = QuoteTerm.Assess(proposal.GetProperty("termIntent"));
        if (term.Term is null) throw new QuoteValidationException(term.Issues);
        _ = QuoteRatingRules.CivilDuration(term.Term);
        var risk = proposal.GetProperty("risk"); var cover = proposal.GetProperty("cover");
        var declarations = risk.GetProperty("declarations"); var responses = cover.GetProperty("responses");
        var locations = risk.GetProperty("locations").EnumerateArray().OrderBy(x => x.GetProperty("id").GetGuid()).ToArray();
        var biSelected = Boolean(responses, "prototype.quote.7660fc5eb42e");
        var elSelected = Boolean(declarations, "prototype.quote.36ef01068295");
        var liability = risk.GetProperty("liability");
        var bi = risk.TryGetProperty("businessInterruption", out var selectedBi) ? selectedBi : default;
        var wages = elSelected ? risk.GetProperty("wages").EnumerateArray().Select(x => new CommercialWageRating(x.GetProperty("id").GetGuid(),
            x.GetProperty("category").GetProperty("value").GetString()!, Amount(x, "employees"), Amount(x, "labourOnlySubcontractors"), Amount(x, "bonaFideSubcontractors"))).OrderBy(x => x.Id).ToArray() : [];
        var extensions = new List<CommercialExtensionRating>();
        if (biSelected)
        {
            var supplier = Reference(responses, "prototype.quote.9b4688f28580");
            if (supplier != 1)
            {
                var code = supplier switch { 2 => "unspecified-suppliers", 3 => "named-suppliers", 4 => "named-customers", _ => throw Invalid("/cover/responses") };
                var definition = configuration.GetProperty("extensions").GetProperty(code);
                var limit = UnderwritingConfiguration.Amount(definition, "limit");
                if (supplier is 3 or 4)
                {
                    var kind = supplier == 3 ? "supplier" : "customer";
                    var rows = bi.TryGetProperty("dependencies", out var dependencies) ? dependencies.EnumerateArray().Where(x => x.GetProperty("kind").GetString() == kind).ToArray() : [];
                    if (rows.Length == 0) throw Invalid("/risk/businessInterruption/dependencies");
                    limit = rows.Sum(x => Amount(x, "limit"));
                    if (limit <= 0 || limit > UnderwritingConfiguration.Amount(definition, "limit")) throw Invalid("/risk/businessInterruption/dependencies");
                }
                extensions.Add(new(code, limit));
            }
            foreach (var (question, code) in new[] { ("prototype.quote.a2f1dd35162a", "denial-of-access"), ("prototype.quote.ab908171e40b", "loss-of-attraction") })
                if (Reference(responses, question) == 2) extensions.Add(new(code, UnderwritingConfiguration.Amount(configuration.GetProperty("extensions").GetProperty(code), "limit")));
        }
        decimal Selection(string id, string table)
        {
            var value = Reference(responses, id);
            var limits = configuration.GetProperty("selectionLimits").GetProperty(table);
            return limits.TryGetProperty(value.ToString(CultureInfo.InvariantCulture), out _) ? UnderwritingConfiguration.Amount(limits, value.ToString(CultureInfo.InvariantCulture)) : throw Invalid("/cover/responses");
        }
        var works = cover.TryGetProperty("contractWorks", out var cw) && cw.GetProperty("selected").GetBoolean() ? Amount(cw, "sumInsured") : 0;
        var construction = locations.Any(x => {
            var answers = x.GetProperty("responses");
            return Reference(answers, "prototype.addloc.wall-construction") >= 5 || Reference(answers, "prototype.addloc.composite-panels") >= 2 ||
                Boolean(answers, "prototype.addloc.timber-frame") || Reference(answers, "prototype.addloc.heritage-listed") >= 2;
        });
        var flood = Reference(declarations, "prototype.quote.ade0f3f0df5e") >= 2 || Boolean(declarations, "prototype.quote.c08c9ebaf825") ||
            Boolean(declarations, "prototype.quote.a6ff9fdbe769") || locations.Any(x => x.GetProperty("floodZone").GetString() is "2" or "3");
        var facts = new CommercialRatingFacts(locations.Select(x => new CommercialLocationRating(x.GetProperty("id").GetGuid(), Amount(x, "buildings"), Amount(x, "contents"), Amount(x, "stock"))).ToArray(),
            biSelected, biSelected ? Amount(bi, "sumInsured") : 0, biSelected ? bi.GetProperty("indemnityMonths").GetInt32() : 0,
            elSelected, elSelected ? Amount(liability, "employersLimit") : 0, wages, Amount(risk.GetProperty("business"), "turnover"),
            Amount(liability, "publicLimit"), Amount(liability, "productsLimit"), works,
            Selection("prototype.quote.480819d83a08", "goodsInTransit"), Selection("prototype.quote.c8c59a389166", "money"),
            configuration.GetProperty("glassIncluded").GetProperty(Reference(responses, "prototype.quote.1638a58efb48").ToString(CultureInfo.InvariantCulture)).GetBoolean(), extensions, construction, flood,
            Boolean(declarations, "prototype.quote.0b0b63fca324") || risk.TryGetProperty("losses", out var losses) && losses.GetArrayLength() > 0);
        // The canonical complete proposal is retained for the source hash, not accepted as a
        // caller-provided pricing override. Terms/config/identity pins are added by the service.
        using var canonical = JsonDocument.Parse(prepared.Input.Json);
        var pricing = JsonSerializer.SerializeToElement(new { format = "commercial-pricing-1", referenceVersion = CommercialCaptureRules.ReferenceVersion,
            proposal = canonical.RootElement, term = term.Term, facts });
        return new(facts, term.Term, pricing);
    }
    private static decimal Amount(JsonElement owner, string property) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        decimal.TryParse(value.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ? amount : throw Invalid("/risk/" + property);
    private static JsonElement Answer(JsonElement container, string id) => container.GetProperty("answers").EnumerateArray().Single(x => x.GetProperty("questionId").GetString() == id).GetProperty("value");
    private static bool Boolean(JsonElement container, string id) => Answer(container, id).GetBoolean();
    private static int Reference(JsonElement container, string id) => Answer(container, id).GetProperty("value").GetInt32();
    private static QuoteValidationException Invalid(string path) => new([new("commercial-rating-input-invalid", path)]);
}
