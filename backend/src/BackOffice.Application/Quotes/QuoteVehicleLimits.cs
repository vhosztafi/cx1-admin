using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BackOffice.Application.Quotes;

public static partial class QuoteVehicleRules
{
    private readonly record struct Limit(bool Known,decimal? Maximum);
    private static IReadOnlyList<QuoteFieldIssue> AssessLimits(JsonElement proposal)
    {
        var issues = new List<QuoteFieldIssue>(); var risk = At(proposal,"risk"); var drivers = Items(At(risk,"drivers")).ToArray();
        var plan = QuoteCatalogueIdentity.TrustedValue(Answer(risk,"MTS-06-Q01"),"driverPlans"); var named = plan is 1 or 2; var any = plan is 2 or 3;
        Limit Metadata(JsonElement row,string field)
        {
            var value = At(row,field);
            return value.ValueKind == JsonValueKind.Null ? new(true,null) : value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? new(true,number) : default;
        }
        Limit Aggregate(string question,string collection,Func<JsonElement,Limit> read,string planQuestion,string planCollection,string planField)
        {
            if (plan is null) return default; var values = new List<Limit>();
            if (named)
            {
                if (drivers.Length == 0) return default;
                values.AddRange(drivers.Select(driver => read(QuoteReferenceMetadata.Trusted(Answer(driver,question),collection))));
            }
            if (any) values.Add(Metadata(QuoteReferenceMetadata.Trusted(Answer(risk,planQuestion),planCollection),planField));
            if (values.Count == 0 || values.Any(value => !value.Known)) return default;
            return new(true,values.Any(value => value.Maximum is null) ? null : values.Max(value => value.Maximum));
        }
        var abi = Aggregate("MTS-06-Q27","driverMaxABIGroups",row => Abi(row) is { } number ? new(true,number) : default,"MTS-06-Q05","aadMaxVehicleGrouping","abiGroup");
        var weight = Aggregate("MTS-06-Q20","driverGVWLimits",row => Metadata(row,"gvwLimitDecimal"),"MTS-06-Q06","aadMaxVehicleGvw","maxGVW");
        var cc = Aggregate("MTS-06-Q25","driverMotorcycleCovers",row => Metadata(row,"maxCC"),"MTS-06-Q07","aadMaxMotorcycleCc","maxCC");
        var namedCc = named ? drivers.Select(driver => Metadata(QuoteReferenceMetadata.Trusted(Answer(driver,"MTS-06-Q25"),"driverMotorcycleCovers"),"maxCC")).ToArray() : [];
        var anyCc = Metadata(QuoteReferenceMetadata.Trusted(Answer(risk,"MTS-06-Q07"),"aadMaxMotorcycleCc"),"maxCC");
        var facts = QuoteCoverFacts.Reconcile(proposal).Facts; var specified = Items(At(risk,"specifiedVehicleIds")).Select(Text).ToHashSet(StringComparer.OrdinalIgnoreCase); var index = 0;
        foreach (var vehicle in Items(At(risk,"vehicles")))
        {
            var path = $"/risk/vehicles/{index++}"; void Add(string code,string field) => issues.Add(new(code,path + "/" + field));
            var type = QuoteReferenceMetadata.Trusted(At(vehicle,"vehicleType"),"vehicleType"); var category = Text(At(type,"category"));
            if (type.ValueKind == JsonValueKind.Undefined) { Add("vehicle-limit-type-context-required","vehicleType"); continue; }
            if (Number(At(vehicle,"abiGroup")) is { } group && !(category == "Commercial" && At(type,"requiresABICheckForCommercialVehicle").ValueKind == JsonValueKind.False))
            { if (!abi.Known) Add("vehicle-abi-context-required","abiGroup"); else if (group > abi.Maximum) Add("vehicle-abi-limit-exceeded","abiGroup"); }
            if (category != "Car" && Number(At(vehicle,"grossWeightKg")) is { } kg)
            { if (!weight.Known) Add("vehicle-gvw-context-required","grossWeightKg"); else if (kg > weight.Maximum) Add("vehicle-gvw-limit-exceeded","grossWeightKg"); }
            if (category == "Motorcycle")
            {
                if (!cc.Known) Add("vehicle-motorcycle-context-required","declaredEngineSize");
                else if (cc.Maximum == 0) Add("motorcycle-cover-not-selected","vehicleType");
                else if (cc.Maximum is not null && Text(At(vehicle,"declaredEngineSize")) is { } engine)
                {
                    if (!Regex.IsMatch(engine,@"^\d+(\.\d+)?$",RegexOptions.CultureInvariant) || !decimal.TryParse(engine,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var size)) Add("motorcycle-engine-size-invalid","declaredEngineSize");
                    else if (namedCc.Length > 0 && size > namedCc.Max(value => value.Maximum) || any && size > anyCc.Maximum) Add("motorcycle-cc-limit-exceeded","declaredEngineSize");
                }
            }
            if (!specified.Contains(Text(At(vehicle,"id"))) && Money(At(vehicle,"value")) is { } amount && facts.GetValueOrDefault("coverLevel") != "third-party-only")
            {
                if (!facts.TryGetValue("ownVehicleLimit",out var raw) || !decimal.TryParse(raw,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var maximum)) Add("vehicle-value-context-required","value");
                else if (amount > maximum) Add("vehicle-value-limit-exceeded","value");
            }
        }
        return issues;
    }
}
