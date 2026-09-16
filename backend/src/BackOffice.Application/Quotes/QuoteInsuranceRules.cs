using System.Text.Json;
using System.Text.RegularExpressions;
using static BackOffice.Application.Quotes.QuoteSectionValues;

namespace BackOffice.Application.Quotes;

// Capture consistency only: declared NCB is not verified proof or entitlement.
public static class QuoteInsuranceRules
{
    public static IReadOnlyList<QuoteFieldIssue> Assess(JsonElement proposal)
    {
        var issues = new List<QuoteFieldIssue>(); var insurance = At(proposal,"risk.previousInsurance"); const string root = "/risk/previousInsurance";
        void Add(string code,QuoteSectionField field) { if (issues.Count < QuoteCaptureShape.MaximumIssues) issues.Add(new(code,field.Path,field.QuestionId)); }
        QuoteSectionField Field(string name) => new(At(insurance,name),root + "/" + name);
        QuoteSectionField Get(int number) => number == 12 ? Field("noClaimsBonusExpiresOn") with { QuestionId = "MTS-05-Q12" } : Answer(insurance,root,$"MTS-05-Q{number:00}");
        void Condition(int number,bool? active)
        {
            var field = Get(number);
            if (active == true && !Present(field.Value)) Add("insurance-answer-required",field);
            else if (active != true && field.Value.ValueKind != JsonValueKind.Undefined) Add(active is null ? "insurance-context-required" : "inactive-insurance-answer-retained",field);
        }
        Condition(10,true);
        var ncb = Reference(Get(10),"noClaimBonuses"); var ncbId = Number(At(ncb,"value")); bool? has = ncbId is null ? null : ncbId != 1;
        foreach (var number in new[] { 11,12,13 }) Condition(number,has);
        var earned = Reference(Get(11),"noClaimBonusesEarned"); var insurer = Reference(Get(13),"noClaimBonusPreviousInsurers");
        var earnedId = Number(At(earned,"value")); var insurerId = Number(At(insurer,"value"));
        Condition(16,has == false ? false : has is null || insurerId is null ? null : insurerId == 21);
        Condition(17,has == false ? false : has is null || earnedId is null ? null : earnedId == 3);
        bool? intro = has == false || ncbId is not null and not 2 || earnedId is not null and not 3 ? false : ncbId is null || earnedId is null || insurerId is null ? null : Boolean(At(insurer,"requireNCBIntroRenewal")) == true;
        Condition(14,intro); var declaredIntro = Boolean(Get(14).Value);
        Condition(15,intro == false ? false : intro is null || declaredIntro is null ? null : declaredIntro == true);
        if (Date(Text(Get(12).Value),out var expires) && expires < new DateOnly(1900,1,1)) Add("ncb-expiry-too-early",Get(12));
        if (Text(Get(16).Value) is { Length: > 50 }) Add("insurer-details-too-long",Get(16));
        QuoteSectionField Prototype(string suffix) => Answer(insurance,root,"prototype.quote." + suffix);
        var origin = Prototype("c0650c760167"); var protection = Prototype("61a13bb828b2");
        if (Present(origin.Value) && Present(Get(11).Value))
        {
            var value = Number(At(Reference(origin,origin.QuestionId!),"value"));
            if (value is null || earnedId is null) Add("insurance-origin-context-required",origin);
            else if ((value switch { 1 => 3,2 => 1,3 => 2,_ => 0 }) != earnedId) { Add("conflicting-ncb-origin",origin); Add("conflicting-ncb-origin",Get(11)); }
        }
        if (Boolean(protection.Value) is { } protectedValue && Boolean(Get(17).Value) is { } sourceProtection && protectedValue != sourceProtection)
        { Add("conflicting-ncb-protection",protection); Add("conflicting-ncb-protection",Get(17)); }
        if (Number(Field("noClaimsYears").Value) is { } years)
        {
            var basis = Text(Field("noClaimsYearsBasis").Value); var label = Text(At(ncb,"text"));
            var match = label is null ? Match.Empty : Regex.Match(label,@"^(\d+)(\+)? Years?$",RegexOptions.CultureInvariant);
            var sourceYears = ncbId == 1 ? 0 : match.Success && long.TryParse(match.Groups[1].Value,out var parsed) ? parsed : (long?)null;
            if (ncbId is null || basis is null || sourceYears is null) Add("ncb-years-context-required",Field("noClaimsYears"));
            else
            {
                var sourceAtLeast = match.Success && match.Groups[2].Success; var prototypeAtLeast = basis == "at-least";
                var conflict = sourceAtLeast ? !prototypeAtLeast && years < sourceYears : prototypeAtLeast ? years > sourceYears : years != sourceYears;
                if (conflict) Add("conflicting-ncb-years",Field("noClaimsYears"));
            }
        }
        if (Text(At(proposal,"productCode")) == "motor-trade-road-risks")
        {
            var claim = Prototype("1bdc05ff8b3d"); var introductory = Prototype("3fad63dd9abf");
            foreach (var field in new[] { claim,protection,introductory,Field("insurer") }) if (!Present(field.Value)) Add("prototype-insurance-answer-required",field);
            var claimed = Boolean(claim.Value);
            if (claimed == true)
            {
                foreach (var field in new[] { Field("noClaimsYears"),Field("noClaimsYearsBasis"),Field("expiresOn"),origin }) if (!Present(field.Value)) Add("prototype-insurance-answer-required",field);
                if (Number(Field("noClaimsYears").Value) == 0) Add("claimed-discount-years-must-be-positive",Field("noClaimsYears"));
            }
            if (claimed == false)
            {
                if (Boolean(protection.Value) == true) Add("discount-protection-without-claim",protection);
                if (Present(origin.Value)) Add("inactive-discount-origin-retained",origin);
                foreach (var field in new[] { Field("noClaimsYears"),Field("noClaimsYearsBasis") }) if (Present(field.Value)) Add("inactive-claimed-discount-years",field);
            }
            var reason = Answer(insurance,root,"prototype.no-claims.reason");
            if (claimed == false && !Present(reason.Value)) Add("conditional-answer-required",reason);
            else if (claimed != false && Present(reason.Value)) Add(claimed is null ? "controlling-answer-required" : "inactive-answer-retained",reason);
        }
        return issues;
    }
}
