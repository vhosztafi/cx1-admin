namespace BackOffice.Application.Agencies;

public sealed record AgencyPermissionInput(string Permission, string Reason);
public sealed record AgencyPermissionDecision(string State, string Reason);

// Input/lifecycle policy only. Services must resolve current stored authority
// inside the command transaction, including before an idempotent receipt replay.
public static class AgencyPermissionRules
{
    public const string BordereauDownload = "bordereau-download";
    public const string StatementDownload = "statement-download";

    public static AgencyPermissionInput Request(string permission, string reason)
    {
        if (permission is not (BordereauDownload or StatementDownload)) throw new AgencyCommandException(422, "invalid-agency-permission");
        return new(permission, Reason(reason));
    }

    public static AgencyPermissionDecision Decide(Guid requestedBy, Guid decisionBy, string currentState, string decision, string reason)
    {
        if (requestedBy == Guid.Empty || decisionBy == Guid.Empty || requestedBy == decisionBy)
            throw new AgencyCommandException(403, "independent-permission-decision-required");
        if (currentState != "pending") throw new AgencyCommandException(409, "permission-request-not-pending");
        var state = decision switch
        {
            "approve" => "granted",
            "reject" => "rejected",
            _ => throw new AgencyCommandException(422, "invalid-permission-decision")
        };
        return new(state, Reason(reason));
    }

    public static string Reason(string reason)
    {
        reason = reason?.Trim() ?? "";
        if (reason.Length is < 1 or > 1000 || reason.Any(char.IsControl)) throw new AgencyCommandException(422, "permission-reason-required");
        return reason;
    }
}
