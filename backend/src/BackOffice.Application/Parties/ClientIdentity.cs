using System.Text;
using System.Text.Json.Serialization;

namespace BackOffice.Application.Parties;

public sealed record AddressWrite(
    [property:JsonRequired] string? Line1,
    [property:JsonRequired] string? Town,
    [property:JsonRequired] string? Postcode,
    [property:JsonRequired] string? Country,
    string? Line2 = null,string? County = null);
public sealed record ClientWrite(
    [property:JsonRequired] string? LegalName,
    [property:JsonRequired] string? EntityType,
    [property:JsonRequired] AddressWrite? Address,
    string? CompanyNumber = null);
public sealed record PartyFieldIssue(string Path,string Code,string Message);
public sealed class PartyValidationException(IReadOnlyList<PartyFieldIssue> issues) : Exception("Check the client identity fields.")
{
    public IReadOnlyList<PartyFieldIssue> Issues {get;}=issues;
}
public sealed record ValidatedClientIdentity(string LegalName,string NormalizedName,string EntityType,string? CompanyNumber,AddressWrite Address);

public static class ClientIdentity
{
    public static ValidatedClientIdentity Validate(ClientWrite input)
    {
        var issues=new List<PartyFieldIssue>();
        var legal=Field(input.LegalName,200,"/legalName",true,issues);
        var normalized=NormalizeName(legal ?? "");
        if (normalized.Length>200) issues.Add(new("/legalName","too-long","Use at most 200 normalized characters."));
        if (input.EntityType is not ("sole-trader" or "partnership" or "limited-company" or "llp"))
            issues.Add(new("/entityType","invalid-choice","Choose a supported legal entity type."));
        var company=Field(input.CompanyNumber,30,"/companyNumber",false,issues)?.ToUpperInvariant();
        var address=input.Address;
        if (address is null) issues.Add(new("/address","required","Enter the business address."));
        var line1=Field(address?.Line1,200,"/address/line1",true,issues);
        var town=Field(address?.Town,100,"/address/town",true,issues);
        var postcode=Field(address?.Postcode,20,"/address/postcode",true,issues)?.ToUpperInvariant();
        var line2=Field(address?.Line2,200,"/address/line2",false,issues);
        var county=Field(address?.County,100,"/address/county",false,issues);
        if (address?.Country!="GB") issues.Add(new("/address/country","invalid-country","Use GB for this demo's business address."));
        if (issues.Count>0) throw new PartyValidationException(issues);
        return new(legal!,normalized,input.EntityType!,company,new AddressWrite(line1!,town!,postcode!,"GB",line2,county));
    }

    // Matching/search key only: preserve declared legal name and punctuation separately.
    public static string NormalizeName(string value) => string.Join(' ',value.Normalize(NormalizationForm.FormKC)
        .Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string? Field(string? value,int maximum,string path,bool required,List<PartyFieldIssue> issues)
    {
        if (value is null) {if(required)issues.Add(new(path,"required","Enter a value."));return null;}
        var trimmed=value.Trim();
        if (trimmed.Length==0) {issues.Add(new(path,"empty","Omit an optional field or enter a value."));return null;}
        if (value.Length>maximum) issues.Add(new(path,"too-long",$"Use at most {maximum} characters."));
        if (value.Any(char.IsControl)) issues.Add(new(path,"invalid-text","Use a single line without control characters."));
        return trimmed;
    }
}
