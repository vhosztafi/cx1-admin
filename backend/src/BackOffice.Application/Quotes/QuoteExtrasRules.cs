using System.Text.Json;
using static BackOffice.Application.Quotes.QuoteSectionValues;

namespace BackOffice.Application.Quotes;

public static partial class QuoteCoverRules
{
    private static IReadOnlyList<QuoteFieldIssue> AssessExtras(JsonElement proposal)
    {
        var issues = new List<QuoteFieldIssue>(); var cover = At(proposal,"cover"); var drivers = Items(At(proposal,"risk.drivers")).ToArray();
        void Add(string code,QuoteSectionField field) => issues.Add(new(code,field.Path,field.QuestionId));
        QuoteSectionField Get(int number) => Answer(cover,"/cover",$"MTS-11-Q{number:00}");
        void Condition(int number,bool? active)
        {
            var field = Get(number);
            if (active == true && !Present(field.Value)) Add("extras-answer-required",field);
            else if (active != true && field.Value.ValueKind != JsonValueKind.Undefined) Add(active is null ? "extras-context-required" : "inactive-extras-answer-retained",field);
        }
        var level = QuoteCoverFacts.Reconcile(proposal).Facts.GetValueOrDefault("coverLevel");
        void Comprehensive(QuoteSectionField field,string family) { if (Boolean(At(Reference(field,family),"isComprehensive")) == true && level != "comprehensive") Add("extras-comprehensive-cover-required",field); }
        foreach (var number in new[] { 7,8,9 }) Condition(number,true);
        var demonstration = Boolean(Get(1).Value); Condition(2,demonstration);
        var motorcycle = drivers.Any(driver => Present(Answer(driver,"","MTS-06-Q26").Value));
        Condition(3,demonstration == false || !motorcycle ? false : demonstration);
        Comprehensive(Get(2),"demonstrationCovers"); var windscreen = Boolean(Get(4).Value);
        Condition(5,windscreen); Condition(6,windscreen);
        if (windscreen == true && level != "comprehensive") Add("extras-comprehensive-cover-required",Get(4));
        var plan = QuoteCatalogueIdentity.TrustedValue(Answer(At(proposal,"risk"),"/risk","MTS-06-Q01").Value,"driverPlans");
        var intent = At(proposal,"termIntent"); var term = QuoteTerm.Assess(intent);
        DateOnly? start = Date(Text(At(intent,"localStartDate")),out var from) ? from : null;
        DateOnly? end = term.Term is not null ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(term.Term.EndsAt,"Europe/London").DateTime) : null;
        foreach (var (key,number,required) in new[] { ("annualEuropeanCover",10,new[] { "registration","usage" }), ("temporaryEuropeanCover",13,new[] { "registration","startsOn","endsOn","area","cover","usage" }) })
        {
            var rows = Items(At(cover,key)).ToArray(); var selected = Boolean(Get(number).Value); var path = "/cover/" + key;
            if (selected == true && rows.Length == 0) Add("european-cover-row-required",new(default,path));
            if (selected != true && rows.Length > 0) Add(selected is null ? "european-cover-context-required" : "inactive-european-cover-retained",new(default,path));
            var seen = new HashSet<string>(StringComparer.Ordinal); var index = 0;
            foreach (var trip in rows)
            {
                var root = path + "/" + index++; QuoteSectionField Field(string name) => new(At(trip,name),root + "/" + name);
                foreach (var field in required) if (!Present(Field(field).Value)) Add("european-trip-field-required",Field(field));
                if (key == "annualEuropeanCover" && Text(Field("registration").Value) is { } registration && !seen.Add(registration.Replace(" ","",StringComparison.Ordinal).ToUpperInvariant())) Add("duplicate-registration",Field("registration"));
                var social = Boolean(At(Reference(Field("usage"),"europeanTripUsage"),"isSocialDomesticPleasure"));
                if (plan is null) Add("european-driver-plan-required",new(default,root));
                if (key == "temporaryEuropeanCover")
                {
                    var driverIds = Items(At(trip,"driverIds")).Select(Text).ToArray();
                    if (plan is not null and not 3 && driverIds.Length == 0) Add("european-trip-drivers-required",Field("driverIds"));
                    if (term.Term is null) Add("european-trip-term-required",new(default,root));
                    else
                    {
                        if (Date(Text(Field("startsOn").Value),out var tripStart) && tripStart < start) Add("european-trip-before-policy",Field("startsOn"));
                        if (Date(Text(Field("endsOn").Value),out var tripEnd) && tripEnd > end) Add("european-trip-after-policy",Field("endsOn"));
                    }
                    if (Date(Text(Field("startsOn").Value),out var begins) && Date(Text(Field("endsOn").Value),out var finishes) && finishes <= begins) Add("european-trip-end-must-follow-start",Field("endsOn"));
                    Comprehensive(Field("cover"),"europeanTripCover");
                    if (social == true && plan != 3) foreach (var id in driverIds)
                    {
                        var driver = drivers.FirstOrDefault(row => string.Equals(Text(At(row,"id")),id,StringComparison.OrdinalIgnoreCase));
                        var usage = QuoteReferenceMetadata.Trusted(At(driver,"usage"),"driverUsages");
                        if (usage.ValueKind == JsonValueKind.Undefined) Add("european-driver-usage-context-required",Field("driverIds"));
                        else if (Boolean(At(usage,"isSocialDomesticPleasure")) == false) Add("european-trip-usage-ineligible",Field("usage"));
                    }
                }
                if (social == true && plan == 3) Add("european-trip-usage-ineligible",Field("usage"));
            }
        }
        return issues;
    }
}
