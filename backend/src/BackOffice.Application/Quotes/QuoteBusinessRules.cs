using System.Text.Json;

namespace BackOffice.Application.Quotes;

// Section readiness after strict shape/catalogue validation. Missing or
// contradictory answers never prevent saving a valid-shaped partial draft.
public static class QuoteBusinessRules
{
    private static readonly Lazy<JsonElement[]> Mappings = new(() =>
    {
        _ = QuoteCatalogueIdentity.Version; // Validate the bundled catalogues first.
        using var stream = typeof(QuoteBusinessRules).Assembly.GetManifestResourceStream("QuoteCapture.Questions")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("mappings").EnumerateArray().Select(x => x.Clone()).ToArray();
    });
    private static readonly string[] SplitKeys = ["sales", "servicing", "mechanicalRepair", "breakdownRecovery", "bodyRepairs", "valeting", "other"];
    private static IEnumerable<string> Owners(string section, params int[] numbers) => numbers.Select(n => $"MTS-{section}-Q{n:00}");
    private static readonly string[] Required = [.. Owners("01", 6, 7, 8, 9, 12, 15, 16, 17, 20),
        .. Owners("03", 1, 2, 3, 4, 6, 8, 9), .. Owners("12", 1, 3, 5, 7, 9, 11, 13, 15, 17, 19), "MTS-02-Q01"];

    public static IReadOnlyList<QuoteFieldIssue> Assess(JsonElement proposal)
    {
        var product = At(proposal, "productCode");
        if (product.ValueKind != JsonValueKind.String || product.GetString() is not ("motor-trade-road-risks" or "motor-trade-combined"))
            return [new("unsupported-capture-product", "/productCode")];
        var rows = Mappings.Value.Where(row => row.GetProperty("products").EnumerateArray().Any(x => x.GetString() == product.GetString())).ToArray();
        var issues = new List<QuoteFieldIssue>();
        void Add(string code, string path) { if (issues.Count < QuoteCaptureShape.MaximumIssues) issues.Add(new(code, path)); }
        JsonElement Row(string owner) => rows.First(x => At(x, "owner").GetString() == owner);
        (JsonElement Value, string Path) Read(string owner)
        {
            var row = Row(owner); var path = row.GetProperty("canonicalPath").GetString()!;
            return row.GetProperty("contractKind").GetString() == "Answer"
                ? Answer(proposal, path[..^10], row.GetProperty("questionId").GetString()!)
                : (At(proposal, path), "/" + path.Replace('.', '/'));
        }
        void Require(string owner) { var field = Read(owner); if (!Present(field.Value)) Add("required-capture-field", field.Path); }
        foreach (var owner in Required) Require(owner);
        if (QuoteCatalogueIdentity.TrustedValue(Read("MTS-01-Q06").Value, "companyTypes") is 2 or 3 or 4)
        {
            Require("MTS-01-Q11"); var name = Read("MTS-01-Q11");
            if (name.Value.ValueKind == JsonValueKind.String && name.Value.GetString()!.Length > 50) Add("company-name-too-long", name.Path);
        }
        var telephone = Read("MTS-01-Q13"); var mobile = Read("MTS-01-Q14");
        if (!Present(telephone.Value) && !Present(mobile.Value)) Add("contact-number-required", "/insured/contact");
        foreach (var field in new[] { telephone, mobile })
            if (Present(field.Value) && field.Value.GetString()!.Length > 11) Add("contact-number-too-long", field.Path);
        if (Present(mobile.Value) && !mobile.Value.GetString()!.StartsWith("07", StringComparison.Ordinal)) Add("mobile-prefix-invalid", mobile.Path);
        var tradingFrom = QuoteCatalogueIdentity.TrustedValue(Read("MTS-02-Q01").Value, "tradingFroms");
        var premises = Items(At(proposal, "risk.premises")).ToArray();
        if (tradingFrom is 3 or 4)
        {
            if (premises.Length == 0) Add("premises-required", "/risk/premises");
            for (var index = 0; index < premises.Length; index++)
                foreach (var owner in Owners("02", 2, 3, 4, 5, 6, 7, 10))
                {
                    var path = Row(owner).GetProperty("canonicalPath").GetString()!["risk.premises[].".Length..];
                    if (!Present(At(premises[index], path))) Add("required-capture-field", $"/risk/premises/{index}/{path.Replace('.', '/')}");
                }
        }
        else if (premises.Length > 0) Add(tradingFrom is null ? "premises-context-required" : "inactive-premises-retained", "/risk/premises");
        var handled = Read("MTS-03-Q08");
        if (Number(handled.Value) is < 1) Add("vehicles-handled-minimum", handled.Path);
        var started = Read("MTS-03-Q01");
        if (Present(started.Value) && string.CompareOrdinal(started.Value.GetString(), "1900-01-01") < 0) Add("business-start-too-early", started.Path);
        foreach (var row in rows.Where(x => At(x, "requiredWhen").ValueKind == JsonValueKind.Object &&
            (At(x, "owner").GetString()!.StartsWith("MTS-03-", StringComparison.Ordinal) || At(x, "owner").GetString()!.StartsWith("MTS-12-", StringComparison.Ordinal))))
        {
            var scope = row.GetProperty("canonicalPath").GetString()![..^10];
            var condition = row.GetProperty("requiredWhen");
            var parent = Answer(proposal, scope, condition.GetProperty("questionId").GetString()!);
            var child = Answer(proposal, scope, row.GetProperty("questionId").GetString()!);
            if (parent.Value.ValueKind != JsonValueKind.Undefined && JsonElement.DeepEquals(parent.Value, condition.GetProperty("equals")))
            { if (!Present(child.Value)) Add("conditional-answer-required", child.Path); }
            else if (child.Value.ValueKind != JsonValueKind.Undefined)
                Add(parent.Value.ValueKind == JsonValueKind.Undefined ? "controlling-answer-required" : "inactive-answer-retained", child.Path);
        }
        AssessSplit(proposal, Add);
        AssessPrototype(proposal, Add);
        AssessEntity(proposal, Add);
        AssessActivities(proposal, Add);
        AssessBusinessBounds(proposal, Add);
        return issues;
    }

    private static void AssessEntity(JsonElement proposal, Action<string, string> add)
    {
        var insured = At(proposal, "insured"); var names = Items(At(insured, "proposerNames")).ToArray();
        if (names.Length == 0) add("proposer-name-required", "/insured/proposerNames");
        for (var index = 0; index < names.Length; index++)
            if (!Present(names[index])) add("proposer-name-required", $"/insured/proposerNames/{index}");
        var entity = At(insured, "entityType");
        if (!Present(entity)) { add("legal-entity-required", "/insured/entityType"); return; }
        var company = QuoteCatalogueIdentity.TrustedValue(At(insured, "declaredCompanyType"), "companyTypes");
        if (company is null) add("legal-entity-company-context-required", "/insured/declaredCompanyType");
        else if (!(entity.GetString() switch {
            "sole-trader" => company == 1,
            "partnership" or "llp" => company == 4,
            "limited-company" => company is 2 or 3,
            _ => false }))
        {
            add("conflicting-legal-entity", "/insured/entityType");
            add("conflicting-legal-entity", "/insured/declaredCompanyType");
        }
        if (entity.GetString() is "limited-company" or "llp" && !Present(At(insured, "companyNumber")))
            add("incorporated-company-number-required", "/insured/companyNumber");
    }

    private static void AssessActivities(JsonElement proposal, Action<string, string> add)
    {
        const string path = "/risk/business/activities";
        var value = At(proposal, "risk.business.activities"); var activities = Items(value).ToArray();
        if (activities.Length == 0) add("business-activity-required", path);
        var codes = new HashSet<(string?, long)>(); long total = 0; var complete = activities.Length > 0;
        for (var index = 0; index < activities.Length; index++)
        {
            var activity = activities[index]; var code = At(activity, "code"); var share = Number(At(activity, "turnoverBasisPoints"));
            var itemPath = path + "/" + index;
            if (code.ValueKind == JsonValueKind.Undefined) add("business-activity-code-required", itemPath + "/code");
            else
            {
                if (!codes.Add((At(code, "collection").GetString(), At(code, "value").GetInt64()))) add("duplicate-business-activity", itemPath + "/code");
                if (activities.Length > 1 && QuoteCatalogueIdentity.RequiresCarJockeyRadius(code)) add("car-jockey-must-be-only-activity", itemPath + "/code");
            }
            if (share is null or < 100) add("business-activity-minimum-one-percent", itemPath + "/turnoverBasisPoints");
            if (share is null) complete = false; else total += share.Value;
        }
        if (value.ValueKind != JsonValueKind.Undefined)
        {
            if (!complete) add("activity-share-required", path);
            else if (total != 10000) add("activity-total-must-equal-100-percent", path);
        }
    }

    private static void AssessBusinessBounds(JsonElement proposal, Action<string, string> add)
    {
        var started = At(proposal, "risk.business.startedOn"); var policyStart = At(proposal, "termIntent.localStartDate");
        if (Present(started) && Present(policyStart) && string.CompareOrdinal(started.GetString(), policyStart.GetString()) > 0)
            add("business-start-after-policy", "/risk/business/startedOn");
        void Limit(JsonElement value, int maximum, string path)
        {
            if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > maximum) add("source-text-too-long", path);
        }
        void Address(JsonElement address, string path)
        {
            Limit(At(address, "postcode"), 10, path + "/postcode");
            foreach (var field in new[] { "houseNumber", "street", "town", "city", "county" }) Limit(At(address, field), 50, path + "/" + field);
        }
        Address(At(proposal, "insured.address"), "/insured/address"); var index = 0;
        foreach (var premise in Items(At(proposal, "risk.premises"))) Address(At(premise, "address"), $"/risk/premises/{index++}/address");
        var association = Answer(proposal, "risk.business.responses", "MTS-03-Q07"); Limit(association.Value, 20, association.Path);
        var facts = At(proposal, "risk.materialFacts");
        if (facts.ValueKind == JsonValueKind.String && facts.GetString()!.Length > 1000) add("material-facts-too-long", "/risk/materialFacts");
    }

    private static void AssessPrototype(JsonElement proposal, Action<string, string> add)
    {
        (JsonElement Value, string Path) Read(string id) => Answer(proposal, "risk.business.responses", id);
        void Require(string id) { var answer = Read(id); if (!Present(answer.Value)) add("required-prototype-business-answer", answer.Path); }
        long? Pinned(string id) => QuoteCatalogueIdentity.TrustedValue(Read(id).Value, id);
        if (!Present(At(proposal, "risk.business.description"))) add("business-description-required", "/risk/business/description");
        foreach (var suffix in new[] { "ea4580cbac7a", "46414cc10100", "ef70e80708bb", "36da21d3c935", "6c1927f561b8", "f7972c55f517", "382ce4de8253", "922ca15dc9ed" })
        {
            var id = "prototype.quote." + suffix; Require(id);
            if (Read(id).Value.ValueKind == JsonValueKind.True && !Present(At(proposal, "risk.materialFacts")))
                add("declaration-material-facts-required", "/risk/materialFacts");
        }
        const string trader = "prototype.quote.c6181a11c34c", experience = "prototype.quote.0552d5a68ba2",
            employment = "prototype.quote.34613c23e95d", occupation = "prototype.quote-value.3fd9edd7e66e";
        foreach (var id in new[] { trader, experience, employment }) Require(id);
        if (Pinned(trader) == 2)
        {
            Require(occupation);
            if (Pinned(employment) == 1) add("part-time-employment-required", Read(employment).Path);
        }
        else if (Pinned(trader) == 1)
        {
            if (Present(Read(occupation).Value)) add("inactive-main-occupation-retained", Read(occupation).Path);
            if (Pinned(employment) is 2 or 3) add("inactive-main-employment-retained", Read(employment).Path);
        }
        else if (Present(Read(occupation).Value)) add("main-occupation-context-required", Read(occupation).Path);
        foreach (var (parents, details) in new (string[], string)[] {
            (["ba9d4158ae2c", "35350a32e79e", "2ad460339240", "f5e77ab8ec93", "7fe8e3151553", "27322dcfabf5", "f856f5891026"], "909e1c6eff8c"),
            (["d7a75768e505", "5f9e8331ac6f", "488ecf4bdc09", "87fad6b4a9fe"], "2d662a3ec81d") })
        {
            var ids = parents.Select(x => "prototype.quote." + x).ToArray(); var detail = "prototype.quote-value." + details;
            foreach (var id in ids) Require(id);
            if (ids.Any(id => Read(id).Value.ValueKind == JsonValueKind.True)) Require(detail);
            else if (Present(Read(detail).Value)) add(ids.All(id => Read(id).Value.ValueKind == JsonValueKind.False)
                ? "inactive-prototype-details-retained" : "prototype-details-context-required", Read(detail).Path);
        }
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var activity in Items(At(proposal, "risk.business.activities")))
        {
            var value = QuoteCatalogueIdentity.TrustedValue(At(activity, "code"), "mtOccupations");
            if (value is null || !(Number(At(activity, "turnoverBasisPoints")) is > 0)) continue;
            foreach (var (values, suffix) in new (long[], string)[] {
                ([5, 6, 16, 17], "35350a32e79e"), ([13], "2ad460339240"), ([23, 29], "ba9d4158ae2c") })
            {
                var id = "prototype.quote." + suffix;
                if (values.Contains(value.Value) && Read(id).Value.ValueKind != JsonValueKind.True && emitted.Add(id))
                    add("activity-declaration-required", Read(id).Path);
            }
        }
    }

    private static void AssessSplit(JsonElement proposal, Action<string, string> add)
    {
        const string path = "/risk/business/declaredActivitySplit";
        var business = At(proposal, "risk.business"); var split = At(business, "declaredActivitySplit");
        var shares = SplitKeys.ToDictionary(key => key, key => Number(At(split, key)), StringComparer.Ordinal);
        foreach (var key in SplitKeys) if (shares[key] is null) add("activity-split-share-required", path + "/" + key);
        if (shares.Values.All(x => x is not null) && shares.Values.Sum(x => x!.Value) != 10000) add("activity-split-total-invalid", path);
        var detail = Answer(proposal, "risk.business.responses", "prototype.quote-value.3e4fdf2b682e");
        if (shares["other"] is > 0 && !Present(detail.Value)) add("other-activity-description-required", detail.Path);
        if (detail.Value.ValueKind != JsonValueKind.Undefined && !(shares["other"] is > 0))
            add(shares["other"] is null ? "other-activity-share-context-required" : "inactive-other-activity-description", detail.Path);
        foreach (var (keys, occupations) in new (string[], long[])[] {
            (["sales"], [5, 6, 7, 8, 23]), (["servicing", "mechanicalRepair"], [16, 17, 18]),
            (["breakdownRecovery"], [2, 3]), (["bodyRepairs"], [10]), (["valeting"], [26, 27]) })
        {
            var minimum = Items(At(business, "activities")).Where(activity =>
                QuoteCatalogueIdentity.TrustedValue(At(activity, "code"), "mtOccupations") is { } value && occupations.Contains(value))
                .Sum(activity => Number(At(activity, "turnoverBasisPoints")) ?? 0);
            if (keys.All(key => shares[key] is not null) && minimum > keys.Sum(key => shares[key]!.Value))
                add("activity-split-below-declared-occupations", path);
        }
    }

    private static bool Present(JsonElement value) => value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) &&
        (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));
    private static long? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
    private static IEnumerable<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    private static JsonElement At(JsonElement value, string path)
    {
        foreach (var key in path.Split('.'))
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out value)) return default;
        return value;
    }
    private static (JsonElement Value, string Path) Answer(JsonElement proposal, string scope, string question)
    {
        var path = "/" + scope.Replace('.', '/') + "/answers"; var index = 0;
        foreach (var answer in Items(At(proposal, scope + ".answers")))
        {
            if (At(answer, "questionId").GetString() == question) return (At(answer, "value"), path + $"/{index}/value");
            index++;
        }
        return (default, path);
    }
}
