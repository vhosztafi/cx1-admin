using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BackOffice.Application.Quotes;

public static partial class QuoteDriverRules
{
    private static readonly Lazy<JsonElement> References = new(() =>
    {
        _ = QuoteCatalogueIdentity.Version;
        using var stream = typeof(QuoteDriverRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("collections").Clone();
    });
    private static JsonElement TrustedMetadata(JsonElement reference,string collection)
    {
        var value = QuoteCatalogueIdentity.TrustedValue(reference,collection);
        return value is null ? default : References.Value.GetProperty(collection).EnumerateArray().First(row => Number(At(row,"value")) == value);
    }

    private static IReadOnlyList<QuoteFieldIssue> AssessEligibility(JsonElement proposal)
    {
        var issues = new List<QuoteFieldIssue>();
        var drivers = Items(At(proposal,"risk.drivers")).ToArray();
        var company = TrustedMetadata(At(proposal,"insured.declaredCompanyType"),"companyTypes");
        var policyholders = drivers.Count(driver => QuoteCatalogueIdentity.TrustedValue(At(driver,"relationship"),"driverRelationshipsPolicyHolder") == 3);
        for (var index = 0; index < drivers.Length; index++)
        {
            var driver = drivers[index]; var root = $"/risk/drivers/{index}";
            void Add(string code,string field) { if (issues.Count < QuoteCaptureShape.MaximumIssues) issues.Add(new(code,root + "/" + field)); }
            JsonElement Read(int number) => Answer(At(driver,"responses"),root + "/responses",Id(number)).Value;
            void AddAnswer(string code,int number)
            {
                if (issues.Count < QuoteCaptureShape.MaximumIssues)
                    issues.Add(new(code,Answer(At(driver,"responses"),root + "/responses",Id(number)).Path,Id(number)));
            }
            var relationship = QuoteCatalogueIdentity.TrustedValue(At(driver,"relationship"),"driverRelationshipsPolicyHolder");
            var usage = QuoteCatalogueIdentity.TrustedValue(At(driver,"usage"),"driverUsages");
            var motorcycle = QuoteCatalogueIdentity.TrustedValue(Read(25),"driverMotorcycleCovers");
            var start = At(proposal,"termIntent.localStartDate"); var age = Years(At(driver,"dateOfBirth"),start);
            if (company.ValueKind == JsonValueKind.Undefined || relationship is null) Add("driver-relationship-context-required","relationship");
            else if (!Items(At(company,"relationshipOptions")).Any(item => Number(item) == relationship)) Add("driver-relationship-ineligible","relationship");
            if (relationship == 5 && age is < 25) Add("spouse-under-25","relationship");
            if (relationship == 3 && policyholders > 1) Add("duplicate-policyholder-driver","relationship");
            if (motorcycle == 6)
            {
                var experience = Years(Read(26),start);
                if (age is null || experience is null) AddAnswer("motorcycle-eligibility-context-required",26);
                else if (age < 30 || experience < 2) AddAnswer("motorcycle-over-1000-ineligible",25);
            }
            var personal = Read(31); var other = Read(32);
            if ((personal.ValueKind != JsonValueKind.Undefined || other.ValueKind != JsonValueKind.Undefined) && usage is null) Add("driver-usage-context-required","usage");
            if (personal.ValueKind == JsonValueKind.True && usage == 1) AddAnswer("personal-cover-motor-trade-only",31);
            if (personal.ValueKind == JsonValueKind.False && relationship is 1 or 4 or 3 or 5 && usage is not null and not 1) AddAnswer("personal-cover-required-for-relationship",31);
            if (other.ValueKind == JsonValueKind.True && usage == 1) AddAnswer("other-cover-motor-trade-only",32);
            if (other.ValueKind == JsonValueKind.True && age is < 21) AddAnswer("other-cover-under-21",32);
            if (other.ValueKind == JsonValueKind.False && age is >= 21 && relationship is 1 or 4 or 3 && usage is not null and not 1) AddAnswer("other-cover-required-for-relationship",32);
            var child = 0;
            foreach (var conviction in Items(At(driver,"convictions")))
            {
                var field = $"convictions/{child++}/banMonths";
                if (At(conviction,"disqualified").ValueKind != JsonValueKind.True || Number(At(conviction,"banMonths")) is not { } months || !Date(Text(At(conviction,"occurredOn")),out var occurred)) continue;
                if (!Date(Text(start),out var inception)) { Add("ban-policy-start-required",field); continue; }
                // Draft shape bounds months to0..1200, but a valid year9999 date
                // can still overflow DateOnly. Such a ban is after any valid term.
                var monthIndex = (long)occurred.Year * 12 + occurred.Month - 1 + months;
                if (monthIndex > 9999L * 12 + 11 || (months >= 0 && months <= int.MaxValue && occurred.AddMonths((int)months) > inception)) Add("driver-ban-active-at-policy-start",field);
            }
        }
        return issues;
    }

    private static IReadOnlyList<QuoteFieldIssue> AssessDeclarations(JsonElement proposal)
    {
        var issues = new List<QuoteFieldIssue>(); var index = 0;
        foreach (var driver in Items(At(proposal,"risk.drivers")))
        {
            var root = $"/risk/drivers/{index++}";
            void Add(string code,string path) { if (issues.Count < QuoteCaptureShape.MaximumIssues) issues.Add(new(code,path)); }
            (JsonElement Value,string Path) Read(string id) => Answer(At(driver,"responses"),root + "/responses",id);
            string Normalize(string value) => Regex.Replace(value.Trim(),@"\s+"," ").ToLowerInvariant();
            var full = Text(At(driver,"fullName")); var first = Text(At(driver,"firstName")); var surname = Text(At(driver,"surname"));
            if (!string.IsNullOrEmpty(full) && !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(surname) && Normalize(full) != Normalize(first + " " + surname)) Add("conflicting-driver-name",root + "/fullName");
            var prototype = Read("prototype.adddriver.trade-employment"); var source = Read(Id(28));
            var declared = QuoteCatalogueIdentity.TrustedValue(prototype.Value,"prototype.adddriver.trade-employment");
            var employment = QuoteCatalogueIdentity.TrustedValue(source.Value,"driverTradeEmploymentBasises");
            if (prototype.Value.ValueKind != JsonValueKind.Undefined && source.Value.ValueKind != JsonValueKind.Undefined)
            {
                if (declared is null || employment is null) Add("driver-employment-context-required",prototype.Path);
                else if (declared != employment) { Add("conflicting-driver-employment",prototype.Path); Add("conflicting-driver-employment",source.Path); }
            }
            var other = Read("prototype.adddriver.other-occupation"); var partTime = declared == 2 || employment == 2;
            if (partTime && prototype.Value.ValueKind != JsonValueKind.Undefined && !Present(other.Value)) Add("prototype-other-occupation-required",other.Path);
            if (other.Value.ValueKind != JsonValueKind.Undefined && !partTime) Add(declared is null && employment is null ? "driver-employment-context-required" : "inactive-prototype-other-occupation",other.Path);
            var residency = Read(Id(21)).Value.ValueKind;
            var residenceDate = residency == JsonValueKind.True ? At(driver,"dateOfBirth") : residency == JsonValueKind.False ? Read(Id(22)).Value : default;
            var licenceDate = QuoteCatalogueIdentity.TrustedValue(At(driver,"licence.type"),"driverLicenceTypes") is 1 or 2 ? At(driver,"licence.issuedOn") : default;
            foreach (var (id,date) in new[] { ("prototype.adddriver.residency-years",residenceDate),("prototype.adddriver.licence-years",licenceDate) })
            {
                var field = Read(id); if (field.Value.ValueKind == JsonValueKind.Undefined) continue;
                var years = Years(date,At(proposal,"termIntent.localStartDate"));
                if (years is null) Add("driver-years-context-required",field.Path);
                else if (Number(field.Value) != years) Add("conflicting-driver-years",field.Path);
            }
        }
        return issues;
    }
    private static bool Date(string? value,out DateOnly date) => DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date);
}
