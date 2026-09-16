using System.Globalization;
using System.Text.Json;
using static BackOffice.Application.Quotes.QuoteSectionValues;

namespace BackOffice.Application.Quotes;

public static partial class QuoteCoverRules
{
    public static IReadOnlyList<QuoteFieldIssue> Assess(JsonElement proposal)
    {
        var issues = new List<QuoteFieldIssue>(); var cover = At(proposal,"cover"); var facts = QuoteCoverFacts.Reconcile(proposal).Facts;
        void Add(string code,QuoteSectionField field) { if (issues.Count < QuoteCaptureShape.MaximumIssues) issues.Add(new(code,field.Path,field.QuestionId)); }
        QuoteSectionField Get(string id) => Answer(cover,"/cover",id);
        QuoteSectionField Source(int number) => Get($"MTS-05-Q{number:00}");
        void Condition(int number,bool? active)
        {
            var field = Source(number);
            if (active == true && !Present(field.Value)) Add("cover-answer-required",field);
            else if (active != true && field.Value.ValueKind != JsonValueKind.Undefined) Add(active is null ? "cover-context-required" : "inactive-cover-answer-retained",field);
        }
        var level = facts.GetValueOrDefault("coverLevel"); bool? limits = level is null ? null : level != "third-party-only";
        if (level is null) Add("cover-level-required",Source(1));
        var activities = Items(At(proposal,"risk.business.activities")).Select(row => QuoteReferenceMetadata.Trusted(At(row,"code"),"mtOccupations")).ToArray();
        var complete = activities.Length > 0 && activities.All(row => row.ValueKind != JsonValueKind.Undefined);
        bool? customerRequired = limits == false ? false : limits is null || !complete ? null : activities.Any(row => Boolean(At(row,"customerLOI")) == true);
        void Fact(string name,bool? active,int source,string prototype)
        {
            if (active == true && !facts.ContainsKey(name)) Add("cover-fact-required",Source(source));
            if (active != true) foreach (var field in new[] { Source(source),Get("prototype.quote." + prototype) }) if (field.Value.ValueKind != JsonValueKind.Undefined) Add(active is null ? "cover-context-required" : "inactive-cover-answer-retained",field);
        }
        Fact("ownVehicleLimit",limits,2,"d9dd069a314c"); Fact("excess",limits,4,"00216de47ab5"); Fact("customerVehicleLimit",customerRequired,3,"b4c7e25f7781");
        if (limits == true && !complete) Add("cover-activity-context-required",new(default,"/risk/business/activities"));
        foreach (var number in new[] { 5,6,7 }) Condition(number,limits);
        if (limits == true)
        {
            if (Present(Source(5).Value) && Boolean(Source(5).Value) != false) Add("unsupported-all-sections-excess",Source(5));
            if (Number(At(Reference(Source(6),"excessLates"),"value")) is not null and not 1) Add("unsupported-late-notification-excess",Source(6));
        }
        if (level == "third-party-fire-theft") foreach (var (name,number) in new[] { ("ownVehicleLimit",2),("customerVehicleLimit",3) })
            if (facts.TryGetValue(name,out var amount) && decimal.TryParse(amount,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value) && value > 15000) Add("fire-theft-limit-exceeded",Source(number));
        Condition(8,true); var loan = Boolean(Source(8).Value); Condition(9,loan);
        if (loan == true && Boolean(At(Reference(Source(9),"customerLoanCoverLevels"),"isComprehensive")) == true && level != "comprehensive") Add("customer-loan-cover-ineligible",Source(9));
        var index = 0;
        foreach (var vehicle in Items(At(proposal,"risk.vehicles")))
        { if (Boolean(At(vehicle,"customerLoan")) == true && loan != true) Add(loan is null ? "customer-loan-context-required" : "customer-loan-cover-required",new(default,$"/risk/vehicles/{index}/customerLoan")); index++; }
        var demo = Get("prototype.quote.becc2653d0de"); var sourceDemo = Get("MTS-11-Q01"); var privateUse = Get("prototype.quote.4c5df77ffba1");
        var courtesy = Answer(At(proposal,"risk.business"),"/risk/business","prototype.quote.af67cb40b4de");
        foreach (var field in new[] { demo,privateUse,courtesy }) if (!Present(field.Value)) Add("required-prototype-cover-answer",field);
        if (Boolean(demo.Value) is { } demoValue && Boolean(sourceDemo.Value) is { } sourceValue && demoValue != sourceValue) { Add("conflicting-demonstration-cover",demo); Add("conflicting-demonstration-cover",sourceDemo); }
        if (Boolean(demo.Value) == true && !Present(sourceDemo.Value)) Add("demonstration-cover-context-required",sourceDemo);
        var courtesyOption = Number(At(Reference(courtesy,courtesy.QuestionId!),"value"));
        if (courtesyOption is not null && loan is not null && (courtesyOption == 2) != loan) { Add("conflicting-courtesy-cover",courtesy); Add("conflicting-courtesy-cover",Source(8)); }
        var drivers = Items(At(proposal,"risk.drivers")).ToArray(); var usages = drivers.Select(driver => QuoteReferenceMetadata.Trusted(At(driver,"usage"),"driverUsages")).ToArray();
        var plan = QuoteCatalogueIdentity.TrustedValue(Answer(At(proposal,"risk"),"/risk","MTS-06-Q01").Value,"driverPlans");
        if (Boolean(privateUse.Value) == false) for (var driver = 0; driver < usages.Length; driver++) if (Boolean(At(usages[driver],"isSocialDomesticPleasure")) == true) { Add("conflicting-private-use",privateUse); Add("conflicting-private-use",new(default,$"/risk/drivers/{driver}/usage")); }
        if (Boolean(privateUse.Value) == true && plan == 1 && usages.Length > 0 && usages.All(row => row.ValueKind != JsonValueKind.Undefined) && !usages.Any(row => Boolean(At(row,"isSocialDomesticPleasure")) == true)) Add("private-use-driver-required",privateUse);
        return issues.Concat(AssessExtras(proposal)).Take(QuoteCaptureShape.MaximumIssues).ToArray();
    }
}
