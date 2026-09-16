using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Domain;
using static BackOffice.Application.Quotes.QuoteSectionValues;

namespace BackOffice.Application.Underwriting;

public sealed record ProjectedUnderwritingInput(RatingFacts Rating, ResolvedQuoteTerm Term, JsonElement Pricing,
    IReadOnlyList<UnderwritingDriver> Drivers, IReadOnlyList<int> TradeValues, int TradingYears, decimal VehicleLimit,
    IReadOnlyDictionary<string, decimal> CoverLimits, bool HasSalvage, int AnyDriverCount, int? AnyDriverMinimumAge, int? AnyDriverMaximumAge)
{
    public UnderwritingRisk RiskForPremium(decimal premium) => new(premium, Rating.StockLimit, VehicleLimit, TradingYears,
        Rating.HasClaims, Rating.HasValeting, TradeValues, Drivers, CoverLimits) { HasSalvageOrBreaking = HasSalvage,
            AnyDriverCount = AnyDriverCount, AnyDriverMinimumAge = AnyDriverMinimumAge, AnyDriverMaximumAge = AnyDriverMaximumAge };
}

// Pure projection after the service has assessed semantic capture/matching and
// lookup provenance. Structural/catalogue/meaning gates are repeated here so
// absent input cannot silently become zero risk. No raw personal data is emitted.
public static class QuoteUnderwritingInput
{
    private static readonly Lazy<JsonElement> Meanings = new(() =>
    {
        using var stream = typeof(QuoteUnderwritingInput).Assembly.GetManifestResourceStream("Underwriting.SourceMeanings")!;
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.GetProperty("referenceVersion").GetString() != QuoteCatalogueIdentity.Version)
            throw new InvalidOperationException("Underwriting reference meanings do not match capture.");
        return doc.RootElement.Clone();
    });
    private sealed record Trade(Guid Id, int Value, long Share);
    private sealed record Vehicle(Guid Id, decimal Value);

    public static ProjectedUnderwritingInput Project(JsonElement proposal, JsonElement ratingConfiguration)
    {
        var shape = QuoteCaptureShape.ValidateCompleteness(proposal);
        if (shape.Count > 0) throw new QuoteValidationException(shape);
        var identity = QuoteCatalogueIdentity.ValidateQuestions(proposal).Concat(QuoteCatalogueIdentity.ValidateReferences(proposal)).ToArray();
        if (identity.Length > 0) throw new QuoteValidationException(identity);
        var product = proposal.GetProperty("productCode").GetString()!;
        if (!UnderwritingConfiguration.Valid(ratingConfiguration, "rating") || ratingConfiguration.GetProperty("productCode").GetString() != product)
            throw new ArgumentException("Applicable rating configuration is required.");
        var term = QuoteTerm.Assess(proposal.GetProperty("termIntent"));
        if (term.Term is null) throw new QuoteValidationException(term.Issues);
        try { _ = QuoteRatingRules.CivilDuration(term.Term); }
        catch (ArgumentException) { throw Invalid("underwriting-term-invalid", "/termIntent"); }
        var start = DateOnly.ParseExact(proposal.GetProperty("termIntent").GetProperty("localStartDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var premises = Items(At(proposal, "risk.premises")).Select(x => x.GetProperty("id").GetGuid()).ToArray();
        var sections = UnderwritingRules.ValidateSections(proposal.GetProperty("cover"), product, premises);
        if (sections.Count != 0) throw new QuoteValidationException(sections);
        var requested = At(proposal, "cover.requestedSections").EnumerateArray().ToArray();
        JsonElement Selected(string code) => requested.FirstOrDefault(x => x.GetProperty("code").GetString() == code && x.GetProperty("selected").GetBoolean());
        decimal SectionLimit(string code) => Selected(code).ValueKind == JsonValueKind.Undefined ? 0 : Amount(Selected(code), "limit", "/cover/requestedSections");
        var stock = SectionLimit("stock-custody"); var tools = SectionLimit("tools-equipment"); var premisesLimit = SectionLimit("premises");
        var business = At(proposal, "risk.business"); var tradingYears = Years(At(business, "startedOn"), start, "/risk/business/startedOn");
        var trades = Items(At(business, "activities")).Select(x => new Trade(x.GetProperty("id").GetGuid(),
            Trusted(At(x, "code"), "mtOccupations", "/risk/business/activities"), Number(At(x, "turnoverBasisPoints")) ?? throw Invalid("activity-share-required", "/risk/business/activities")))
            .Where(x => x.Share > 0).OrderBy(x => x.Id).ToArray();
        if (trades.Length == 0 || trades.Sum(x => x.Share) != 10000) throw Invalid("activity-shares-invalid", "/risk/business/activities");
        var valetingValues = ratingConfiguration.GetProperty("valetingTradeValues").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
        var hasValeting = trades.Any(x => valetingValues.Contains(x.Value));
        var hasSalvage = RequiredBoolean(Answer(business, "/risk/business", "prototype.quote.ba9d4158ae2c"));
        var businessLoss = RequiredBoolean(Answer(business, "/risk/business", "prototype.quote.36da21d3c935"));
        var drivers = Items(At(proposal, "risk.drivers")).Select((x, i) =>
        {
            var path = "/risk/drivers/" + i;
            return new UnderwritingDriver(x.GetProperty("id").GetGuid(), Years(At(x, "dateOfBirth"), start, path + "/dateOfBirth"),
                Years(At(x, "licence.issuedOn"), start, path + "/licence/issuedOn"),
                RequiredBoolean(Answer(x, path, "MTS-06-Q33")) || Items(At(x, "convictions")).Any(),
                RequiredBoolean(Answer(x, path, "MTS-06-Q40")) || Items(At(x, "losses")).Any());
        }).OrderBy(x => x.Id).ToArray();
        var risk = At(proposal, "risk");
        var driverPlan = Trusted(Answer(risk, "/risk", "MTS-06-Q01").Value, "driverPlans", "/risk/responses");
        if (driverPlan is < 1 or > 3 || (driverPlan == 3 && drivers.Length > 0) || (driverPlan is 1 or 2 && drivers.Length == 0)) throw Invalid("driver-basis-invalid", "/risk/responses");
        int anyCount = 0; int? minAge = null, maxAge = null;
        var anyLimits = new Dictionary<string, int>();
        if (driverPlan is 2 or 3)
        {
            var count = Number(Answer(risk, "/risk", "MTS-06-Q02").Value);
            if (count is null or < 1 or > 1000) throw Invalid("positive-any-driver-count-required", "/risk/responses");
            anyCount = (int)count;
            minAge = NumericMeaning(Answer(risk, "/risk", "MTS-06-Q03").Value, "aadDriverMinAge");
            maxAge = NumericMeaning(Answer(risk, "/risk", "MTS-06-Q04").Value, "aadDriverMaxAge");
            if (minAge > maxAge) throw Invalid("any-driver-age-range-invalid", "/risk/responses");
            foreach (var (number, collection) in new[] { (5, "aadMaxVehicleGrouping"), (6, "aadMaxVehicleGvw"), (7, "aadMaxMotorcycleCc") })
                anyLimits[collection] = Trusted(Answer(risk, "/risk", $"MTS-06-Q{number:00}").Value, collection, "/risk/responses");
        }
        var youngest = drivers.Select(x => x.Age).Concat(minAge is null ? [] : new[] { minAge.Value }).Min();
        var vehicles = Items(At(proposal, "risk.vehicles")).Select((x, i) => new Vehicle(x.GetProperty("id").GetGuid(), Amount(x, "value", "/risk/vehicles/" + i + "/value"))).OrderBy(x => x.Id).ToArray();
        var coverFacts = QuoteCoverFacts.Reconcile(proposal);
        if (coverFacts.Issues.Count > 0) throw new QuoteValidationException(coverFacts.Issues);
        var level = coverFacts.Facts.GetValueOrDefault("coverLevel") ?? throw Invalid("cover-level-required", "/cover/responses");
        decimal CoverAmount(string key, bool required = false) => coverFacts.Facts.TryGetValue(key, out var value) ? Money.Parse(value).Pence / 100m :
            required ? throw Invalid("cover-limit-required", "/cover/responses") : 0;
        var own = CoverAmount("ownVehicleLimit", level != "third-party-only"); var customer = CoverAmount("customerVehicleLimit");
        var exposure = Math.Max(Math.Max(own, customer), vehicles.Select(x => x.Value).DefaultIfEmpty(0).Max());
        if (Selected("stock-custody").ValueKind != JsonValueKind.Undefined) exposure = Math.Max(exposure, Amount(Selected("stock-custody"), "anyOneVehicleLimit", "/cover/requestedSections"));
        var insurance = At(proposal, "risk.previousInsurance");
        var sourceYears = NumericMeaning(Answer(insurance, "/risk/previousInsurance", "MTS-05-Q10").Value, "noClaimBonuses");
        var noClaimsYears = Number(At(insurance, "noClaimsYears")) is { } declaredYears ? checked((int)declaredYears) : sourceYears;
        var noClaimsBasis = Text(At(insurance, "noClaimsYearsBasis")) ?? "source-minimum";
        var claims = businessLoss || drivers.Any(x => x.HasClaims);
        var limits = new Dictionary<string, decimal> { ["road-risks"] = Math.Max(own, customer) };
        if (stock > 0) limits["stock-custody"] = stock;
        if (premisesLimit > 0) limits["premises"] = premisesLimit;
        if (tools > 0) limits["tools-equipment"] = tools;
        var facts = new RatingFacts(product, checked(drivers.Length + anyCount), vehicles.Length, stock, premisesLimit > 0, tools > 0, claims, hasValeting, youngest, noClaimsYears);
        var pricing = JsonSerializer.SerializeToElement(new { meaningsVersion = Meanings.Value.GetProperty("version").GetString(), productCode = product,
            term = term.Term, trades, tradingYears, drivers, vehicles, driverPlan, anyCount, minAge, maxAge, anyLimits,
            hasSalvage, businessLoss, noClaimsYears, noClaimsBasis,
            cover = new { level, own, customer, excess = CoverAmount("excess"), requestedSections = requested } });
        return new(facts, term.Term, pricing, drivers, trades.Select(x => x.Value).Distinct().Order().ToArray(), tradingYears, exposure, limits, hasSalvage, anyCount, minAge, maxAge);
    }

    private static int NumericMeaning(JsonElement reference, string collection)
    {
        var id = Trusted(reference, collection, "/risk/responses");
        var row = Meanings.Value.GetProperty("collections").GetProperty(collection).EnumerateArray().SingleOrDefault(x => x.GetProperty("value").GetInt32() == id);
        if (row.ValueKind == JsonValueKind.Undefined) throw Invalid("reference-meaning-unavailable", "/risk/responses");
        return row.GetProperty("numericValue").GetInt32();
    }
    private static int Trusted(JsonElement reference, string collection, string path) =>
        QuoteCatalogueIdentity.TrustedValue(reference, collection) is { } value ? checked((int)value) : throw Invalid("untrusted-underwriting-reference", path);
    private static bool RequiredBoolean(QuoteSectionField field) => Boolean(field.Value) ?? throw Invalid("underwriting-declaration-required", field.Path);
    private static int Years(JsonElement date, DateOnly at, string path)
    {
        if (!Date(Text(date), out var from) || from > at) throw Invalid("invalid-underwriting-date", path);
        var years = at.Year - from.Year; return years - (from.AddYears(years) > at ? 1 : 0);
    }
    private static decimal Amount(JsonElement holder, string name, string path)
    {
        try { var value = Money.Parse(holder.GetProperty(name).GetString()!).Pence / 100m; return value >= 0 ? value : throw Invalid("invalid-underwriting-amount", path); }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or KeyNotFoundException) { throw Invalid("invalid-underwriting-amount", path); }
    }
    private static QuoteValidationException Invalid(string code, string path) => new([new(code, path)]);
}
