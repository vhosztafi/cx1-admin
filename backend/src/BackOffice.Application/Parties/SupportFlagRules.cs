using System.Text.Json.Serialization;

namespace BackOffice.Application.Parties;

public sealed record FlagWrite(
    [property:JsonRequired]string? TypeCode,[property:JsonRequired]string? InternalCategory,
    [property:JsonRequired]string? InternalInstruction,[property:JsonRequired]string? ConsentBasis,
    [property:JsonRequired]DateOnly ReviewOn,[property:JsonRequired]string? Reason,
    [property:JsonRequired]Guid[]? VisibleRelationshipIds,string? AgencyInstruction=null);
public sealed record ValidatedFlag(string TypeCode,string InternalCategory,string InternalInstruction,string ConsentBasis,
    DateOnly ReviewOn,string Reason,Guid[] VisibleRelationshipIds,string? AgencyInstruction);

public static class SupportFlagRules
{
    public static ValidatedFlag Validate(FlagWrite input,DateOnly? today=null)
    {
        var issues=new List<PartyFieldIssue>();
        // Reject before processing supplied sensitive text. Error details never echo it.
        if(input.ConsentBasis is not ("verbal-consent" or "written-consent" or "third-party-authority"))
            throw new PartyValidationException([new("/consentBasis","consent-required","Remove sensitive details unless an accepted consent or authority basis is recorded.")]);
        if(input.TypeCode is not ("vulnerability" or "third-party-authority" or "financial-difficulty" or "accessible-format" or "interpreter-required" or "deceased-or-business-ceased"))
            issues.Add(new("/typeCode","invalid-choice","Choose a supported flag type."));
        if(input.InternalCategory is not ("health" or "life-event" or "resilience" or "capability" or "authority"))
            issues.Add(new("/internalCategory","invalid-choice","Choose a supported internal category."));
        var instruction=Text(input.InternalInstruction,2000,"/internalInstruction",true,issues);
        var reason=Text(input.Reason,1000,"/reason",true,issues);
        var agency=Text(input.AgencyInstruction,1000,"/agencyInstruction",false,issues);
        if(input.ReviewOn==default || (today is not null && input.ReviewOn<today.Value))issues.Add(new("/reviewOn","invalid-date","Choose today or a future review date."));
        var grants=input.VisibleRelationshipIds;
        if(grants is null || grants.Length>100 || grants.Any(x=>x==Guid.Empty) || grants.Distinct().Count()!=grants.Length)
            issues.Add(new("/visibleRelationshipIds","invalid-grants","Supply up to 100 distinct relationship identifiers, or an empty list for internal only."));
        if(grants is {Length:>0} && agency is null)issues.Add(new("/agencyInstruction","required","Provide functional wording for every shared flag."));
        if(issues.Count>0)throw new PartyValidationException(issues);
        return new(input.TypeCode!,input.InternalCategory!,instruction!,input.ConsentBasis,input.ReviewOn,reason!,grants!.Order().ToArray(),agency);
    }
    public static void ValidateReviewDate(DateOnly reviewOn,DateOnly today)
    {
        if(reviewOn==default || reviewOn<today)throw new PartyValidationException([new("/reviewOn","invalid-date","Choose today or a future review date.")]);
    }
    private static string? Text(string? value,int maximum,string path,bool required,List<PartyFieldIssue> issues)
    {
        if(value is null){if(required)issues.Add(new(path,"required","Enter a value."));return null;}
        if(string.IsNullOrWhiteSpace(value)){issues.Add(new(path,"empty","Enter a value or omit an optional field."));return null;}
        if(value.Length>maximum)issues.Add(new(path,"too-long",$"Use at most {maximum} characters."));
        if(value.Any(c=>char.IsControl(c) && c is not ('\r' or '\n' or '\t')))issues.Add(new(path,"invalid-text","Remove unsupported control characters."));
        return value.Replace("\r\n","\n").Replace('\r','\n').Trim();
    }
}
