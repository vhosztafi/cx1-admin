using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;

namespace BackOffice.Application.Policies;

public static class PolicyHistoryRules
{
    // Callers must authorize both version identities before comparing. Only
    // validated, issued public declarations reach the stable-ID diff engine.
    public static IReadOnlyList<QuoteRevisionChange> Compare(JsonElement before,JsonElement after)
        =>QuoteRevisionDiff.Compare(Declarations(before),Declarations(after));

    public static JsonElement Declarations(JsonElement snapshot)
    {
        Validate(snapshot);var result=new JsonObject();
        foreach(var field in new[]{"productCode","insured","risk","cover","term","premium","cancellation"})
            if(snapshot.TryGetProperty(field,out var value))result[field]=JsonNode.Parse(value.GetRawText());
        return JsonSerializer.SerializeToElement(result);
    }

    public static ClonedQuoteCapture Clone(JsonElement snapshot,QuoteVersionPins sourcePins,QuoteVersionPins destinationPins)
    {
        Validate(snapshot);
        var insured=JsonNode.Parse(snapshot.GetProperty("insured").GetRawText())!.AsObject();
        insured.Remove("clientId");insured.Remove("clientAgencyRelationshipId");
        var risk=JsonNode.Parse(snapshot.GetProperty("risk").GetRawText())!.AsObject();risk.Remove("driverBasis");
        var cover=JsonNode.Parse(snapshot.GetProperty("cover").GetRawText())!.AsObject();
        cover.Remove("sections");cover.Remove("endorsements");cover.Remove("warranties");
        var product=snapshot.GetProperty("productCode").GetString()!;
        // No issued dates, prices, approvals or operational provenance. Absence
        // of a new term intentionally leaves capture incomplete for review.
        var capture=new JsonObject{["schemaVersion"]=sourcePins.SchemaVersion,["productCode"]=product,
            ["insured"]=insured,["risk"]=risk,["cover"]=cover};
        return QuoteLifecycleRules.Clone(capture.ToJsonString(),product,sourcePins,destinationPins);
    }

    private static void Validate(JsonElement snapshot)
    {
        if(!PolicySnapshotShape.Valid(snapshot))throw new ArgumentException("A supported, validated issued policy snapshot is required.",nameof(snapshot));
    }
}
