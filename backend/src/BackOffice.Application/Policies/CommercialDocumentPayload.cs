using System.Text.Json;
using System.Text.Json.Nodes;

namespace BackOffice.Application.Policies;

public static class CommercialDocumentPayload
{
    public static JsonElement Create(CommercialPayloadSource source, string kind)
    {
        var snapshot = source.Read(); var cover = snapshot.GetProperty("cover"); var risk = snapshot.GetProperty("risk");
        if (kind is not ("policy-schedule" or "policy-statement" or "policy-certificate") || snapshot.GetProperty("snapshotFormat").GetString() == "issued-commercial-cancellation-1")
            throw new ArgumentException("commercial-document-kind-not-applicable");
        var payload = new JsonObject { ["format"] = "commercial-document-1", ["policyId"] = source.PolicyId, ["versionId"] = source.VersionId,
            ["sourceContentHash"] = source.SourceContentHash, ["kind"] = kind, ["effectiveAt"] = JsonSerializer.SerializeToNode(source.EffectiveFrom) };
        void Copy(string name, JsonElement value) => payload[name] = JsonNode.Parse(value.GetRawText());
        Copy("insured", snapshot.GetProperty("insured")); Copy("term", snapshot.GetProperty("term"));
        Copy("endorsements", cover.GetProperty("endorsements")); Copy("warranties", cover.GetProperty("warranties"));
        if (kind == "policy-schedule")
        {
            Copy("sections", cover.GetProperty("sections")); Copy("locations", risk.GetProperty("locations"));
            Copy("business", risk.GetProperty("business")); Copy("wages", risk.GetProperty("wages")); Copy("liability", risk.GetProperty("liability"));
            if (risk.TryGetProperty("businessInterruption", out var interruption)) Copy("businessInterruption", interruption);
            Copy("premium", snapshot.GetProperty("premium"));
        }
        else if (kind == "policy-statement") { Copy("declarations", risk); Copy("cover", cover); }
        else
        {
            var sections = cover.GetProperty("sections").EnumerateArray().Where(x => x.GetProperty("code").GetString() == "employers-liability").ToArray();
            if (sections.Length != 1) throw new ArgumentException("commercial-document-kind-not-applicable");
            Copy("section", sections[0]);
            if (risk.GetProperty("liability").TryGetProperty("employersReferenceNumber", out var reference)) Copy("employersReferenceNumber", reference);
        }
        return JsonSerializer.SerializeToElement(payload);
    }

    public static bool Valid(JsonElement payload, CommercialPayloadSource source, string kind)
    {
        try { return CommercialPayloadSource.Unique(payload) && JsonElement.DeepEquals(payload, Create(source, kind)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or JsonException) { return false; }
    }
}
