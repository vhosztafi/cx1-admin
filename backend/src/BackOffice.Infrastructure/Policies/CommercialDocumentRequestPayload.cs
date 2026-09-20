using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;

namespace BackOffice.Infrastructure.Policies;

internal static class CommercialDocumentRequestPayload
{
    internal static string Complete(string envelope, PolicyVersion version, DateTimeOffset termEndsAt, string kind)
    {
        using var snapshot = JsonDocument.Parse(version.SnapshotJson);
        if (snapshot.RootElement.GetProperty("productCode").GetString() != "commercial-combined") return envelope;
        var source = new CommercialPayloadSource(version.PolicyId, version.Id, Convert.ToHexStringLower(version.ContentHash),
            version.SnapshotJson, version.EffectiveAt, termEndsAt);
        var result = JsonNode.Parse(envelope)!.AsObject();
        result["commercial"] = JsonNode.Parse(CommercialDocumentPayload.Create(source, kind).GetRawText());
        return result.ToJsonString();
    }
}
