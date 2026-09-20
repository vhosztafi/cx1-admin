using System.Globalization;
using System.Text.Json;

namespace BackOffice.Application.Underwriting;

public sealed record CommercialReferral(string RuleCode, string Dimension, string Disposition, Guid? TargetId = null,
    decimal? RequestedAmount = null, decimal? AuthorisedAmount = null)
{
    public UnderwritingRequirement Requirement => new(RuleCode, Dimension, TargetId, RequestedAmount, AuthorisedAmount);
}

// These are the published prototype/demo appetite rules, independent of pricing
// loadings. A paid loading does not resolve a referral or increase authority.
public static class CommercialReferralRules
{
    public static IReadOnlyList<CommercialReferral> SourceReferrals(JsonElement proposal)
    {
        RequireProduct(proposal);
        var risk = proposal.GetProperty("risk"); var declarations = risk.GetProperty("declarations");
        var result = new List<CommercialReferral>();
        bool Yes(string id) => Answer(declarations, "prototype.quote." + id).GetBoolean();
        void Add(bool applies, string code, string dimension, string disposition = "internal-review", Guid? target = null,
            decimal? amount = null, decimal? limit = null)
        { if (applies) result.Add(new(code, dimension, disposition, target, amount, limit)); }
        Add(Reference(declarations, "prototype.quote.ade0f3f0df5e") != 1, "PR-04", "flood");
        Add(Yes("a6ff9fdbe769") || Yes("c08c9ebaf825"), "PR-05", "flood");
        Add(Yes("be47c08f530f") && Yes("aaccb5c97c33"), "PR-08", "subsidence", "outside-appetite");
        // Other adverse movement answers require review; signs of actual movement
        // retain the prototype's distinct outside-appetite disposition.
        if (Yes("be47c08f530f"))
            Add(new[] { "8293bc041f81", "02d9c6be528a", "a6a4579c177e", "7a732bc99a5f", "fcc7e22c33ea", "923e600eb3a4" }.Any(Yes), "CC-movement-review", "subsidence");
        foreach (var location in risk.GetProperty("locations").EnumerateArray())
        {
            var id = location.GetProperty("id").GetGuid(); var answers = location.GetProperty("responses");
            Add(Reference(answers, "prototype.addloc.composite-panels") != 1 || Reference(answers, "prototype.addloc.wall-construction") == 6,
                "PR-11", "construction", target: id);
            Add(Answer(answers, "prototype.addloc.timber-frame").GetBoolean() || Reference(answers, "prototype.addloc.wall-construction") == 5,
                "PR-12", "construction", target: id);
            Add(Reference(answers, "prototype.addloc.heritage-listed") != 1, "PR-14", "construction", target: id);
            Add(location.GetProperty("floodZone").GetString() is "2" or "3", "PR-05", "flood", target: id);
            var total = Amount(location, "buildings") + Amount(location, "contents") + Amount(location, "stock");
            Add(total > 2500000m, "AU-05", "single-location", "carrier-required", id, total, 2500000m);
            Add(total > 2000000m, "AU-06", "maximum-estimated-loss", "carrier-required", id, total, 2000000m);
        }
        Add(Yes("755d136885fb"), "PR-17", "waste-recycling", "outside-appetite");
        Add(Yes("cae14bccab65"), "PR-18", "waste-recycling", "outside-appetite");
        Add(Yes("ec38529cf3bd"), "PR-21", "unoccupancy");
        Add(Yes("8cc83c3a006f"), "LI-03", "heat-height");
        Add(Yes("185df2844c76") || risk.GetProperty("liability").GetProperty("maximumHeightMetres").GetDecimal() > 2m, "LI-05", "heat-height");
        Add(Yes("ed8870d17328"), "LI-08", "hazardous-work", "outside-appetite");
        Add(!Yes("c329b4c60f47"), "LI-11", "territory-products");
        Add(Answer(risk.GetProperty("business").GetProperty("responses"), "prototype.quote-value.c2b0372b4040").GetInt32() > 0, "LI-12", "territory-products");
        Add(!Yes("f538427148cf"), "LI-15", "health-safety");
        Add(Yes("8581de6c0b2a"), "LI-18", "health-safety");
        Add(Yes("eb57b4a6cc8b"), "UW-20", "business-history");
        Add(Yes("cfb8d4a5494b"), "UW-18", "business-history");
        var losses = risk.TryGetProperty("losses", out var rows) ? rows.GetArrayLength() : 0;
        Add(losses > 2, "UW-30", "loss-history");
        return result.OrderBy(x => x.RuleCode, StringComparer.Ordinal).ThenBy(x => x.TargetId).ToArray();
    }

    public static IReadOnlyList<UnderwritingRequirement> AssessAuthority(JsonElement definition, JsonElement proposal, decimal annualPremium)
    {
        RequireProduct(proposal);
        var kind = definition.TryGetProperty("kind", out var value) ? value.GetString() : null;
        if (kind is not ("binder" or "authority") || !CommercialUnderwritingConfiguration.Valid(definition, kind) || annualPremium <= 0 || decimal.Round(annualPremium, 2) != annualPremium)
            throw new ArgumentException("Current typed Commercial Combined authority and premium are required.");
        var limits = definition.GetProperty("limits"); var risk = proposal.GetProperty("risk"); var result = new List<UnderwritingRequirement>();
        void Check(string code, string dimension, decimal requested, string field, Guid? id = null)
        {
            var allowed = UnderwritingConfiguration.Amount(limits, field);
            if (requested > allowed) result.Add(new(code, dimension, id, requested, allowed));
        }
        Check("CC-premium-limit", "premium-limit", annualPremium, "annualPremium");
        foreach (var location in risk.GetProperty("locations").EnumerateArray())
        {
            var total = Amount(location, "buildings") + Amount(location, "contents") + Amount(location, "stock");
            Check("AU-05", "single-location", total, "singleLocation", location.GetProperty("id").GetGuid());
            Check("AU-06", "maximum-estimated-loss", total, "maximumEstimatedLoss", location.GetProperty("id").GetGuid());
        }
        var liability = risk.GetProperty("liability");
        if (Answer(risk.GetProperty("declarations"), "prototype.quote.36ef01068295").GetBoolean()) Check("CC-employers-limit", "employers-liability", Amount(liability, "employersLimit"), "employersLiability");
        Check("CC-public-limit", "public-liability", Amount(liability, "publicLimit"), "publicLiability");
        Check("CC-products-limit", "products-liability", Amount(liability, "productsLimit"), "productsLiability");
        if (Answer(proposal.GetProperty("cover").GetProperty("responses"), "prototype.quote.7660fc5eb42e").GetBoolean())
            Check("CC-bi-limit", "business-interruption", Amount(risk.GetProperty("businessInterruption"), "sumInsured"), "businessInterruption");
        if (proposal.GetProperty("cover").TryGetProperty("contractWorks", out var works) && works.GetProperty("selected").GetBoolean())
            Check("CC-works-limit", "contract-works", Amount(works, "sumInsured"), "contractWorks");
        var reviewCodes = definition.GetProperty("reviewCodes").EnumerateArray().Select(x => x.GetString()).ToHashSet();
        foreach (var referral in SourceReferrals(proposal))
            if (referral.Disposition == "outside-appetite" || referral.Disposition == "carrier-required" || !reviewCodes.Contains(referral.Dimension)) result.Add(referral.Requirement);
        // Whole-book district exposure is assessed atomically at issue in 08-08/09.
        // This predicate is intentionally insufficient permission to issue.
        return result.Distinct().ToArray();
    }

    internal static void RequireProduct(JsonElement proposal)
    {
        if (proposal.ValueKind != JsonValueKind.Object || !proposal.TryGetProperty("productCode", out var code) || code.GetString() != "commercial-combined")
            throw new ArgumentException("Commercial Combined proposal required.");
    }
    internal static JsonElement Answer(JsonElement container, string id) => container.GetProperty("answers").EnumerateArray().Single(x => x.GetProperty("questionId").GetString() == id).GetProperty("value");
    private static int Reference(JsonElement container, string id) => Answer(container, id).GetProperty("value").GetInt32();
    private static decimal Amount(JsonElement owner, string name) => decimal.Parse(owner.GetProperty(name).GetString()!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}
