namespace BackOffice.Application.Agencies;

public sealed record AgencyUserInput(string Email,string NormalizedEmail,string DisplayName,string Role);
public static class AgencyUserRules
{
    public static AgencyUserInput Validate(string email,string displayName,string role)
    {
        email=email?.Trim()??"";
        if(email.Length is <3 or >254||email.Any(char.IsControl)||!System.Net.Mail.MailAddress.TryCreate(email,out var address)||address.Address!=email)
            throw new AgencyCommandException(422,"invalid-agency-user-email");
        var profile=ValidateProfile(displayName,role);
        return new(email,email.ToUpperInvariant(),profile.DisplayName,profile.Role);
    }
    public static (string DisplayName,string Role) ValidateProfile(string displayName,string role)
    {
        displayName=displayName?.Trim()??"";
        if(displayName.Length is <1 or >200||displayName.Any(char.IsControl))throw new AgencyCommandException(422,"invalid-agency-user-name");
        if(role is not ("broker-admin" or "broker-user" or "broker-readonly"))throw new AgencyCommandException(422,"invalid-agency-user-role");
        return (displayName,role);
    }
}
