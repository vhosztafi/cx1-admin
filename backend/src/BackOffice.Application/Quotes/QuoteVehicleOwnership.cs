using System.Text.Json;

namespace BackOffice.Application.Quotes;

// Cross-section prerequisite for driver personal-vehicle cover. Vehicle capture
// uses the same assessment when its own editor is added in05-05.
public static class QuoteVehicleOwnership
{
    public static IReadOnlyList<QuoteFieldIssue> Assess(JsonElement proposal)
    {
        var issues = new List<QuoteFieldIssue>(); var risk = At(proposal,"risk");
        var drivers = Items(At(risk,"drivers")).ToArray();
        var company = QuoteCatalogueIdentity.TrustedValue(At(proposal,"insured.declaredCompanyType"),"companyTypes");
        var specified = Items(At(risk,"specifiedVehicleIds")).Select(Text).Where(value => value is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var vehicle in Items(At(risk,"vehicles")))
        {
            var path = $"/risk/vehicles/{index++}";
            void Add(string code,string field) { if (issues.Count < QuoteCaptureShape.MaximumIssues) issues.Add(new(code,path + "/" + field)); }
            var owner = QuoteCatalogueIdentity.TrustedValue(At(vehicle,"declaredOwnerType"),"vehicleOwnerTypes");
            if (company is null || owner is null) { Add("vehicle-owner-context-required","declaredOwnerType"); continue; }
            var driverId = Text(At(vehicle,"ownerDriverId"));
            if (owner != 4)
            {
                var allowed = company == 1 ? new long[] { 1 } : company == 4 ? [2] : [2,3];
                if (!allowed.Contains(owner.Value)) Add("vehicle-owner-type-ineligible","declaredOwnerType");
                if (driverId is not null) Add("inactive-owner-driver-retained","ownerDriverId");
                continue;
            }
            if (driverId is null) { Add("vehicle-owner-driver-required","ownerDriverId"); continue; }
            var driver = drivers.FirstOrDefault(row => string.Equals(Text(At(row,"id")),driverId,StringComparison.OrdinalIgnoreCase));
            if (driver.ValueKind == JsonValueKind.Undefined) { Add("vehicle-owner-driver-not-in-proposal","ownerDriverId"); continue; }
            var relationship = QuoteCatalogueIdentity.TrustedValue(At(driver,"relationship"),"driverRelationshipsPolicyHolder");
            if (relationship is null) { Add("vehicle-owner-relationship-context-required","ownerDriverId"); continue; }
            if (!specified.Contains(Text(At(vehicle,"id"))) && company == 1 && relationship == 3) Add("policyholder-driver-owner-not-selectable","ownerDriverId");
            var answer = Items(At(driver,"responses.answers")).FirstOrDefault(row => Text(At(row,"questionId")) == "MTS-06-Q31");
            var personal = At(answer,"value").ValueKind;
            if (personal != JsonValueKind.True) Add(personal == JsonValueKind.Undefined ? "vehicle-owner-cover-context-required" : "vehicle-owner-personal-cover-required","ownerDriverId");
        }
        return issues;
    }
    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static IEnumerable<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    private static JsonElement At(JsonElement value,string path) { foreach (var part in path.Split('.')) if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part,out value)) return default; return value; }
}
