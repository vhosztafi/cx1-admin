using System.Net.Mail;
using System.Text.Json.Serialization;

namespace BackOffice.Application.Parties;

public sealed record MarketingConsentWrite(
    [property:JsonRequired] string? State,[property:JsonRequired] bool Email,[property:JsonRequired] bool Telephone,
    [property:JsonRequired] DateTimeOffset RecordedAt,[property:JsonRequired] string? Source);
public sealed record ContactWrite(
    [property:JsonRequired] string? Role,[property:JsonRequired] bool IsPrimary,[property:JsonRequired] MarketingConsentWrite? MarketingConsent,
    string? FullName=null,string? FirstName=null,string? Surname=null,string? Email=null,string? Telephone=null,Guid? PersonId=null);
public sealed record ValidatedContact(string FullName,string NormalizedName,string? FirstName,string? Surname,string Role,string? Email,
    string? Telephone,bool IsPrimary,MarketingConsentWrite MarketingConsent,Guid? PersonId);

public static class ContactRules
{
    public static ValidatedContact Validate(ContactWrite input,DateTimeOffset now)
    {
        var issues=new List<PartyFieldIssue>();
        var full=Text(input.FullName,200,"/fullName",false,issues);
        var first=Text(input.FirstName,100,"/firstName",false,issues);var surname=Text(input.Surname,100,"/surname",false,issues);
        if(full is null && (first is null || surname is null))issues.Add(new("/fullName","required","Enter a full name or both explicit name components."));
        full??=first is not null && surname is not null ? first+" "+surname : "";
        var normalized=ClientIdentity.NormalizeName(full);
        if(full.Length>200 || normalized.Length>200)issues.Add(new("/fullName","too-long","Use at most 200 characters."));
        var role=Text(input.Role,100,"/role",true,issues);
        var email=Text(input.Email,254,"/email",false,issues);var telephone=Text(input.Telephone,50,"/telephone",false,issues);
        if(email is not null && (!MailAddress.TryCreate(email,out var parsed) || parsed.Address!=email || !email.Contains('@')))
            issues.Add(new("/email","invalid-email","Enter an email address without a display name."));
        if(input.PersonId==Guid.Empty)issues.Add(new("/personId","invalid-person","Select an existing person or omit the identifier."));
        var consent=input.MarketingConsent;
        if(consent is null)issues.Add(new("/marketingConsent","required","Record the declared consent state."));
        var source=Text(consent?.Source,200,"/marketingConsent/source",true,issues);
        if(consent is not null)
        {
            if(consent.State is not ("given" or "withheld" or "not-asked"))issues.Add(new("/marketingConsent/state","invalid-choice","Choose given, withheld or not-asked."));
            else if(consent.State=="given" ? !consent.Email && !consent.Telephone : consent.Email || consent.Telephone)
                issues.Add(new("/marketingConsent","inconsistent-consent","Given needs a permitted channel; withheld and not-asked permit no channels."));
            if(consent.RecordedAt==default || consent.RecordedAt>now)issues.Add(new("/marketingConsent/recordedAt","invalid-time","Enter the declared recording time, no later than now."));
        }
        if(issues.Count>0)throw new PartyValidationException(issues);
        return new(full,normalized,first,surname,role!,email,telephone,input.IsPrimary,consent! with {Source=source!,RecordedAt=consent.RecordedAt.ToUniversalTime()},input.PersonId);
    }

    public static bool PrimaryOnCreate(int activeCount,bool requested)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(activeCount);return activeCount==0 || requested;
    }
    public static bool CanDemote(bool currentlyPrimary,bool requestedPrimary)=>!currentlyPrimary || requestedPrimary;
    public static bool CanEnd(bool currentlyPrimary,int activeCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(activeCount);return !currentlyPrimary || activeCount<=1;
    }
    private static string? Text(string? value,int max,string path,bool required,List<PartyFieldIssue> issues)
    {
        if(value is null){if(required)issues.Add(new(path,"required","Enter a value."));return null;}
        if(string.IsNullOrWhiteSpace(value)){issues.Add(new(path,"empty","Enter a value or omit an optional field."));return null;}
        if(value.Length>max)issues.Add(new(path,"too-long",$"Use at most {max} characters."));
        if(value.Any(char.IsControl))issues.Add(new(path,"invalid-text","Use a single line without control characters."));
        return value.Trim();
    }
}
