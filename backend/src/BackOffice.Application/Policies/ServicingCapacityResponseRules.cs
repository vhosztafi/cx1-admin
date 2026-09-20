using System.Text;
using System.Text.Json;
using BackOffice.Application.Underwriting;
using BackOffice.Application.Quotes;

namespace BackOffice.Application.Policies;

public sealed record ServicingCarrierConditionInput(JsonElement Definition, IReadOnlyList<DateTimeOffset> EffectiveDates);
public sealed record ServicingCapacityResponseDefinition(string Outcome, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo,
    IReadOnlyList<JsonElement> AuthorisedLimits, IReadOnlyList<ServicingCarrierConditionInput> Conditions);
public sealed record ServicingParsedCarrierCondition(string DefinitionJson, IReadOnlyList<DateTimeOffset> EffectiveDates,
    IReadOnlyList<ServicingParsedCondition> Slices);
public sealed record ServicingParsedCapacityResponse(IReadOnlyList<CapacityExtension> Extensions,
    IReadOnlyList<ServicingParsedCarrierCondition> Conditions)
{
    public IReadOnlyList<CommercialCapacityExtension>? CommercialExtensions { get; init; }
}

public static class ServicingCapacityResponseRules
{
    // Both the demo adapter and supplied responses enter through this parser.
    // Persistence and current-scope checks remain with their owning command.
    public static ServicingParsedCapacityResponse Parse(ServicingCapacityResponseDefinition response,
        string ruleCode, string dimension, DateTimeOffset submittedAt, DateTimeOffset receivedAt, DateTimeOffset now,
        IReadOnlyList<ServicingEvidenceSlice> slices)
    {
        if (response is null || response.AuthorisedLimits is null || response.Conditions is null || slices is null ||
            slices.Count is < 1 or > 100 || submittedAt.Offset != TimeSpan.Zero || receivedAt.Offset != TimeSpan.Zero ||
            now.Offset != TimeSpan.Zero || receivedAt < submittedAt || receivedAt > now ||
            string.IsNullOrWhiteSpace(ruleCode) || string.IsNullOrWhiteSpace(dimension) ||
            response.Outcome is not ("approve" or "approve-with-conditions" or "query" or "decline")) throw Invalid();
        DateTimeOffset? previous = null; long bytes = 0;
        foreach (var slice in slices)
        {
            if (slice is null || slice.EffectiveAt.Offset != TimeSpan.Zero || previous is not null && slice.EffectiveAt <= previous ||
                slice.Proposal.ValueKind != JsonValueKind.Object || !slice.Proposal.TryGetProperty("risk", out var risk) ||
                risk.ValueKind != JsonValueKind.Object) throw Invalid();
            bytes += Encoding.UTF8.GetByteCount(slice.Proposal.GetRawText());
            if (bytes > ServicingRatingInput.MaximumBytes) throw Invalid();
            previous = slice.EffectiveAt;
        }
        var approving = response.Outcome is "approve" or "approve-with-conditions";
        if (approving ? response.AuthorisedLimits.Count is < 1 or > 20 || response.ValidFrom is null || response.ValidTo is null ||
                response.ValidFrom >= response.ValidTo || response.ValidFrom.Value.Offset != TimeSpan.Zero || response.ValidTo.Value.Offset != TimeSpan.Zero
            : response.AuthorisedLimits.Count != 0 || response.ValidFrom is not null || response.ValidTo is not null) throw Invalid();
        if (response.Outcome == "approve-with-conditions" ? response.Conditions.Count is < 1 or > 20 : response.Conditions.Count != 0) throw Invalid();
        var commercial = slices.Any(x => x.Proposal.TryGetProperty("productCode", out var code) && code.GetString() == CommercialCaptureRules.ProductCode);
        CommercialCapacityExtension[]? commercialExtensions = null;
        if (commercial)
        {
            if (slices.Any(x => !x.Proposal.TryGetProperty("productCode", out var code) || code.GetString() != CommercialCaptureRules.ProductCode)) throw Invalid();
            foreach (var slice in slices) CommercialCaptureRules.ValidateShapeAndIdentity(slice.Proposal);
            commercialExtensions = response.AuthorisedLimits.Select(CommercialCapacityRules.Extension).ToArray();
            if (commercialExtensions.Any(x => x.Dimension != dimension || x.RiskItemId is { } id &&
                    !slices.Any(s => s.Proposal.GetProperty("risk").GetProperty("locations").EnumerateArray().Any(l => l.GetProperty("id").GetGuid() == id))) ||
                commercialExtensions.Select(x => (x.Dimension,x.RiskItemId)).Distinct().Count() != commercialExtensions.Length) throw Invalid();
        }
        var expected = commercial ? dimension : CapacityRules.Dimension(ruleCode, dimension);
        var extensions = commercial ? [] : response.AuthorisedLimits.Select(CapacityRules.Extension).ToArray();
        if (extensions.Any(x => x.Dimension != expected || x.Dimension == "trade-restriction" && x.QuestionId != ruleCode) ||
            extensions.Select(x => x.Dimension).Distinct(StringComparer.Ordinal).Count() != extensions.Length) throw Invalid();
        var conditions = new List<ServicingParsedCarrierCondition>(); var definitions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in response.Conditions)
        {
            if (input is null || input.EffectiveDates is null) throw Invalid();
            var parsed = ServicingConditionRules.Parse(input.Definition, slices, input.EffectiveDates);
            var canonical = parsed[0].Condition.DefinitionJson;
            // A condition cannot be split into duplicate entries to evade the
            // response condition limit or acquire contradictory resolutions.
            if (!definitions.Add(canonical)) throw Invalid();
            conditions.Add(new(canonical, input.EffectiveDates.ToArray(), parsed));
        }
        return new(extensions, conditions.AsReadOnly()) { CommercialExtensions = commercialExtensions };
    }

    private static ArgumentException Invalid() => new("A bounded current servicing response with exact extent and dated conditions is required.");
}
