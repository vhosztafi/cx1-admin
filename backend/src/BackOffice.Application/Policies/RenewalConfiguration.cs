using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record RenewalSettings(string RuleVersion,IReadOnlyList<int> AllowedTermMonths,int DefaultTermMonths,
    int LossRatioThresholdBasisPoints,int ExperienceLoadingBasisPoints,decimal RenewalFee,int InvitationDaysBeforeExpiry,int LapseDaysAfterExpiry);

public static class RenewalConfiguration
{
    public const string Scope="renewal-preparation";
    public static RenewalSettings? Parse(string json)
    {
        if(json is null || json.Length>4096)return null;
        try
        {
            using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=4});var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)return null;
            var keys=new HashSet<string>(["demo","kind","schemaVersion","ruleVersion","currency","allowedTermMonths","defaultTermMonths",
                "lossRatioThresholdBasisPoints","experienceLoadingBasisPoints","renewalFee","invitationDaysBeforeExpiry","lapseDaysAfterExpiry"],StringComparer.Ordinal);
            foreach(var property in root.EnumerateObject())if(!keys.Remove(property.Name))return null;
            if(keys.Count!=0 || root.GetProperty("demo").ValueKind!=JsonValueKind.True || Text("kind")!=Scope || Text("schemaVersion")!="1" || Text("currency")!="GBP")return null;
            var rule=Text("ruleVersion");
            if(rule is null || rule.Length is <1 or >60 || rule.Any(x=>!char.IsAsciiLetterOrDigit(x) && x!='-'))return null;
            var months=root.GetProperty("allowedTermMonths");
            if(months.ValueKind!=JsonValueKind.Array || months.GetArrayLength() is <1 or >12)return null;
            var allowed=new List<int>();
            foreach(var value in months.EnumerateArray())
                if(value.ValueKind!=JsonValueKind.Number || !value.TryGetInt32(out var month) || month is <1 or >12 || allowed.Contains(month))return null;
                else allowed.Add(month);
            var defaultMonths=Number("defaultTermMonths",1,12);var threshold=Number("lossRatioThresholdBasisPoints",0,100000);
            var loading=Number("experienceLoadingBasisPoints",0,10000);var invite=Number("invitationDaysBeforeExpiry",0,365);var lapse=Number("lapseDaysAfterExpiry",0,365);
            var amount=Text("renewalFee");
            if(defaultMonths is null || !allowed.Contains(defaultMonths.Value) || threshold is null || loading is null || invite is null || lapse is null ||
                amount is null || amount.Length>16 || !decimal.TryParse(amount,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var fee) ||
                fee<0 || fee>QuoteRatingRules.MaximumMoney || amount!=fee.ToString("0.00",CultureInfo.InvariantCulture))return null;
            return new(rule,allowed.AsReadOnly(),defaultMonths.Value,threshold.Value,loading.Value,fee,invite.Value,lapse.Value);

            string? Text(string name)=>root.GetProperty(name) is var value && value.ValueKind==JsonValueKind.String?value.GetString():null;
            int? Number(string name,int minimum,int maximum)=>root.GetProperty(name) is var value && value.ValueKind==JsonValueKind.Number &&
                value.TryGetInt32(out var number) && number>=minimum && number<=maximum?number:null;
        }
        catch(JsonException){return null;}
    }
}
