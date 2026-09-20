using System.Text.Json;

namespace BackOffice.Application.Underwriting;

public sealed record CommercialProof(string Code, string Label, string Path, Guid? RiskItemId = null);

public static class CommercialEvidenceRules
{
    public static IReadOnlyList<CommercialProof> Requirements(JsonElement proposal)
    {
        CommercialReferralRules.RequireProduct(proposal);
        var risk = proposal.GetProperty("risk");
        var result = new List<CommercialProof> { new("cc-property-proof", "Property schedule and declared sums insured", "/risk/locations"),
            new("cc-liability-proof", "Liability activities and territory", "/risk/liability"),
            new("cc-claims-experience-proof", "Five year claims experience from the previous insurer", "/risk/losses"),
            new("cc-health-safety-proof", "Health and safety policy and risk assessments", "/risk/declarations") };
        foreach (var location in risk.GetProperty("locations").EnumerateArray())
        {
            var id = location.GetProperty("id").GetGuid();
            result.Add(new("cc-location-proof", "Construction, protections and flood information: " + location.GetProperty("reference").GetString(),
                "/risk/locations", id));
            result.Add(new("cc-electrical-proof", "Electrical inspection certificate, with C1 and C2 defects rectified", "/risk/locations", id));
            result.Add(new("cc-alarm-proof", "Intruder alarm specification and maintenance contract", "/risk/locations", id));
            if (CommercialReferralRules.Answer(risk.GetProperty("declarations"), "prototype.quote.be47c08f530f").GetBoolean())
                result.Add(new("cc-structural-proof", "Structural survey for requested subsidence cover", "/risk/locations", id));
        }
        if (CommercialReferralRules.Answer(risk.GetProperty("declarations"), "prototype.quote.36ef01068295").GetBoolean())
            foreach (var wage in risk.GetProperty("wages").EnumerateArray())
                result.Add(new("cc-wage-proof", "Wage roll: " + wage.GetProperty("category").GetProperty("label").GetString(), "/risk/wages", wage.GetProperty("id").GetGuid()));
        if (CommercialReferralRules.Answer(proposal.GetProperty("cover").GetProperty("responses"), "prototype.quote.7660fc5eb42e").GetBoolean())
            result.Add(new("cc-bi-proof", "Business interruption basis, indemnity period and dependencies", "/risk/businessInterruption"));
        if (CommercialReferralRules.SourceReferrals(proposal).Any(x => x.Dimension is "business-history" or "loss-history") ||
            CommercialReferralRules.Answer(risk.GetProperty("declarations"), "prototype.quote.0b0b63fca324").GetBoolean() ||
            risk.TryGetProperty("losses", out var losses) && losses.GetArrayLength() > 0)
            result.Add(new("cc-business-proof", "Business declarations and loss history", "/risk/business"));
        return result;
    }

    public static ReferralCondition Condition(JsonElement value, JsonElement proposal)
    {
        CommercialReferralRules.RequireProduct(proposal);
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("code", out var codeValue) || codeValue.ValueKind != JsonValueKind.String)
            throw new ArgumentException("A closed Commercial Combined evidence condition is required.");
        var code = codeValue.GetString()!;
        var targeted = code is "provide-cc-location-proof" or "provide-cc-wage-proof" or "provide-cc-electrical-proof" or "provide-cc-alarm-proof" or "provide-cc-structural-proof";
        var keys = value.EnumerateObject().Select(x => x.Name).ToArray();
        var expected = targeted ? new[] { "code", "riskItemId" } : ["code"];
        if (keys.Length != expected.Length || keys.Distinct().Count() != keys.Length || expected.Except(keys).Any() || !code.StartsWith("provide-cc-", StringComparison.Ordinal))
            throw new ArgumentException("A closed Commercial Combined evidence condition is required.");
        Guid? id = null;
        if (targeted)
        {
            if (!value.GetProperty("riskItemId").TryGetGuid(out var parsed) || parsed == Guid.Empty) throw new ArgumentException("An owned evidence subject is required.");
            id = parsed;
        }
        var purpose = Requirements(proposal).SingleOrDefault(x => x.Code == code[8..] && x.RiskItemId == id)
            ?? throw new ArgumentException("The evidence subject must exist in the current Commercial Combined proposal.");
        return new(code, "documentary", value.GetRawText(), purpose.Label, null, id is Guid target ? [target] : [], purpose.Code);
    }
}
