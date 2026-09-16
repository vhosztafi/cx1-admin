using System.Globalization;
using System.Text.Json;

namespace BackOffice.Application.Quotes;

// Driver section rules, after strict draft and catalogue validation.
// Cover-dependent dynamic options and global history reconciliation have separate owners.
public static partial class QuoteDriverRules
{
    private static readonly Lazy<JsonElement[]> Mappings = new(() =>
    {
        _ = QuoteCatalogueIdentity.Version;
        using var stream = typeof(QuoteDriverRules).Assembly.GetManifestResourceStream("QuoteCapture.Questions")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("mappings").EnumerateArray().Select(row => row.Clone()).ToArray();
    });
    private static readonly int[] Required = [8,9,10,11,12,13,14,15,18,19,20,21,23,24,25,27,28,31,32,33,40,46,48,53];
    private sealed record History(string Key, int Parent, string? Collection, int[] Fields);
    private static readonly History[] Histories = [new("occupations",28,"driverTradeEmploymentBasises",[29,30]),
        new("convictions",33,null,[34,35,36,37,38]), new("losses",40,null,[41,42,43,44,45]),
        new("criminalConvictions",48,null,[49,50,51,52]), new("countyCourtJudgments",53,null,[54,55,56,57])];

    public static IReadOnlyList<QuoteFieldIssue> Assess(JsonElement proposal)
    {
        var product = Text(At(proposal,"productCode"));
        if (product is not ("motor-trade-road-risks" or "motor-trade-combined")) return [new("unsupported-capture-product","/productCode")];
        var mappings = Mappings.Value.Where(row => row.GetProperty("products").EnumerateArray().Any(item => item.GetString() == product)).ToArray();
        JsonElement Mapping(int number) => mappings.First(row => Text(At(row,"owner")) == Id(number));
        var issues = new List<QuoteFieldIssue>();
        void Add(string code,string path,int? number = null) { if (issues.Count < QuoteCaptureShape.MaximumIssues) issues.Add(new(code,path,number is null ? null : Id(number.Value))); }
        var drivers = Items(At(proposal,"risk.drivers")).ToArray();
        var selection = Answer(At(proposal,"risk.responses"),"/risk/responses",Id(1));
        var plan = QuoteCatalogueIdentity.TrustedValue(selection.Value,"driverPlans");
        if (plan is null) Add(selection.Value.ValueKind == JsonValueKind.Undefined ? "driver-plan-required" : "driver-plan-context-required",selection.Path,1);
        for (var number = 2; number <= 7; number++)
        {
            var field = Answer(At(proposal,"risk.responses"),"/risk/responses",Id(number));
            if (plan is 2 or 3 && field.Value.ValueKind == JsonValueKind.Undefined) Add("any-driver-answer-required",field.Path,number);
            else if (plan is not (2 or 3) && field.Value.ValueKind != JsonValueKind.Undefined) Add(plan is null ? "any-driver-context-required" : "inactive-any-driver-answer-retained",field.Path,number);
            if (number == 2 && plan is 2 or 3 && field.Value.ValueKind != JsonValueKind.Undefined && Number(field.Value) is null or < 1) Add("positive-any-driver-count-required",field.Path,number);
        }
        if (plan is 1 or 2 && drivers.Length == 0) Add("named-driver-required","/risk/drivers");
        if (drivers.Length > 0 && plan is null or 3) Add(plan is null ? "named-driver-plan-context-required" : "inactive-named-drivers-retained","/risk/drivers");

        for (var index = 0; index < drivers.Length; index++)
        {
            var driver = drivers[index]; var root = $"/risk/drivers/{index}";
            (JsonElement Value,string Path) Read(int number)
            {
                var mapping = Mapping(number);
                if (Text(At(mapping,"contractKind")) == "Answer") return Answer(At(driver,"responses"),root + "/responses",Id(number));
                var path = mapping.GetProperty("canonicalPath").GetString()!["risk.drivers[].".Length..];
                return (At(driver,path),root + "/" + path.Replace('.','/'));
            }
            void Require(int number) { var field = Read(number); if (!Present(field.Value)) Add("required-driver-field",field.Path,number); }
            foreach (var number in Required) Require(number);
            var start = At(proposal,"termIntent.localStartDate"); var age = Years(At(driver,"dateOfBirth"),start);
            if (age is < 17 or > 85) Add("driver-age-out-of-range",root + "/dateOfBirth",11);
            if (Read(21).Value.ValueKind == JsonValueKind.False) Require(22);
            var motorcycle = QuoteCatalogueIdentity.TrustedValue(Read(25).Value,"driverMotorcycleCovers");
            if (motorcycle is not null and not 1) Require(26);
            if (Present(Read(26).Value) && Present(At(driver,"licence.issuedOn")) && string.CompareOrdinal(Text(Read(26).Value),Text(At(driver,"licence.issuedOn"))) < 0)
                Add("motorcycle-licence-before-driving-licence",Read(26).Path,26);
            if (Read(46).Value.ValueKind == JsonValueKind.True) Require(47);
            if (QuoteCatalogueIdentity.TrustedValue(At(driver,"licence.type"),"driverLicenceTypes") == 3) Add("provisional-licence-not-covered",root + "/licence/type",23);
            if (age is < 25 && Years(At(driver,"licence.issuedOn"),start) is < 1) Add("young-driver-licence-experience",root + "/licence/issuedOn",24);
            foreach (var number in new[] {11,22,24,26}) { var field = Read(number); if (Present(field.Value) && string.CompareOrdinal(Text(field.Value),"1900-01-01") < 0) Add("driver-date-too-early",field.Path,number); }
            foreach (var group in Histories)
            {
                var parent = Read(group.Parent).Value; var trusted = group.Collection is null ? null : QuoteCatalogueIdentity.TrustedValue(parent,group.Collection);
                var active = group.Collection is null ? parent.ValueKind == JsonValueKind.True : trusted == 2;
                var known = group.Collection is null ? parent.ValueKind is JsonValueKind.True or JsonValueKind.False : trusted is not null;
                var rows = Items(At(driver,group.Key)).ToArray(); var path = root + "/" + group.Key;
                if (!active) { if (rows.Length > 0) Add(known ? "inactive-driver-history-retained" : "driver-history-context-required",path,group.Parent); continue; }
                if (rows.Length == 0) Add("driver-history-required",path,group.Parent);
                var occupations = new HashSet<(string?,long?)>();
                for (var child = 0; child < rows.Length; child++)
                {
                    var row = rows[child]; var itemPath = path + "/" + child;
                    foreach (var number in group.Fields)
                    {
                        var field = Mapping(number).GetProperty("canonicalPath").GetString()!.Split(group.Key + "[].",StringSplitOptions.None)[1];
                        if (!Present(At(row,field))) Add("required-driver-history-field",itemPath + "/" + field.Replace('.','/'),number);
                    }
                    if (Present(At(row,"occurredOn")) && string.CompareOrdinal(Text(At(row,"occurredOn")),"1900-01-01") < 0) Add("driver-date-too-early",itemPath + "/occurredOn");
                    if (group.Key == "convictions" && At(row,"disqualified").ValueKind == JsonValueKind.True && !Present(At(row,"banMonths"))) Add("ban-length-required",itemPath + "/banMonths",39);
                    if (group.Key == "occupations" && At(row,"occupation").ValueKind == JsonValueKind.Object && !occupations.Add((Text(At(row,"occupation.collection")),Number(At(row,"occupation.value"))))) Add("duplicate-driver-occupation",itemPath + "/occupation",29);
                }
            }
        }
        return issues.Concat(AssessEligibility(proposal)).Concat(AssessDeclarations(proposal)).Take(QuoteCaptureShape.MaximumIssues).ToArray();
    }
    private static string Id(int number) => $"MTS-06-Q{number:00}";
    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
    private static bool Present(JsonElement value) => value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) && (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));
    private static IEnumerable<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    private static JsonElement At(JsonElement value,string path) { foreach (var part in path.Split('.')) if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part,out value)) return default; return value; }
    private static int? Years(JsonElement date,JsonElement at)
    {
        if (!DateOnly.TryParseExact(Text(date),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var from) || !DateOnly.TryParseExact(Text(at),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var to) || from > to) return null;
        var years = to.Year - from.Year;
        return years - (from.AddYears(years) > to ? 1 : 0);
    }
    private static (JsonElement Value,string Path) Answer(JsonElement responses,string root,string id)
    {
        var index = 0;
        foreach (var answer in Items(At(responses,"answers"))) { if (Text(At(answer,"questionId")) == id) return (At(answer,"value"),$"{root}/answers/{index}/value"); index++; }
        return (default,root + "/answers");
    }
}
