using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Domain;

namespace BackOffice.Application.Underwriting;

public sealed record ReferralCondition(string Code, string Kind, string DefinitionJson, string Wording,
    string? EndorsementCode, IReadOnlyList<Guid> TargetIds, string? RequirementCode, Guid? TermsVersionId = null,
    string? TermsHash = null, int? MinimumYears = null);

// Pure typed rules. Current SQL ownership, authority grants, latest decision and
// review identity are checked by the command boundary before these predicates.
public static class ReferralRules
{
    public static ReferralCondition Condition(JsonElement value, JsonElement proposal)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        string Text(string name)
        {
            if (!value.TryGetProperty(name, out var item) || item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())) throw Invalid();
            return item.GetString()!;
        }
        var code = Text("code");
        string[] fields = code switch {
            "provide-driver-proof" => ["code", "driverId", "requirementCode"],
            "provide-premises-security" => ["code", "premisesId"],
            "provide-signed-statement" => ["code", "termsVersionId", "termsHash"],
            "provide-trading-history" => ["code"],
            "overnight-security" => ["code", "premisesId", "wordingVersion"],
            "named-drivers-only" => ["code", "driverIds", "wordingVersion"],
            "any-driver-minimum-licence" => ["code", "minimumYears", "wordingVersion"],
            "revise-stock-limit" => ["code", "maximumAmount"],
            "revise-vehicle-limit" => ["code", "vehicleId", "maximumAmount"],
            _ => throw Invalid()
        };
        var keys = value.EnumerateObject().Select(x => x.Name).ToArray();
        if (keys.Length != fields.Length || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length || fields.Except(keys).Any()) throw Invalid();
        Guid Id(string key)
        {
            if (!Guid.TryParseExact(Text(key), "D", out var id) || id == Guid.Empty) throw Invalid();
            return id;
        }
        JsonElement[] Items(string collection)
        {
            if (!proposal.TryGetProperty("risk", out var risk) || !risk.TryGetProperty(collection, out var items) || items.ValueKind != JsonValueKind.Array) return [];
            return items.EnumerateArray().ToArray();
        }
        JsonElement Target(Guid id, string collection)
        {
            var matches = Items(collection).Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("id", out var key) && key.ValueKind == JsonValueKind.String && key.TryGetGuid(out var actual) && actual == id).ToArray();
            if (matches.Length != 1) throw Invalid(); return matches[0];
        }
        var targets = new List<Guid>(); string kind = "documentary", wording = "", requirement = code; string? endorsement = null;
        Guid? terms = null; string? termsHash = null; int? minimumYears = null;
        if (fields.Contains("wordingVersion") && Text("wordingVersion") != "1") throw Invalid();
        if (fields.Contains("maximumAmount"))
        {
            try { if (Money.Parse(Text("maximumAmount")).Pence <= 0) throw Invalid(); }
            catch (Exception error) when (error is FormatException or OverflowException) { throw Invalid(); }
            kind = "risk-change";
        }
        if (fields.Contains("driverId")) { var id = Id("driverId"); Target(id, "drivers"); targets.Add(id); }
        if (fields.Contains("premisesId")) { var id = Id("premisesId"); Target(id, "premises"); targets.Add(id); }
        if (fields.Contains("vehicleId")) { var id = Id("vehicleId"); Target(id, "vehicles"); targets.Add(id); }
        switch (code)
        {
            case "provide-driver-proof":
                requirement = Text("requirementCode");
                if (requirement is not ("photocard-both-sides" or "driving-record")) throw Invalid();
                break;
            case "provide-premises-security": requirement = "premises-security"; break;
            case "provide-trading-history": requirement = "trading-history"; break;
            case "provide-signed-statement":
                terms = Id("termsVersionId"); termsHash = Text("termsHash");
                if (!Hash(termsHash)) throw Invalid(); requirement = "signed-statement"; break;
            case "overnight-security":
                kind = "warranty"; endorsement = "W-07"; requirement = "warranty-acknowledgement";
                wording = "Vehicles kept at the declared secured premises overnight."; break;
            case "named-drivers-only":
                var ids = value.GetProperty("driverIds");
                if (ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() is < 1 or > 100) throw Invalid();
                var names = new List<string>();
                foreach (var item in ids.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || !item.TryGetGuid(out var id) || id == Guid.Empty || targets.Contains(id)) throw Invalid();
                    var driver = Target(id, "drivers"); targets.Add(id);
                    // Use retained captured names only; IDs remain the applicability keys.
                    string Name(string field) => driver.TryGetProperty(field, out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()!.Trim() : "";
                    var name = (Name("firstName") + " " + Name("surname")).Trim();
                    if (string.IsNullOrWhiteSpace(name)) throw Invalid(); names.Add(name);
                }
                kind = "warranty"; endorsement = "named-drivers-only"; requirement = "warranty-acknowledgement";
                wording = "Driving is restricted to the following named drivers: " + string.Join(", ", names) + "."; break;
            case "any-driver-minimum-licence":
                if (value.GetProperty("minimumYears").ValueKind != JsonValueKind.Number || !value.GetProperty("minimumYears").TryGetInt32(out var years) || years is < 1 or > 80) throw Invalid();
                minimumYears = years; kind = "warranty"; endorsement = code; requirement = "warranty-acknowledgement";
                wording = $"Any authorised driver must have held a full driving licence for at least {years.ToString(CultureInfo.InvariantCulture)} complete years."; break;
        }
        if (wording.Length > 8000) throw Invalid();
        return new(code, kind, value.GetRawText(), wording, endorsement, targets.AsReadOnly(), kind == "risk-change" ? null : requirement, terms, termsHash, minimumYears);
    }

    public static IReadOnlyList<UnderwritingRequirement> AuthorityBlockers(JsonElement actor, JsonElement binder,
        UnderwritingRisk risk, IReadOnlyList<ReferralCondition> conditions, int minimumTradingYears = 5)
    {
        var issues = UnderwritingRules.AssessAuthority(actor, risk, minimumTradingYears)
            .Concat(UnderwritingRules.AssessAuthority(binder, risk, minimumTradingYears));
        var minimum = Math.Max(actor.GetProperty("limits").GetProperty("minimumLicenceYears").GetInt32(), binder.GetProperty("limits").GetProperty("minimumLicenceYears").GetInt32());
        var licensed = conditions.Any(x => x.Code == "any-driver-minimum-licence" && x.Kind == "warranty" && x.MinimumYears >= minimum);
        return issues.Where(x => !(licensed && x.RuleCode == "any-driver-licence-years")).Distinct().ToArray();
    }

    public static bool CanResolveWithEvidence(ReferralCondition condition) => condition.Kind is "documentary" or "warranty";
    public static bool EvidenceSatisfied(string screening, string review, bool withdrawn, string expected, string actual) =>
        screening == "accepted" && review == "accepted" && !withdrawn && Hash(expected) && Hash(actual) &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual));
    public static bool Hash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static ArgumentException Invalid() => new("A closed condition with current risk targets is required.");
}
