using System.Text.Json;

namespace BackOffice.Application.Quotes;

public static partial class QuoteVehicleRules
{
    private static IReadOnlyList<QuoteFieldIssue> AssessPlates(JsonElement proposal)
    {
        var issues = new List<QuoteFieldIssue>(); var risk = At(proposal,"risk");
        void Add(string code,string path,string? question = null) => issues.Add(new(code,path,question));
        const string heldId = "prototype.quote-value.315960b57ab1";
        var held = Answer(At(risk,"business"),heldId).ValueKind; var covered = Answer(risk,"MTS-07-Q01").ValueKind;
        var countValue = Answer(risk,"MTS-07-Q02"); var count = Number(countValue);
        var rows = Items(At(risk,"heldTradePlates")).ToArray(); var plates = Items(At(risk,"tradePlates")).ToArray();
        if (held == JsonValueKind.Undefined) Add("trade-plates-held-answer-required","/risk/business/responses/answers",heldId);
        if (held == JsonValueKind.True && rows.Length == 0) Add("held-trade-plate-inventory-required","/risk/heldTradePlates");
        if (held != JsonValueKind.True && rows.Length > 0) Add("inactive-held-trade-plates","/risk/heldTradePlates");
        var heldNumbers = new HashSet<string>(StringComparer.Ordinal); var index = 0;
        foreach (var row in rows)
        {
            var path = $"/risk/heldTradePlates/{index++}/number"; var number = Text(At(row,"number"));
            if (string.IsNullOrWhiteSpace(number)) Add("held-trade-plate-number-required",path);
            else if (!heldNumbers.Add(Normalize(number))) Add("duplicate-held-trade-plate",path);
        }
        if (covered == JsonValueKind.Undefined) Add("trade-plate-cover-answer-required","/risk/responses/answers","MTS-07-Q01");
        if (covered == JsonValueKind.True && held != JsonValueKind.True) Add("covered-trade-plates-require-held-declaration","/risk/responses/answers","MTS-07-Q01");
        if (plates.Length > 150) Add("trade-plate-row-limit","/risk/tradePlates");
        if (covered == JsonValueKind.False && (plates.Length > 0 || Present(countValue))) Add("inactive-trade-plate-data","/risk/tradePlates");
        if (covered == JsonValueKind.True && (count is null or < 1 or > 999)) Add("trade-plate-count-required","/risk/responses/answers","MTS-07-Q02");
        if (count < plates.Length) Add("trade-plate-count-below-rows","/risk/tradePlates");
        var numbers = new HashSet<string>(StringComparer.Ordinal); index = 0;
        foreach (var row in plates)
        {
            var path = $"/risk/tradePlates/{index++}/number"; var number = Text(At(row,"number"));
            if (string.IsNullOrWhiteSpace(number)) { Add("trade-plate-number-required",path); continue; }
            number = Normalize(number);
            if (!numbers.Add(number)) Add("duplicate-registration",path);
            if (!heldNumbers.Contains(number)) Add("covered-trade-plate-not-held",path);
        }
        return issues;
    }
    private static readonly (int Parent,int Share)[] Shares = [(1,2),(3,4),(5,6),(7,8),(9,10),(11,12),(13,14),(15,16),(17,18),(19,20),(22,23),(24,25),(26,27),(28,29),(30,31),(33,34),(36,37)];
    private static IReadOnlyList<QuoteFieldIssue> AssessPortfolio(JsonElement proposal)
    {
        var issues = new List<QuoteFieldIssue>(); var risk = At(proposal,"risk");
        string Id(int number) => $"MTS-10-Q{number:00}";
        JsonElement Value(int number) => Answer(risk,Id(number));
        void Add(string code,int? number = null)
        {
            var id = number is null ? null : Id(number.Value); var rows = Items(At(risk,"responses.answers")).ToArray();
            var index = Array.FindIndex(rows,row => Text(At(row,"questionId")) == id);
            issues.Add(new(code,"/risk/responses/answers" + (index < 0 ? "" : $"/{index}/value"),id));
        }
        long total = 0; var selected = 0; var invalid = false;
        foreach (var (parent,share) in Shares)
        {
            var active = Value(parent).ValueKind == JsonValueKind.True; var value = Number(Value(share));
            if (active)
            {
                selected++;
                if (value is null or <= 0 or > 10000) { Add("positive-portfolio-percentage-required",share); invalid = true; }
                else { total += value.Value; if (value < 100 && share is not (4 or 8)) Add("portfolio-minimum-one-percent",share); }
            }
            if (!active && Present(Value(share))) Add(Present(Value(parent)) ? "inactive-answer-retained" : "controlling-answer-required",share);
        }
        if (selected == 0) Add("vehicle-category-required"); else if (!invalid && total != 10000) Add("portfolio-total-must-equal-100-percent");
        foreach (var (parent,child) in new[] { (19,21),(30,32),(33,35) })
        {
            if (Value(parent).ValueKind == JsonValueKind.True && !Present(Value(child))) Add("conditional-answer-required",child);
            else if (Value(parent).ValueKind != JsonValueKind.True && Present(Value(child))) Add(Present(Value(parent)) ? "inactive-answer-retained" : "controlling-answer-required",child);
        }
        if (Value(19).ValueKind == JsonValueKind.True && Number(Value(21)) is < 3) Add("transporter-minimum-three-vehicles",21);
        var drivers = Items(At(risk,"drivers")).ToArray(); var plan = QuoteCatalogueIdentity.TrustedValue(Answer(risk,"MTS-06-Q01"),"driverPlans");
        if (Value(3).ValueKind == JsonValueKind.True)
        {
            var groups = new List<long>(); var complete = plan is not null;
            if (plan is 1 or 2)
            {
                if (drivers.Length == 0) complete = false;
                foreach (var driver in drivers) { var group = Abi(QuoteReferenceMetadata.Trusted(Answer(driver,"MTS-06-Q27"),"driverMaxABIGroups")); if (group is null) complete = false; else groups.Add(group.Value); }
            }
            if (plan is 2 or 3) { var group = Number(At(QuoteReferenceMetadata.Trusted(Answer(risk,"MTS-06-Q05"),"aadMaxVehicleGrouping"),"abiGroup")); if (group is null) complete = false; else groups.Add(group.Value); }
            if (!complete) Add("sports-portfolio-context-required",3); else if (!groups.Any(value => value > 28)) Add("sports-portfolio-ineligible",3);
        }
        if (Value(7).ValueKind == JsonValueKind.True)
        {
            var named = drivers.Any(driver => Present(Answer(driver,"MTS-06-Q26")));
            var motorcycle = QuoteCatalogueIdentity.TrustedValue(Answer(risk,"MTS-06-Q07"),"aadMaxMotorcycleCc");
            if (!named && (plan is null || plan is 2 or 3 && motorcycle is null)) Add("motorcycle-portfolio-context-required",7);
            else if (!named && !(plan != 1 && motorcycle is not null and not 1)) Add("motorcycle-portfolio-ineligible",7);
        }
        var specified = Items(At(risk,"specifiedVehicleIds")).Select(Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var vehicle in Items(At(risk,"vehicles")))
        {
            var type = QuoteReferenceMetadata.Trusted(At(vehicle,"vehicleType"),"vehicleType");
            var checks = new[] { (5,Text(At(type,"category")) == "Commercial" && Number(At(vehicle,"grossWeightKg")) > 3500), (11,At(vehicle,"imported").ValueKind == JsonValueKind.True),
                (13,!specified.Contains(Text(At(vehicle,"id"))) && Items(At(vehicle,"modifications")).Any()), (17,At(type,"requiresRallyTrackKitCarsTrikes").ValueKind == JsonValueKind.True),
                (22,At(type,"requiresQuadBikes").ValueKind == JsonValueKind.True),(26,Number(At(vehicle,"seats")) > 7) };
            foreach (var (number,active) in checks) if (active && Value(number).ValueKind != JsonValueKind.True && !issues.Any(i => i.Code == "portfolio-declaration-conflicts-with-vehicle" && i.QuestionId == Id(number))) Add("portfolio-declaration-conflicts-with-vehicle",number);
        }
        return issues;
    }
    private static long? Abi(JsonElement row) => Text(At(row,"text")) is { } label && label.StartsWith("Group ",StringComparison.Ordinal) && long.TryParse(label[6..],out var value) ? value : null;
}
