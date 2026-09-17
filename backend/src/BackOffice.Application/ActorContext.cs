namespace BackOffice.Application;

public sealed record ActorContext(Guid UserId,Guid? TeamId,Guid? AgencyId,IReadOnlySet<string> Roles)
{
    // A role grants a capability, never arbitrary access to a business record.
    // Internal capabilities never apply to an agency-scoped identity.
    public bool HasCapability(string capability) => AgencyId is null && capability switch
    {
        "authenticated" => true,
        "platform-admin" => Roles.Contains("system-admin"),
        "integration-admin" => Roles.Contains("system-admin"),
        "integration-retry" => Roles.Contains("system-admin"),
        "audit-read" => Roles.Contains("system-admin"),
        "client-servicing" => Roles.Overlaps(["servicing","underwriter","senior-underwriter"]),
        "quote-read" or "quote-capture" => Roles.Overlaps(["servicing","underwriter","senior-underwriter"]),
        "policy-draft-write" or "policy-draft-rate" => Roles.Overlaps(["servicing","underwriter","senior-underwriter"]),
        "policy-draft-takeover" => Roles.Overlaps(["underwriter","senior-underwriter"]),
        "quote-rate" or "quote-submit" or "quote-revise" or "quote-terms" or "quote-acceptance" or "underwriting-read" or "underwriting-evidence-write" or "policy-read" or "policy-discovery-read"
            => Roles.Overlaps(["servicing","underwriter","senior-underwriter"]),
        "underwriting-decide-within-authority" or "underwriting-evidence-review" or "underwriting-escalate" or "underwriting-record-capacity" or "policy-issue-within-authority"
            => Roles.Overlaps(["underwriter","senior-underwriter"]),
        "client-read" or "relationship-read" or "contact-write" => Roles.Overlaps(["servicing","underwriter","senior-underwriter","agency-admin"]),
        "client-write" or "support-internal-read" or "support-write" => Roles.Overlaps(["servicing","underwriter","senior-underwriter"]),
        "match-read" or "match-review" => Roles.Overlaps(["underwriter","senior-underwriter"]),
        "agency-read" => Roles.Overlaps(["agency-admin","system-admin","underwriter","senior-underwriter"]),
        "agency-admin" => Roles.Overlaps(["agency-admin","system-admin"]),
        "finance" => Roles.Contains("finance"),
        _ => false
    };
}
