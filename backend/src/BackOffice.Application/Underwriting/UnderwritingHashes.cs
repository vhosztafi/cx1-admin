using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;

namespace BackOffice.Application.Underwriting;

public sealed record UnderwritingOwnership(Guid QuoteId, Guid RevisionId, Guid AgencyId, Guid ClientId, Guid RelationshipId,
    Guid RatingRuleVersionId, Guid BinderVersionId, Guid AuthorityVersionId);
public sealed record UnderwritingAssuranceItem(Guid Id, string Kind, Guid? LatestDecisionId, string State, string Fingerprint);

public static class UnderwritingHashes
{
    // pricingFacts is the closed server projection, never the full proposal:
    // names/support flags, proof, labels and display timestamps do not belong.
    public static CanonicalQuoteInput Pricing(UnderwritingOwnership owner, QuoteVersionPins pins, JsonElement pricingFacts)
    {
        if (new[] { owner.QuoteId, owner.RevisionId, owner.AgencyId, owner.ClientId, owner.RelationshipId,
                owner.RatingRuleVersionId, owner.BinderVersionId, owner.AuthorityVersionId }.Any(x => x == Guid.Empty) || pricingFacts.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Complete retained ownership and typed pricing facts are required.");
        return QuoteCanonicalJson.Create(JsonSerializer.Serialize(new { format = "underwriting-pricing-1", owner, facts = Ordered(pricingFacts) }), pins);
    }

    public static string Terms(Guid cycleId, Guid ratingId, QuoteVersionPins pins, JsonElement contractualPayload)
    {
        if (cycleId == Guid.Empty || ratingId == Guid.Empty || contractualPayload.ValueKind != JsonValueKind.Object) throw new ArgumentException("Exact rating and terms payload are required.");
        return QuoteCanonicalJson.Create(JsonSerializer.Serialize(new { format = "underwriting-terms-1", cycleId, ratingId, payload = Ordered(contractualPayload) }), pins).ContentHash;
    }

    // Acceptance is deliberately absent: it records this hash, not a new input
    // to it. Exact proof/decision IDs prevent same-content ownership substitution.
    public static string Assurance(Guid cycleId, QuoteVersionPins pins, IReadOnlyList<UnderwritingAssuranceItem> items)
    {
        if (cycleId == Guid.Empty || items.Select(x => x.Id).Distinct().Count() != items.Count ||
            items.Any(x => x.Id == Guid.Empty || x.LatestDecisionId == Guid.Empty ||
                x.Kind is not ("evidence" or "referral" or "condition" or "capacity") || string.IsNullOrWhiteSpace(x.State) ||
                x.Fingerprint.Length != 64 || x.Fingerprint.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            throw new ArgumentException("Exact assurance identities and fingerprints are required.");
        return QuoteCanonicalJson.Create(JsonSerializer.Serialize(new { format = "underwriting-assurance-1", cycleId, items = items.OrderBy(x => x.Id) }), pins).ContentHash;
    }

    private static JsonNode? Ordered(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var obj = new JsonObject();
            foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)) obj.Add(property.Name, Ordered(property.Value));
            return obj;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var items = value.EnumerateArray().ToArray();
            if (items.Length > 0 && items.All(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String))
            {
                if (items.Select(x => x.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count() != items.Length) throw new ArgumentException("Duplicate stable pricing item.");
                items = items.OrderBy(x => x.GetProperty("id").GetString(), StringComparer.Ordinal).ToArray();
            }
            return new JsonArray(items.Select(Ordered).ToArray());
        }
        return JsonNode.Parse(value.GetRawText());
    }
}
