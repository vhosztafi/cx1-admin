using System.Text.Json;
using static BackOffice.Application.Quotes.QuoteSectionValues;

namespace BackOffice.Application.Quotes;

public static class QuoteAdditionalRules
{
    public static IReadOnlyList<QuoteFieldIssue> Assess(JsonElement proposal)
    {
        var issues = new List<QuoteFieldIssue>(); var risk = At(proposal,"risk");
        void Add(string code,QuoteSectionField field) => issues.Add(new(code,field.Path,field.QuestionId));
        var rows = Items(At(risk,"business.activities")).Select(row => QuoteReferenceMetadata.Trusted(At(row,"code"),"mtOccupations")).ToArray();
        var complete = rows.Length > 0 && rows.All(row => row.ValueKind != JsonValueKind.Undefined);
        bool? jockey = complete ? rows.Any(row => Boolean(At(row,"requireCarJockeyRadius")) == true) : null;
        var radius = Answer(risk,"/risk","MTS-13-Q02");
        if (jockey == true && !Present(radius.Value)) Add("car-jockey-radius-required",radius);
        if (jockey != true && Present(radius.Value)) Add(jockey is null ? "additional-activity-context-required" : "inactive-car-jockey-radius-retained",radius);
        var shunter = Answer(risk,"/risk","MTS-13-Q03");
        if (Present(shunter.Value))
        {
            if (!complete) Add("additional-activity-context-required",shunter);
            else if (rows.Length != 1 || Boolean(At(rows[0],"requireShunterRadius")) != true) Add("inactive-shunter-radius-retained",shunter);
            else
            {
                var plan = QuoteCatalogueIdentity.TrustedValue(Answer(risk,"/risk","MTS-06-Q01").Value,"driverPlans");
                var usages = Items(At(risk,"drivers")).Select(driver => QuoteReferenceMetadata.Trusted(At(driver,"usage"),"driverUsages")).ToArray();
                var known = usages.Length > 0 && usages.All(row => row.ValueKind != JsonValueKind.Undefined);
                bool? driverEligible = plan == 3 ? true : plan is null || !known ? null : usages.All(row => Boolean(At(row,"isSocialDomesticPleasure")) == false);
                var facts = QuoteCoverFacts.Reconcile(proposal).Facts;
                bool? coverEligible = facts.GetValueOrDefault("coverLevel") == "third-party-only" || facts.GetValueOrDefault("ownVehicleLimit") == "0.00" ? true : !facts.ContainsKey("coverLevel") || !facts.ContainsKey("ownVehicleLimit") ? null : false;
                if (driverEligible == false || coverEligible == false) Add("inactive-shunter-radius-retained",shunter);
                else if (driverEligible is null || coverEligible is null) Add("shunter-eligibility-context-required",shunter);
            }
        }
        var combined = Text(At(proposal,"productCode")) == "motor-trade-combined"; var index = 0;
        foreach (var premise in Items(At(risk,"premises")))
        {
            var root = $"/risk/premises/{index++}";
            foreach (var field in new[] { "declaredUse","security" })
            {
                if (combined && !Present(At(premise,field))) Add("required-prototype-reference",new(default,root + "/" + field));
                if (!combined && Present(At(premise,field))) Add("inapplicable-prototype-reference",new(default,root + "/" + field));
            }
            if (combined) foreach (var id in new[] { "prototype.addprem.overnight-vehicles","prototype.addprem.public-access" })
            { var answer = Answer(premise,root,id); if (!Present(answer.Value)) Add("required-prototype-detail-answer",answer); }
        }
        return issues.Take(QuoteCaptureShape.MaximumIssues).ToArray();
    }
}
