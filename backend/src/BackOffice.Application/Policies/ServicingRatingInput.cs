using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Security.Cryptography;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record ServicingRatingSliceInput(DateTimeOffset EffectiveAt, IReadOnlyList<Guid> ChangeIds, ProjectedUnderwritingInput Input);
public sealed record EncodedServicingRatingInput(string Json, byte[] ContentHash);
public sealed record RenewalRatingContext(Guid PreparationVersionId, Guid? ExperienceVersionId, Guid? ExperienceReviewId,
    Guid? FairValueAssessmentId, RenewalExperienceFacts? Experience, bool EvidenceAccepted, string RuleVersion,
    int ThresholdBasisPoints, int LoadingBasisPoints);
public sealed record ServicingRatingRequestInput
{
    public required string Format { get; init; }
    public required Guid DraftId { get; init; }
    public required Guid RevisionId { get; init; }
    public required Guid PolicyId { get; init; }
    public required Guid BaseTermId { get; init; }
    public required Guid BaseVersionId { get; init; }
    public required Guid ProductVersionId { get; init; }
    public required Guid AgencyTermsVersionId { get; init; }
    public required Guid RatingRuleVersionId { get; init; }
    public required Guid BinderVersionId { get; init; }
    public required Guid AuthorityVersionId { get; init; }
    public required Guid RuntimeVersionId { get; init; }
    public required Guid ScenarioVersionId { get; init; }
    public required Guid ServicingSettingVersionId { get; init; }
    public required Guid RequestedBy { get; init; }
    public required DateTimeOffset RequestedAt { get; init; }
    public required string BaseContentHash { get; init; }
    public required string RevisionContentHash { get; init; }
    public required ResolvedQuoteTerm Term { get; init; }
    public required decimal BaseAnnualPremium { get; init; }
    public required int CommissionBasisPoints { get; init; }
    public required decimal Fee { get; init; }
    public decimal? MinimumPremium { get; init; }
    public required JsonElement RatingDefinition { get; init; }
    public required IReadOnlyList<ServicingRatingSliceInput> Slices { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RenewalRatingContext? Renewal { get; init; }
}

// Persistence/worker contract only. The service must supply held current scope,
// exact immutable source hashes and server projections; this is not an HTTP DTO.
public static class ServicingRatingInput
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 64
    };

    public static EncodedServicingRatingInput Encode(ServicingRatingRequestInput input)
    {
        Validate(input);
        var json = JsonSerializer.Serialize(input, Json); var bytes = Bytes(json);
        CheckJson(json);
        return new(json, SHA256.HashData(bytes));
    }

    public static ServicingRatingRequestInput Read(string json, byte[] expectedHash)
    {
        var bytes = Bytes(json);
        if (expectedHash is not { Length: 32 } || !CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(bytes)))
            throw Invalid();
        CheckJson(json);
        try
        {
            var input = JsonSerializer.Deserialize<ServicingRatingRequestInput>(json, Json) ?? throw Invalid();
            Validate(input); return input;
        }
        catch (JsonException) { throw Invalid(); }
    }

    private static void Validate(ServicingRatingRequestInput input)
    {
        if (input is null || input.Format is not ("servicing-rating-input-1" or "servicing-rating-input-2") ||
            (input.Format == "servicing-rating-input-2") != (input.Renewal is not null) || input.RequestedAt.Offset != TimeSpan.Zero ||
            new[] { input.DraftId, input.RevisionId, input.PolicyId, input.BaseTermId, input.BaseVersionId,
                input.ProductVersionId, input.AgencyTermsVersionId, input.RatingRuleVersionId, input.BinderVersionId,
                input.AuthorityVersionId, input.RuntimeVersionId, input.ScenarioVersionId, input.ServicingSettingVersionId, input.RequestedBy }.Any(id => id == Guid.Empty) ||
            !Hash(input.BaseContentHash) || !Hash(input.RevisionContentHash) || input.Term is null || input.Slices is null ||
            input.Slices.Count is < 1 or > 100 || !UnderwritingConfiguration.Valid(input.RatingDefinition, "rating")) throw Invalid();
        foreach (var slice in input.Slices)
        {
            if (slice is null || slice.ChangeIds is null || slice.Input is null || slice.Input.Rating is null ||
                slice.Input.Term != input.Term || slice.Input.Pricing.ValueKind != JsonValueKind.Object ||
                slice.Input.Drivers is null || slice.Input.TradeValues is null || slice.Input.CoverLimits is null ||
                slice.Input.Drivers.Count > 1000 || slice.Input.TradeValues.Count > 1000 || slice.Input.CoverLimits.Count > 1000 ||
                slice.Input.AnyDriverCount < 0 || slice.Input.Rating.DriverCount != slice.Input.Drivers.Count + slice.Input.AnyDriverCount ||
                slice.Input.Drivers.Any(driver => driver is null || driver.Id == Guid.Empty) ||
                slice.Input.Drivers.Select(driver => driver.Id).Distinct().Count() != slice.Input.Drivers.Count)
                throw Invalid();
        }
        // Reuse the exact pricing validation, including currency precision,
        // bounded totals and cumulative stable-ID schedule rules.
        _ = Calculate(input);
    }

    public static CalculatedServicingRating Calculate(ServicingRatingRequestInput input)
    {
        if (input.Format == "servicing-rating-input-1" && input.Renewal is null)
            return ServicingRatingRules.Rate(input.RatingDefinition, input.Term, input.BaseAnnualPremium,
            input.Slices.Select(x => new ServicingRiskSlice(x.EffectiveAt, x.ChangeIds, x.Input.Rating)).ToArray(),
            input.CommissionBasisPoints, input.Fee, input.MinimumPremium);
        var renewal = input.Renewal;
        if (input.Format != "servicing-rating-input-2" || renewal is null || renewal.PreparationVersionId == Guid.Empty ||
            renewal.ExperienceVersionId == Guid.Empty || renewal.ExperienceReviewId == Guid.Empty || renewal.FairValueAssessmentId == Guid.Empty ||
            (renewal.ExperienceVersionId is not null) != (renewal.Experience is not null) ||
            renewal.ExperienceReviewId is not null && renewal.ExperienceVersionId is null ||
            renewal.EvidenceAccepted && renewal.ExperienceReviewId is null ||
            string.IsNullOrWhiteSpace(renewal.RuleVersion) || renewal.RuleVersion.Length > 100 ||
            input.Slices.Count != 1 || input.BaseAnnualPremium <= 0 || input.BaseAnnualPremium > QuoteRatingRules.MaximumMoney ||
            decimal.Round(input.BaseAnnualPremium, 2) != input.BaseAnnualPremium) throw Invalid();
        var slice = input.Slices[0];
        if (slice.EffectiveAt != input.Term.StartsAt || slice.ChangeIds.Count > 100 || slice.ChangeIds.Contains(Guid.Empty) ||
            slice.ChangeIds.Distinct().Count() != slice.ChangeIds.Count) throw Invalid();
        var calculated = RenewalRatingRules.Calculate(input.RatingDefinition, slice.Input.Rating, input.Term,
            input.CommissionBasisPoints, renewal.Experience, renewal.EvidenceAccepted, input.RequestedAt,
            renewal.ThresholdBasisPoints, renewal.LoadingBasisPoints, input.Fee, input.MinimumPremium).Price;
        // Renewal preparations use whole London civil days. Preserve the full
        // term premium while keeping the annual difference only for comparison.
        if (calculated.CivilDays != decimal.Truncate(calculated.CivilDays) ||
            calculated.AnnualCivilDays != decimal.Truncate(calculated.AnnualCivilDays)) throw Invalid();
        var priced = new ServicingRatedSlice(input.Term.StartsAt, input.Term.EndsAt, Array.AsReadOnly(slice.ChangeIds.Order().ToArray()),
            calculated.AnnualPremium, calculated.AnnualPremium - input.BaseAnnualPremium,
            (int)calculated.CivilDays, (int)calculated.AnnualCivilDays, calculated.TermPremium, calculated.Tax, calculated.BrokerCommission);
        return new(input.BaseAnnualPremium, Array.AsReadOnly(new[] { priced }), calculated.TermPremium, calculated.Tax,
            calculated.BrokerCommission, calculated.Fee, calculated.GrossPayable, calculated.GrossPayable - calculated.BrokerCommission);
    }

    private static bool Hash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static byte[] Bytes(string json)
    {
        try
        {
            if (json is null || Utf8.GetByteCount(json) > MaximumBytes) throw Invalid();
            return Utf8.GetBytes(json);
        }
        catch (EncoderFallbackException) { throw Invalid(); }
    }
    private static void CheckJson(string json)
    {
        try { using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 }); Unique(document.RootElement); }
        catch (JsonException) { throw Invalid(); }
    }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { if (!names.Add(property.Name)) throw Invalid(); Unique(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) Unique(item);
    }
    private static ArgumentException Invalid() => new("Invalid or altered persisted servicing rating input.");
}
