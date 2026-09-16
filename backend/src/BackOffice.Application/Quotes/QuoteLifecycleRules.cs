using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace BackOffice.Application.Quotes;

public sealed record QuoteRevisionToken(Guid QuoteId, Guid RevisionId, string ContentHash);
public sealed record ClonedQuoteCapture(PreparedQuoteCapture Capture, IReadOnlyDictionary<Guid, Guid> ItemIds);

public static class QuoteLifecycleRules
{
    // Only a validated declaration document can be transferred. Evidence,
    // lookups, decisions and operational state live outside this closed shape.
    public static ClonedQuoteCapture Clone(string source, string productCode, QuoteVersionPins sourcePins, QuoteVersionPins destinationPins)
    {
        var validated = QuoteRules.Prepare(source, productCode, sourcePins);
        if (sourcePins.ProductVersionId != destinationPins.ProductVersionId || sourcePins.SchemaVersion != destinationPins.SchemaVersion ||
            sourcePins.QuestionSetVersion != destinationPins.QuestionSetVersion || sourcePins.ReferenceVersion != destinationPins.ReferenceVersion)
            throw new QuoteInputException("quote-clone-configuration-mismatch");
        var root = JsonNode.Parse(validated.Input.Json)!;
        var ids = new Dictionary<Guid, Guid>();
        void Collect(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                if (obj["id"] is JsonValue value) ids.Add(value.GetValue<Guid>(), Guid.NewGuid());
                foreach (var child in obj) Collect(child.Value);
            }
            else if (node is JsonArray array) foreach (var child in array) Collect(child);
        }
        Collect(root);
        void Remap(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                foreach (var property in obj.ToArray())
                {
                    if (property.Key is "id" or "ownerDriverId" or "riskItemId")
                        obj[property.Key] = ids[property.Value!.GetValue<Guid>()].ToString("D");
                    else if (property.Key is "driverIds" or "specifiedVehicleIds")
                    {
                        var references = property.Value!.AsArray();
                        for (var i = 0; i < references.Count; i++) references[i] = ids[references[i]!.GetValue<Guid>()].ToString("D");
                    }
                    else Remap(property.Value);
                }
            }
            else if (node is JsonArray array) foreach (var child in array) Remap(child);
        }
        Remap(root);
        return new(QuoteRules.Prepare(root.ToJsonString(), productCode, destinationPins), ids.AsReadOnly());
    }

    // Future rating/acceptance consumers must compare the held current snapshot,
    // not just a quote ID, reference or user-provided completion flag.
    public static bool MatchesCurrent(QuoteRevisionToken current, QuoteRevisionToken requested)
    {
        if (current.QuoteId == Guid.Empty || current.RevisionId == Guid.Empty || current.QuoteId != requested.QuoteId || current.RevisionId != requested.RevisionId)
            return false;
        static bool Hash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitLower);
        return Hash(current.ContentHash) && Hash(requested.ContentHash) && CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(current.ContentHash), Convert.FromHexString(requested.ContentHash));
    }
}
