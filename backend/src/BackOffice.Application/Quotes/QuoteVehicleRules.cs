using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BackOffice.Application.Quotes;

// Runs after strict draft shape, question/reference and stable-ID validation.
// Capture modes are trusted persisted service context, never proposal fields.
public static partial class QuoteVehicleRules
{
    public static IReadOnlyList<QuoteFieldIssue> Assess(JsonElement proposal, DateOnly asOf, IReadOnlyDictionary<Guid,string>? captureModes = null)
    {
        if (asOf < new DateOnly(1900,1,1)) throw new ArgumentOutOfRangeException(nameof(asOf));
        var issues = new List<QuoteFieldIssue>(); var risk = At(proposal,"risk");
        void Add(string code,string path,string? question = null) { if (issues.Count < QuoteCaptureShape.MaximumIssues) issues.Add(new(code,path,question)); }
        var specified = Items(At(risk,"specifiedVehicleIds")).Select(Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var declaration = At(risk,"specifiedVehiclesRequested").ValueKind;
        if (declaration == JsonValueKind.Undefined) Add("specified-vehicle-declaration-required","/risk/specifiedVehiclesRequested");
        if (declaration == JsonValueKind.True && specified.Count == 0) Add("specified-vehicle-required","/risk/specifiedVehicleIds");
        if (declaration == JsonValueKind.False && specified.Count > 0) Add("inactive-specified-vehicles-retained","/risk/specifiedVehicleIds");
        var postcodes = new[] { Text(At(proposal,"insured.address.postcode")) }
            .Concat(Items(At(risk,"premises")).Select(row => Text(At(row,"address.postcode"))))
            .Concat(Items(At(risk,"drivers")).Where(row => Answer(row,"MTS-06-Q31").ValueKind == JsonValueKind.True).Select(row => Text(At(row,"address.postcode"))))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => Normalize(value!)).ToHashSet(StringComparer.Ordinal);
        var registrations = new HashSet<string>(StringComparer.Ordinal); var index = 0;
        foreach (var vehicle in Items(At(risk,"vehicles")))
        {
            var path = $"/risk/vehicles/{index++}"; var isSpecified = specified.Contains(Text(At(vehicle,"id")));
            void Require(string field) { if (!Present(At(vehicle,field))) Add("required-vehicle-field",path + "/" + field); }
            foreach (var field in new[] { "registration","abiCode","value","purchasedOn","declaredOwnerType","keptOvernightType","keptOvernightAddress","partOfLeaseAgreement","modified" }) Require(field);
            if (!isSpecified) Require("customerLoan");
            if (!Present(At(vehicle,"register"))) Add("vehicle-register-required",path + "/register");
            var mode = Guid.TryParse(Text(At(vehicle,"id")),out var id) && captureModes is not null ? captureModes.GetValueOrDefault(id) : null;
            if (mode is not ("manual" or "lookup")) Add("vehicle-capture-context-required",path);
            if (mode == "manual") foreach (var field in new[] { "make","model","vehicleType","bodyDescription","registrationYear","registeredOn","imported" }) Require(field);
            var type = QuoteReferenceMetadata.Trusted(At(vehicle,"vehicleType"),"vehicleType"); var category = Text(At(type,"category"));
            if (type.ValueKind == JsonValueKind.Undefined) Add("vehicle-category-context-required",path + "/vehicleType");
            else
            {
                if (category is not ("Motorcycle" or "Commercial" or "Other")) Require("abiGroup");
                if (mode == "manual" && category != "Car") { Require("declaredEngineSize"); if (category != "Other") Require("grossWeightKg"); }
            }
            if (Text(At(vehicle,"registration")) is { } registration)
            {
                if (!Regex.IsMatch(registration,"^[A-Za-z0-9 ]+$",RegexOptions.CultureInvariant)) Add("vehicle-registration-invalid",path + "/registration");
                if (!registrations.Add(Normalize(registration))) Add("duplicate-registration",path + "/registration");
            }
            if (At(vehicle,"partOfLeaseAgreement").ValueKind == JsonValueKind.True) Require("leaseLengthYears");
            foreach (var field in new[] { "purchasedOn","registeredOn" })
                if (Date(Text(At(vehicle,field)),out var date))
                {
                    if (date < new DateOnly(1900,1,1)) Add("vehicle-date-too-early",path + "/" + field);
                    if (field == "purchasedOn" && !isSpecified && date > asOf) Add("purchase-in-future",path + "/purchasedOn");
                }
            if (mode == "manual" && Number(At(vehicle,"registrationYear")) is < 1900) Add("vehicle-year-too-early",path + "/registrationYear");
            if (isSpecified && Money(At(vehicle,"value")) is < 50000) Add("specified-vehicle-value-minimum",path + "/value");
            var overnight = Text(At(vehicle,"keptOvernightAddress"));
            if (string.IsNullOrWhiteSpace(overnight)) Add("overnight-postcode-required",path + "/keptOvernightAddress");
            else if (postcodes.Count == 0) Add("overnight-postcode-context-required",path + "/keptOvernightAddress");
            else if (!postcodes.Contains(Normalize(overnight))) Add("overnight-postcode-not-in-proposal",path + "/keptOvernightAddress");
            var modifications = Items(At(vehicle,"modifications")).ToArray(); var modified = At(vehicle,"modified").ValueKind;
            if (modified == JsonValueKind.True && modifications.Length == 0) Add("vehicle-modification-required",path + "/modifications");
            if (modified == JsonValueKind.False && modifications.Length > 0) Add("inactive-vehicle-modifications-retained",path + "/modifications");
            var codes = new HashSet<long>(); var child = 0;
            foreach (var modification in modifications)
            {
                var field = $"{path}/modifications/{child++}/code"; var code = At(modification,"code");
                if (!Present(code)) Add("vehicle-modification-code-required",field);
                else if (Number(At(code,"value")) is { } value && !codes.Add(value)) Add("duplicate-vehicle-modification",field);
            }
            AssessCharacteristics(proposal,vehicle,path,Add);
        }
        issues.AddRange(AssessPlates(proposal)); issues.AddRange(AssessPortfolio(proposal)); issues.AddRange(AssessLimits(proposal));
        return issues.Take(QuoteCaptureShape.MaximumIssues).ToArray();
    }
    private static void AssessCharacteristics(JsonElement proposal,JsonElement vehicle,string path,Action<string,string,string?> add)
    {
        const string characteristic = "prototype.addveh.special-characteristics", mid = "prototype.addveh.report-mid";
        void Add(string code,string field,string? question = null) => add(code,path + "/" + field,question);
        var selected = Answer(vehicle,characteristic);
        if (!Present(selected)) Add("vehicle-characteristics-required","responses/answers",characteristic);
        if (!Present(Answer(vehicle,mid))) Add("vehicle-mid-declaration-required","responses/answers",mid);
        var option = QuoteCatalogueIdentity.TrustedValue(selected,characteristic);
        var modified = At(vehicle,"modified").ValueKind == JsonValueKind.True;
        var keys = new[] { "5f9e8331ac6f","488ecf4bdc09","87fad6b4a9fe","d7a75768e505" };
        bool Declared(int index) => Answer(At(proposal,"risk.business"),"prototype.quote." + keys[index]).ValueKind == JsonValueKind.True;
        void Require(int index) { if (!Declared(index)) Add("vehicle-characteristic-declaration-required","responses/answers",characteristic); }
        if (modified) Require(0);
        if (option is null) return;
        if (option == 2 && !modified) { Add("conflicting-vehicle-modification","modified"); Require(0); }
        else if (option is not (2 or 6) && modified) Add("conflicting-vehicle-modification","responses/answers",characteristic);
        if (option == 3) Require(1);
        if (option == 4) { Require(2); if (At(vehicle,"imported").ValueKind == JsonValueKind.False) Add("conflicting-vehicle-import","imported"); }
        if (option == 5) Require(3);
        if (option == 6 && Enumerable.Range(0,4).Count(Declared) < 2) Add("multiple-vehicle-characteristics-context-required","responses/answers",characteristic);
    }
    private static JsonElement Answer(JsonElement holder,string id) => At(Items(At(holder,"responses.answers")).FirstOrDefault(row => Text(At(row,"questionId")) == id),"value");
    private static string Normalize(string value) => value.Replace(" ","",StringComparison.Ordinal).ToUpperInvariant();
    private static bool Present(JsonElement value) => value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) && (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));
    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
    private static decimal? Money(JsonElement value) => decimal.TryParse(Text(value),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount) ? amount : null;
    private static bool Date(string? value,out DateOnly date) => DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date);
    private static IEnumerable<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    private static JsonElement At(JsonElement value,string path) { foreach (var part in path.Split('.')) if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part,out value)) return default; return value; }
}
