using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record ServicingProposalContext(Guid PolicyId, Guid BaseVersionId, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, DateTimeOffset LatestIssuedEffectiveAt, DateTimeOffset Now, bool CanBackdate, ResolvedQuoteTerm? CoverageTerm = null);
public sealed record ServicingProposalSlice(DateTimeOffset EffectiveAt, JsonElement Proposed, IReadOnlyList<Guid> ChangeIds);
public sealed record ServicingProposalAssessment(JsonElement Base, JsonElement Proposed, IReadOnlyList<QuoteRevisionChange> Changes,
    IReadOnlyList<QuoteFieldIssue> ReadinessIssues, IReadOnlyList<ServicingProposalSlice> Slices);

// Pure projection of an already scoped immutable base. No mutation, current
// authority or issued premium is inferred from this result. Invalid-shaped or
// foreign identities fail capture; incomplete declarations/dates block readiness.
public static class ServicingProposalRules
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public static ServicingProposalAssessment Assess(string snapshotJson, string proposalJson, ServicingProposalContext context)
    {
        if (context.PolicyId == Guid.Empty || context.StartsAt >= context.EndsAt) throw new ArgumentException("Trusted policy context is required.");
        if (context.CoverageTerm is { } coverage)
        {
            if(coverage.StartsAt!=context.StartsAt || coverage.EndsAt!=context.EndsAt)throw new ArgumentException("Coverage and projection bounds must agree.");
            _=QuoteRatingRules.CivilDuration(coverage);
        }
        var canonical = ServicingProposalInput.Parse(proposalJson, context.BaseVersionId);
        var envelope = JsonNode.Parse(canonical.Json)!.AsObject(); var snapshot = JsonNode.Parse(snapshotJson)!.AsObject();
        var capture = Capture(snapshot, context); var original = Element(capture);
        var sourceIdentities = Identities(capture); var sourceClient = Guid.Parse(snapshot["insured"]!["clientId"]!.GetValue<string>());
        var issues = new List<QuoteFieldIssue>(); var dates = new List<(JsonObject Change, DateTimeOffset? Effective, int Index)>();
        var common = Resolve(envelope["commonEffectiveIntent"]!, "/commonEffectiveIntent", issues);
        var duplicate = new HashSet<(string Kind, Guid Id, DateTimeOffset? Instant)>();
        bool? specifiedRequired = null;
        var changes = envelope["changes"]!.AsArray();
        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i]!.AsObject(); var kind = change["kind"]!.GetValue<string>();
            if (change["specifiedVehicle"] is { } selection)
            {
                var required = selection["required"]!.GetValue<bool>();
                if (specifiedRequired is { } previous && previous != required) Fail("conflicting-specified-vehicle-declaration", $"/changes/{i}/specifiedVehicle/required");
                specifiedRequired = required;
            }
            var effective = change["effectiveIntent"] is { } intent ? Resolve(intent, $"/changes/{i}/effectiveIntent", issues) : common;
            if (!duplicate.Add((kind, Id(change, "riskItemId"), effective))) Fail("conflicting-target-change", $"/changes/{i}/riskItemId");
            if (change["effectiveIntent"] is not null && envelope["dateBasis"]?.GetValue<string>() != "per-cover-change")
                issues.Add(new("shared-date-override-forbidden", $"/changes/{i}/effectiveIntent"));
            if (effective is { } instant)
            {
                var path = $"/changes/{i}/effectiveIntent";
                if (instant < context.StartsAt || instant >= context.EndsAt) issues.Add(new("effective-outside-term", path));
                if (instant < context.LatestIssuedEffectiveAt) issues.Add(new("effective-before-latest-issued-slice", path));
                if (common is { } from && instant < from) issues.Add(new("cover-date-before-common", path));
                if (instant < context.Now)
                {
                    if (kind == "driver") issues.Add(new("driver-backdate-forbidden", path));
                    else if (!context.CanBackdate) issues.Add(new("senior-backdate-authority-required", path));
                }
            }
            dates.Add((change, effective, i));
        }
        if (common is { } requested && (requested < context.StartsAt || requested >= context.EndsAt)) issues.Add(new("effective-outside-term", "/commonEffectiveIntent"));
        if (common is { } ordered && ordered < context.LatestIssuedEffectiveAt) issues.Add(new("effective-before-latest-issued-slice", "/commonEffectiveIntent"));
        var datesValid = issues.Count == 0; var slices = new List<ServicingProposalSlice>();
        foreach (var group in dates.OrderBy(x => x.Effective ?? context.StartsAt).ThenBy(x => x.Index).GroupBy(x => x.Effective))
        {
            foreach (var entry in group) Apply(capture, entry.Change, context.PolicyId, sourceClient, $"/changes/{entry.Index}");
            var projected = Element(capture);
            var invalid = QuoteCaptureShape.Validate(projected)
                .Concat(QuoteCatalogueIdentity.ValidateQuestions(projected)).Concat(QuoteCatalogueIdentity.ValidateReferences(projected))
                .Concat(QuoteItemIdentity.Validate(projected)).ToList();
            var identities = Identities(capture);
            foreach (var (id, path) in identities)
                if (sourceIdentities.TryGetValue(id, out var originalPath) && path != originalPath) invalid.Add(new("item-owner-changed", path));
            CheckPremisesLinks(capture, invalid);
            if (invalid.Count > 0) throw new QuoteValidationException(invalid.Take(100).ToArray());
            issues.AddRange(Completeness(projected, context));
            if (datesValid && group.Key is { } at) slices.Add(new(at, projected, group.Select(x => Id(x.Change, "changeId")).ToArray()));
        }
        var result = Element(capture);
        if (changes.Count == 0) issues.AddRange(Completeness(result, context));
        var material = QuoteRevisionDiff.Compare(Normalize(original), Normalize(result)).Where(x => x.Kind != "reordered").ToArray();
        return new(original, result, material, issues.Distinct().Take(100).ToArray(), slices);
    }

    private static IEnumerable<QuoteFieldIssue> Completeness(JsonElement proposal, ServicingProposalContext context)
    {
        var asOf = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.Now, London).DateTime);
        // Servicing captures vehicle facts manually. It cannot borrow a quote's
        // lookup authority or claim a fresh external lookup was performed.
        var risk = proposal.GetProperty("risk");
        var modes = risk.TryGetProperty("vehicles", out var vehicles)
            ? vehicles.EnumerateArray().ToDictionary(x => x.GetProperty("id").GetGuid(), _ => "manual") : [];
        var term = new QuoteTermAssessment(context.CoverageTerm ?? new("annual", context.StartsAt, context.EndsAt, "Europe/London"), []);
        var capture = QuoteReadiness.Assess(Guid.Empty, Guid.Empty, proposal, term, null, asOf, modes, includeEvidence: false);
        var premises = risk.TryGetProperty("premises", out var rows) ? rows.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray() : [];
        return capture.Issues.Select(x => new QuoteFieldIssue(x.Code, x.Path, x.QuestionId))
            .Concat(UnderwritingRules.ValidateSections(proposal.GetProperty("cover"), proposal.GetProperty("productCode").GetString()!, premises));
    }

    private static JsonObject Capture(JsonObject snapshot, ServicingProposalContext context)
    {
        var insured = snapshot["insured"]!.DeepClone().AsObject(); insured.Remove("clientId"); insured.Remove("clientAgencyRelationshipId");
        // Issued sections are calculated outputs, separate from requestedSections.
        // Retaining them here would expose premium authority and duplicate IDs.
        var cover = snapshot["cover"]!.DeepClone().AsObject(); cover.Remove("sections"); cover.Remove("endorsements"); cover.Remove("warranties");
        var risk = snapshot["risk"]!.DeepClone().AsObject(); risk.Remove("driverBasis");
        var start = TimeZoneInfo.ConvertTime(context.StartsAt, London); var end = TimeZoneInfo.ConvertTime(context.EndsAt, London);
        var kind = context.CoverageTerm?.Kind ?? snapshot["term"]!["kind"]!.GetValue<string>();
        var intent = new JsonObject { ["kind"] = kind, ["timeZone"] = "Europe/London", ["localStartDate"] = start.ToString("yyyy-MM-dd"),
            ["localStartTime"] = start.ToString("HH:mm"), ["utcOffsetMinutes"] = (int)start.Offset.TotalMinutes };
        if (kind == "short-period") { intent["localEndDate"] = end.ToString("yyyy-MM-dd"); intent["localEndTime"] = end.ToString("HH:mm"); intent["endUtcOffsetMinutes"] = (int)end.Offset.TotalMinutes; }
        return new() { ["schemaVersion"] = "1.0", ["productCode"] = snapshot["productCode"]!.DeepClone(), ["insured"] = insured,
            ["risk"] = risk, ["cover"] = cover, ["termIntent"] = intent };
    }

    private static void Apply(JsonObject capture, JsonObject change, Guid policy, Guid client, string path)
    {
        var kind = change["kind"]!.GetValue<string>(); var operation = change["operation"]!.GetValue<string>(); var id = Id(change, "riskItemId");
        var risk = capture["risk"]!.AsObject(); var payload = change["payload"] as JsonObject;
        var replace = change["payloadMode"]?.GetValue<string>() == "replace";
        if (kind == "vehicle" && change["specifiedVehicle"] is { } selection)
        {
            var selected = selection["selected"]!.GetValue<bool>();
            if (operation == "remove" && selected) Fail("removed-vehicle-cannot-be-specified", path + "/specifiedVehicle/selected");
            var references = risk["specifiedVehicleIds"] as JsonArray ?? [];
            var updated = selected ? references.DeepClone().AsArray()
                : new JsonArray(references.Where(item => Guid.Parse(item!.GetValue<string>()) != id).Select(item => item!.DeepClone()).ToArray());
            if (selected && !references.Any(item => Guid.Parse(item!.GetValue<string>()) == id)) updated.Add(id.ToString());
            risk["specifiedVehicleIds"] = updated; risk["specifiedVehiclesRequested"] = selection["required"]!.GetValue<bool>();
        }
        if (kind is "business" or "policyholder" || kind == "cover" && id == policy)
        {
            if (id != (kind == "policyholder" ? client : policy) || operation != "update") Fail("invalid-singleton-operation", path);
            var target = kind == "business" ? risk["business"]!.AsObject() : capture[kind == "policyholder" ? "insured" : "cover"]!.AsObject();
            Update(target, payload!, replace); return;
        }
        JsonArray rows;
        if (kind == "cover")
        {
            var cover = capture["cover"]!.AsObject(); rows = cover["requestedSections"] as JsonArray ?? [];
            if (cover["requestedSections"] is null) cover["requestedSections"] = rows;
            if (payload is not null)
            {
                if (payload.Count != 1 || payload["requestedSections"] is not JsonArray requested || requested.Count != 1 || requested[0] is not JsonObject section || Id(section, "id") != id)
                    Fail("cover-target-payload-mismatch", path + "/payload");
                payload = payload["requestedSections"]![0]!.DeepClone().AsObject(); payload.Remove("id");
            }
        }
        else
        {
            var collection = kind switch { "driver" => "drivers", "vehicle" => "vehicles", "premises" => "premises", _ => throw new ArgumentException("Unsupported servicing target.") };
            rows = risk[collection] as JsonArray ?? []; if (risk[collection] is null) risk[collection] = rows;
        }
        var existing = rows.OfType<JsonObject>().SingleOrDefault(x => Id(x, "id") == id);
        if (operation == "add")
        {
            if (Identities(capture).ContainsKey(id)) Fail("duplicate-item-id", path + "/riskItemId");
            var added = payload!.DeepClone().AsObject(); added["id"] = id.ToString(); rows.Add(added);
        }
        else
        {
            if (existing is null) Fail("foreign-risk-item", path + "/riskItemId");
            if (operation == "remove") rows.Remove(existing);
            else Update(existing!, payload!, replace);
        }
    }

    private static void Update(JsonObject target, JsonObject payload, bool replace)
    {
        // Explicit typed replacement clears omitted declarations but retains the
        // envelope-owned identity. Partial patches remain backward compatible.
        if (replace)
        {
            var identity = target["id"]?.DeepClone(); target.Clear();
            if (identity is not null) target["id"] = identity;
        }
        Merge(target, payload);
    }

    private static void Merge(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
            if (value is JsonObject child && target[key] is JsonObject existing) Merge(existing, child);
            else target[key] = value!.DeepClone();
    }
    private static DateTimeOffset? Resolve(JsonNode intent, string path, List<QuoteFieldIssue> issues)
    {
        var resolved = QuoteTerm.ResolveLondonTime(intent["localDate"]!.GetValue<string>(), intent["localTime"]!.GetValue<string>(), intent["utcOffsetMinutes"]?.GetValue<int>());
        if (resolved.Code is { } code) issues.Add(new(code, path)); return resolved.Instant;
    }
    private static Dictionary<Guid, string> Identities(JsonNode root)
    {
        var result = new Dictionary<Guid, string>();
        void Walk(JsonNode? node, string path)
        {
            if (node is JsonObject obj)
            {
                if (obj["id"] is { } idNode)
                {
                    if (!Guid.TryParse(idNode.GetValue<string>(), out var id) || id == Guid.Empty || !result.TryAdd(id, path)) Fail("duplicate-or-invalid-item-id", path);
                }
                foreach (var (key, value) in obj) Walk(value, path + "/" + key);
            }
            else if (node is JsonArray rows)
                foreach (var child in rows) Walk(child, path + "/" + (child is JsonObject item && item["id"] is { } id ? id.GetValue<string>().ToLowerInvariant() : "item"));
        }
        Walk(root, ""); return result;
    }
    private static void CheckPremisesLinks(JsonObject capture, List<QuoteFieldIssue> issues)
    {
        var premises = (capture["risk"]!["premises"] as JsonArray ?? []).OfType<JsonObject>().Select(x => Id(x, "id")).ToHashSet();
        foreach (var section in (capture["cover"]!["requestedSections"] as JsonArray ?? []).OfType<JsonObject>())
            if (section["premisesIds"] is JsonArray references)
                foreach (var reference in references)
                    if (!premises.Contains(Guid.Parse(reference!.GetValue<string>()))) issues.Add(new("unknown-premises-reference", "/cover/requestedSections/" + section["id"]));
    }
    private static JsonElement Normalize(JsonElement value)
    {
        JsonNode? Sort(JsonNode? node, string? field = null) => node switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => KeyValuePair.Create(x.Key, Sort(x.Value, x.Key)))),
            JsonArray rows when rows.All(x => x is JsonObject item && item["questionId"] is not null) =>
                new JsonArray(rows.OrderBy(x => x!["questionId"]!.GetValue<string>(), StringComparer.Ordinal).Select(x => Sort(x)).ToArray()),
            JsonArray rows when field is "premisesIds" or "driverIds" or "specifiedVehicleIds" && rows.All(x => x is JsonValue item && item.TryGetValue<string>(out var text) && Guid.TryParse(text, out _)) =>
                new JsonArray(rows.OrderBy(x => Guid.Parse(x!.GetValue<string>())).Select(x => (JsonNode?)JsonValue.Create(Guid.Parse(x!.GetValue<string>()).ToString())).ToArray()),
            JsonArray rows => new JsonArray(rows.Select(x => Sort(x)).ToArray()),
            _ => node?.DeepClone()
        };
        return Element(Sort(JsonNode.Parse(value.GetRawText()))!);
    }
    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static Guid Id(JsonObject node, string key) => Guid.Parse(node[key]!.GetValue<string>());
    private static void Fail(string code, string path) => throw new QuoteValidationException([new(code, path)]);
}
