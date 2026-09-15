namespace BackOffice.Infrastructure.Persistence;

public sealed class AgencyPermissionRequest : MutableRecord
{
    public Guid AgencyId { get; set; }
    public string Permission { get; set; } = "bordereau-download";
    public Guid RequestedBy { get; set; }
    public string Reason { get; set; } = "";
    public string State { get; set; } = "pending";
    public Guid? DecisionBy { get; set; }
    public string? DecisionReason { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}

// SQL creates this immutable provenance when a request is granted. Only the
// complete one-time revocation tuple may subsequently be assigned.
public sealed class AgencyPermissionGrant : MutableRecord
{
    public Guid AgencyId { get; set; }
    public Guid RequestId { get; set; }
    public string Permission { get; set; } = "bordereau-download";
    public Guid GrantedBy { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
}
