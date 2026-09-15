namespace BackOffice.Application.Agencies;

public sealed record AgencyIdentityRole(string Code,string Scope);

// This policy never grants an internal capability or substitutes for current SQL scope.
public static class AgencyAccessRules
{
    public static string? ActiveRole(Guid? agencyId,string userState,string agencyState,IReadOnlyList<AgencyIdentityRole> roles)
    {
        if(agencyId is null||agencyId==Guid.Empty||userState!="active"||agencyState!="active"||roles.Count!=1)return null;
        var role=roles[0];
        return role.Scope=="agency"&&role.Code is "broker-admin" or "broker-user" or "broker-readonly"?role.Code:null;
    }
    public static bool Allows(string? role,string capability)=>role switch
    {
        "broker-admin"=>capability is "agency-context-read" or "agency-sharing-read" or "agency-user-manage" or "agency-permission-request",
        "broker-user" or "broker-readonly"=>capability is "agency-context-read" or "agency-sharing-read",
        _=>false
    };
}
