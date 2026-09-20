using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;

namespace BackOffice.Application.Policies;

public static class CommercialServicingProposalRules
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private static readonly string[] PropertyFields = ["buildings", "contents", "stock", "maximumEstimatedLoss"];
    public static ServicingProposalAssessment Assess(string snapshotJson, string proposalJson, ServicingProposalContext context)
    {
        if (context.PolicyId == Guid.Empty || context.BaseVersionId == Guid.Empty || context.StartsAt >= context.EndsAt)
            throw new ArgumentException("Trusted commercial policy context is required.");
        if (context.CoverageTerm is { } coverage && (coverage.StartsAt != context.StartsAt || coverage.EndsAt != context.EndsAt))
            throw new ArgumentException("Coverage and projection bounds must agree.");
        var canonical = ServicingProposalInput.Parse(proposalJson, context.BaseVersionId);
        var envelope = JsonNode.Parse(canonical.Json)!.AsObject(); var source = JsonNode.Parse(snapshotJson)!.AsObject();
        if (source["productCode"]!.GetValue<string>() != CommercialCaptureRules.ProductCode) Fail("servicing-change-product-mismatch", "/productCode");
        var changes = envelope["changes"]!.AsArray();
        if (changes.Any(x => !x!["kind"]!.GetValue<string>().StartsWith("commercial-", StringComparison.Ordinal))) Fail("servicing-change-product-mismatch", "/changes");
        var capture = Capture(source, context); var original = Element(capture); var initialIdentities = Identities(capture);
        var everUsed = initialIdentities.Keys.ToHashSet(); var client = Id(source["insured"]!.AsObject(), "clientId");
        var issues = new List<QuoteFieldIssue>(); var common = Resolve(envelope["commonEffectiveIntent"]!, "/commonEffectiveIntent", issues);
        var dated = new List<(JsonObject Change, DateTimeOffset? At, int Index)>(); var targets = new HashSet<(string, Guid, DateTimeOffset?)>();
        void Bounds(DateTimeOffset instant, string path)
        {
            if (instant < context.StartsAt || instant >= context.EndsAt) issues.Add(new("effective-outside-term", path));
            if (instant < context.LatestIssuedEffectiveAt) issues.Add(new("effective-before-latest-issued-slice", path));
            if (instant < context.Now && !context.CanBackdate) issues.Add(new("senior-backdate-authority-required", path));
        }
        if (common is { } from) Bounds(from, "/commonEffectiveIntent");
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index]!.AsObject(); var kind = change["kind"]!.GetValue<string>();
            var at = change["effectiveIntent"] is { } intent ? Resolve(intent, $"/changes/{index}/effectiveIntent", issues) : common;
            var targetKind = kind is "commercial-location" or "commercial-property" ? "commercial-location" : kind;
            if (!targets.Add((targetKind, Id(change,"riskItemId"), at))) Fail("conflicting-target-change", $"/changes/{index}/riskItemId");
            if (change["effectiveIntent"] is not null && envelope["dateBasis"]?.GetValue<string>() != "per-cover-change") issues.Add(new("shared-date-override-forbidden", $"/changes/{index}/effectiveIntent"));
            if (at is { } instant)
            {
                Bounds(instant, $"/changes/{index}/effectiveIntent");
                if (common is { } start && instant < start) issues.Add(new("cover-date-before-common", $"/changes/{index}/effectiveIntent"));
            }
            dated.Add((change, at, index));
        }
        var datesValid = issues.Count == 0; var slices = new List<ServicingProposalSlice>();
        foreach (var group in dated.OrderBy(x => x.At ?? context.StartsAt).ThenBy(x => x.Index).GroupBy(x => x.At))
        {
            foreach (var entry in group) Apply(capture, entry.Change, context.PolicyId, client, everUsed, $"/changes/{entry.Index}");
            Validate(capture, initialIdentities);
            var projected = Element(capture); issues.AddRange(Readiness(projected, context));
            if (datesValid && group.Key is { } at) slices.Add(new(at, projected, group.Select(x => Id(x.Change,"changeId")).ToArray()));
        }
        var result = Element(capture);
        if (changes.Count == 0) { Validate(capture, initialIdentities); issues.AddRange(Readiness(result, context)); }
        var material = QuoteRevisionDiff.Compare(Normalize(original), Normalize(result)).Where(x => x.Kind != "reordered").ToArray();
        return new(original, result, material, issues.Distinct().Take(100).ToArray(), slices);
    }

    private static JsonObject Capture(JsonObject source, ServicingProposalContext context)
    {
        var insured = source["insured"]!.DeepClone().AsObject(); insured.Remove("clientId"); insured.Remove("clientAgencyRelationshipId");
        var cover = source["cover"]!.DeepClone().AsObject(); cover.Remove("sections"); cover.Remove("endorsements"); cover.Remove("warranties");
        var start = TimeZoneInfo.ConvertTime(context.StartsAt, London); var end = TimeZoneInfo.ConvertTime(context.EndsAt, London);
        var kind = context.CoverageTerm?.Kind ?? source["term"]!["kind"]!.GetValue<string>();
        var term = new JsonObject { ["kind"] = kind, ["timeZone"] = "Europe/London", ["localStartDate"] = start.ToString("yyyy-MM-dd"), ["localStartTime"] = start.ToString("HH:mm"), ["utcOffsetMinutes"] = (int)start.Offset.TotalMinutes };
        if (kind == "short-period") { term["localEndDate"] = end.ToString("yyyy-MM-dd"); term["localEndTime"] = end.ToString("HH:mm"); term["endUtcOffsetMinutes"] = (int)end.Offset.TotalMinutes; }
        return new() { ["schemaVersion"] = "1.0", ["format"] = CommercialCaptureRules.Format, ["productCode"] = CommercialCaptureRules.ProductCode,
            ["insured"] = insured, ["risk"] = source["risk"]!.DeepClone(), ["cover"] = cover, ["termIntent"] = term };
    }

    private static void Apply(JsonObject capture, JsonObject change, Guid policy, Guid client, HashSet<Guid> used, string path)
    {
        var kind = change["kind"]!.GetValue<string>(); var operation = change["operation"]!.GetValue<string>(); var id = Id(change,"riskItemId");
        var risk = capture["risk"]!.AsObject(); var payload = change["payload"] as JsonObject; var replace = change["payloadMode"]?.GetValue<string>() == "replace";
        var collection = kind switch { "commercial-location" or "commercial-property" => "locations", "commercial-wage" => "wages", "commercial-loss" => "losses", _ => null };
        if (collection is not null)
        {
            var rows = risk[collection] as JsonArray ?? []; if (risk[collection] is null) risk[collection] = rows;
            var existing = rows.OfType<JsonObject>().SingleOrDefault(x => Id(x,"id") == id);
            if (operation == "add")
            {
                if (!used.Add(id) || Identities(capture).ContainsKey(id)) Fail("duplicate-or-reused-item-id", path + "/riskItemId");
                var added = payload!.DeepClone().AsObject(); added["id"] = id.ToString(); rows.Add(added);
            }
            else
            {
                if (existing is null) Fail("foreign-risk-item", path + "/riskItemId");
                if (operation == "remove") rows.Remove(existing);
                else if (kind == "commercial-property")
                {
                    if (replace) foreach (var field in PropertyFields) existing!.Remove(field);
                    Merge(existing!, payload!);
                }
                else Update(existing!, payload!, replace);
            }
            return;
        }
        if (operation != "update" || id != (kind == "commercial-insured" ? client : policy)) Fail("invalid-singleton-operation", path);
        if (kind == "commercial-declarations")
        {
            if (replace) { risk.Remove("declarations"); risk.Remove("materialFacts"); }
            Merge(risk, payload!); return;
        }
        var name = kind switch { "commercial-business" => "business", "commercial-bi" => "businessInterruption", "commercial-liability" => "liability", "commercial-cover" => "cover", "commercial-insured" => "insured", _ => throw new QuoteInputException("servicing-change-product-mismatch") };
        var owner = kind is "commercial-cover" or "commercial-insured" ? capture : risk;
        if (owner[name] is not JsonObject) owner[name] = new JsonObject(); Update(owner[name]!.AsObject(), payload!, replace);
    }
    private static void Update(JsonObject target, JsonObject payload, bool replace)
    {
        if (replace) { var id = target["id"]?.DeepClone(); target.Clear(); if (id is not null) target["id"] = id; }
        Merge(target,payload);
    }
    private static void Merge(JsonObject target, JsonObject payload)
    {
        foreach (var (key,value) in payload)
            if (value is JsonObject child && target[key] is JsonObject current) Merge(current,child); else target[key] = value!.DeepClone();
    }
    private static void Validate(JsonObject value, Dictionary<Guid,string> original)
    {
        var issues = CommercialCaptureRules.ValidateShapeAndIdentity(Element(value)).ToList();
        foreach (var (id,path) in Identities(value)) if (original.TryGetValue(id,out var before) && before != path) issues.Add(new("item-owner-changed",path));
        if (issues.Count > 0) throw new QuoteValidationException(issues.Take(100).ToArray());
    }
    private static IEnumerable<QuoteFieldIssue> Readiness(JsonElement value, ServicingProposalContext context)
        => CommercialCaptureReadiness.Assess(value,DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.Now,London).DateTime)).Select(x => new QuoteFieldIssue(x.Code,x.Path,x.QuestionId));
    private static DateTimeOffset? Resolve(JsonNode value,string path,List<QuoteFieldIssue> issues)
    {
        var result = QuoteTerm.ResolveLondonTime(value["localDate"]!.GetValue<string>(),value["localTime"]!.GetValue<string>(),value["utcOffsetMinutes"]?.GetValue<int>());
        if (result.Code is { } code) issues.Add(new(code,path)); return result.Instant;
    }
    private static Dictionary<Guid,string> Identities(JsonNode root)
    {
        var result = new Dictionary<Guid,string>();
        void Walk(JsonNode? node,string path)
        {
            if (node is JsonObject obj)
            {
                if (obj["id"] is { } identityNode && (!Guid.TryParse(identityNode.GetValue<string>(),out var id) || id == Guid.Empty || !result.TryAdd(id,path))) Fail("duplicate-or-invalid-item-id",path);
                foreach (var (key,value) in obj) Walk(value,path+"/"+key);
            }
            else if (node is JsonArray rows) foreach (var child in rows) Walk(child,path+"/"+(child is JsonObject item && item["id"] is { } identity ? identity.GetValue<string>().ToLowerInvariant() : "item"));
        }
        Walk(root,""); return result;
    }
    private static JsonElement Normalize(JsonElement element)
    {
        JsonNode? Sort(JsonNode? value) => value switch {
            JsonObject obj => new JsonObject(obj.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>KeyValuePair.Create(x.Key,Sort(x.Value)))),
            JsonArray rows when rows.All(x=>x is JsonObject item && item["questionId"] is not null) => new JsonArray(rows.OrderBy(x=>x!["questionId"]!.GetValue<string>(),StringComparer.Ordinal).Select(Sort).ToArray()),
            JsonArray rows => new JsonArray(rows.Select(Sort).ToArray()), _ => value?.DeepClone() };
        return Element(Sort(JsonNode.Parse(element.GetRawText()))!);
    }
    private static Guid Id(JsonObject value,string key) => Guid.Parse(value[key]!.GetValue<string>());
    private static JsonElement Element(JsonNode value) => JsonSerializer.SerializeToElement(value);
    private static void Fail(string code,string path) => throw new QuoteValidationException([new(code,path)]);
}
