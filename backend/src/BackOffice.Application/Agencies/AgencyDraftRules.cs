using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BackOffice.Application.Parties;

namespace BackOffice.Application.Agencies;

public sealed record ValidatedAgencyDraft(string Json,string LegalName,string NormalizedName,string? RegulatoryReference,Guid? RelationshipManagerId);
public sealed record AgencyProductInput([property:JsonRequired]Guid ProductVersionId,[property:JsonRequired]DateOnly EffectiveFrom,[property:JsonRequired]int BrokerCommissionBasisPoints);
public sealed class AgencyCommandException(int status,string code):Exception(code)
{ public int Status {get;}=status; public string Code {get;}=code; }

// Closed structural rules for partial onboarding JSON. Omission is allowed at
// every draft level; unknown keys, nulls and forged state never reach storage.
public static class AgencyDraftRules
{
    private sealed record Field(string Kind="text",int Max=200,string[]? Choices=null,Dictionary<string,Field>? Children=null);
    private static Field Choice(params string[] values)=>new("choice",Choices:values);
    private static Field Object(Dictionary<string,Field> fields)=>new("object",Children:fields);
    private static readonly Field Date=new("date"),Money=new("money"),Bps=new("bps"),Email=new("email",254);
    private static readonly Field Contact=Object(new(){["name"]=new(),["email"]=Email,["telephone"]=new(Max:50)});
    private static readonly Field Address=Object(new(){["line1"]=new(),["line2"]=new(),["town"]=new(Max:100),["county"]=new(Max:100),["postcode"]=new(Max:20),["country"]=Choice("GB")});
    private static readonly Field Shape=Object(new(){
        ["legalName"]=new(),["tradingName"]=new(),["entityType"]=Choice("limited-company","llp","partnership","sole-trader"),["companyNumber"]=new(Max:30),
        ["address"]=Address,["tradingAddressMode"]=Choice("same-as-registered","different"),["tradingAddress"]=Address,["regulatoryReference"]=new(Max:30),
        ["regulatoryStatus"]=Choice("directly-authorised","appointed-representative","introducer-appointed-representative"),["principalFirm"]=new(),
        ["clientMoneyBasis"]=Choice("risk-transfer","cass5-client-money","no-client-money"),["arrangesGeneralInsurance"]=Choice("unchecked","confirmed","restricted"),["territory"]=Choice("UK","GB","NI"),
        ["mainContact"]=Contact,["complianceContact"]=Contact,["accountsContact"]=Contact,["complaintsContact"]=Contact,["relationshipManagerId"]=new("uuid"),
        ["correspondencePreference"]=Choice("email","email-and-post","portal-only"),["officeHours"]=new(),
        ["commercialTerms"]=Object(new(){["effectiveFrom"]=Date,["commissionBasis"]=Choice("per-product","flat-rate"),["flatCommissionBasisPoints"]=Bps,["feeSharing"]=Choice("none","agreed-split"),["feeShareBasisPoints"]=Bps,["volumeCommitmentMode"]=Choice("none","target-no-penalty","target-tiered"),["volumeCommitment"]=Money,["minimumPremiumOverrideMode"]=Choice("none","capacity-provider-agreed"),["minimumPremiumOverride"]=Money,["referralRouting"]=Choice("standard-internal-underwriting")}),
        ["compliance"]=Object(new(){["tobaStatus"]=Choice("not-sent","sent","signed"),["tobaVersion"]=Choice("2026.1","2025.2"),["tobaSignedOn"]=Date,["professionalIndemnityStatus"]=Choice("not-supplied","meets-minimum","below-minimum"),["professionalIndemnityLimit"]=Money,["piExpiresOn"]=Date,["financialStanding"]=Choice("not-started","pending","passed","refer"),["sanctionsCheck"]=Choice("not-started","clear","refer"),["beneficialOwnershipVerified"]=Choice("not-started","verified","refer"),["dataProcessingAgreement"]=Choice("not-sent","sent","signed")}),
        ["paymentTermsDays"]=new("days"),["creditLimit"]=Money,["settlement"]=Object(new(){["statementCycle"]=Choice("monthly","fortnightly"),["method"]=Choice("bank-transfer","direct-debit"),["premiumCollection"]=Choice("agency","mga"),["commissionSettlement"]=Choice("net-remittance","separate-payment")})});

    public static ValidatedAgencyDraft Validate(JsonElement input)
    {
        if(Encoding.UTF8.GetByteCount(input.GetRawText())>65536)throw new AgencyCommandException(413,"agency-draft-too-large");
        var issues=new List<PartyFieldIssue>();var value=Normalize(input,Shape,"/details",issues) as JsonObject;
        if(issues.Count>0)throw new PartyValidationException(issues);
        var name=value?["legalName"]?.GetValue<string>()??"";var normalized=ClientIdentity.NormalizeName(name);
        if(normalized.Length>200)throw new PartyValidationException([new("/details/legalName","too-long","Use a shorter business name.")]);
        return new(value!.ToJsonString(),name,normalized,value["regulatoryReference"]?.GetValue<string>(),value["relationshipManagerId"] is JsonNode manager?Guid.Parse(manager.GetValue<string>()):null);
    }
    private static JsonNode? Normalize(JsonElement value,Field field,string path,List<PartyFieldIssue> errors)
    {
        void Invalid(string message)=>errors.Add(new(path,"invalid-value",message));
        if(field.Kind=="object")
        {
            if(value.ValueKind!=JsonValueKind.Object){Invalid("Use an object with supported fields.");return null;}
            var output=new JsonObject();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var property in value.EnumerateObject().OrderBy(x=>x.Name,StringComparer.Ordinal))
            {
                if(!seen.Add(property.Name)){Invalid("Duplicate fields are not allowed.");continue;}
                if(!field.Children!.TryGetValue(property.Name,out var child)){errors.Add(new(path+"/"+property.Name,"unknown-field","This field is not supported."));continue;}
                var normalized=Normalize(property.Value,child,path+"/"+property.Name,errors);if(normalized is not null)output[property.Name]=normalized;
            }
            return output;
        }
        if(field.Kind is "bps" or "days")
        {
            if(!value.TryGetInt32Safe(out var number) || (field.Kind=="bps" ? number is <0 or >10000 : number is not (30 or 45 or 60))){Invalid("Choose a supported whole-number value.");return null;}
            return JsonValue.Create(number);
        }
        if(value.ValueKind!=JsonValueKind.String){Invalid("Enter a text value or omit an unanswered field.");return null;}
        var raw=value.GetString()!;var text=raw.Trim();
        if(raw.Any(char.IsControl)){Invalid("Control characters are not allowed.");return null;}
        if(text.Length==0 && field.Kind is "text" or "email")return null;
        var valid=field.Kind switch {
            "text"=>raw.Length<=field.Max,
            "email"=>raw.Length<=254 && MailAddress.TryCreate(text,out var address) && address.Address==text && text.Contains('@') && !text.Contains(' '),
            "choice"=>field.Choices!.Contains(text,StringComparer.Ordinal),
            "date"=>DateOnly.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),
            "uuid"=>Guid.TryParseExact(text,"D",out var guid)&&guid!=Guid.Empty,
            "money"=>Regex.IsMatch(text,"^(0|[1-9][0-9]{0,16})\\.[0-9]{2}$",RegexOptions.CultureInvariant),
            _=>false};
        if(!valid){Invalid("Check the value, format and maximum length.");return null;}
        return JsonValue.Create(text);
    }
    private static bool TryGetInt32Safe(this JsonElement value,out int number){number=0;return value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out number);}
    public static void ValidateStep(int step){if(step is <1 or >6)throw new AgencyCommandException(422,"invalid-onboarding-step");}
    public static void ValidateProducts(IReadOnlyList<AgencyProductInput> products)
    {
        if(products.Count>3 || products.Select(x=>x.ProductVersionId).Distinct().Count()!=products.Count || products.Any(x=>x.ProductVersionId==Guid.Empty||x.EffectiveFrom==default||x.BrokerCommissionBasisPoints is <0 or >10000))throw new AgencyCommandException(422,"invalid-agency-products");
    }
}
