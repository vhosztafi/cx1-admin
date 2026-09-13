namespace BackOffice.Application;

public sealed record ActorContext(Guid UserId,Guid? TeamId,Guid? AgencyId,IReadOnlySet<string> Roles)
{
    // A role grants a capability, never arbitrary access to a business record.
    // Agency identities remain denied until agency scoping is implemented.
    public bool HasCapability(string capability) => AgencyId is null && capability switch
    {
        "authenticated" => true,
        "platform-admin" => Roles.Contains("system-admin"),
        "integration-admin" => Roles.Contains("system-admin"),
        "audit-read" => Roles.Contains("system-admin"),
        "client-servicing" => Roles.Overlaps(["servicing","underwriter","senior-underwriter"]),
        "finance" => Roles.Contains("finance"),
        _ => false
    };
}
