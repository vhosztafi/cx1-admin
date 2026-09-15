namespace BackOffice.Application.Agencies;

public sealed record AgencyMatrixColumn(string Role, string Label);
public sealed record AgencyMatrixCell(string Role, bool Allowed, bool Available, string Label);
public sealed record AgencyMatrixRow(string Capability, string Label, IReadOnlyList<AgencyMatrixCell> Cells);
public sealed record AgencyPermissionMatrix(IReadOnlyList<AgencyMatrixColumn> Columns, IReadOnlyList<AgencyMatrixRow> Rows);

public static class AgencyPermissionMatrixRules
{
    public static AgencyPermissionMatrix Build(string agencyState, bool bordereauGranted)
    {
        AgencyMatrixColumn[] columns = [new("broker-user", "Broker user"), new("broker-admin", "Broker administrator"), new("servicing", "Internal servicing"), new("agency-admin", "Internal administrator")];
        bool Internal(string role, string capability) => new ActorContext(Guid.Empty, null, null, new HashSet<string> { role }).HasCapability(capability);
        AgencyMatrixCell No(string role) => new(role, false, false, "No");
        AgencyMatrixCell Future(string role) => new(role, false, false, "Not available yet");
        AgencyMatrixRow Row(string capability, string label, Func<string, AgencyMatrixCell> cell) => new(capability, label, columns.Select(column => cell(column.Role)).ToArray());
        return new(columns,
        [
            Row("quote-create", "Create and rate quotes", Future),
            Row("policy-read", "View own agency policies", Future),
            Row("adjustment-create", "Raise adjustments", Future),
            Row("referral-decide", "Decide referrals", role => role.StartsWith("broker-", StringComparison.Ordinal) ? No(role) : Future(role)),
            Row("bordereau-download", "Download bordereaux", role => role switch
            {
                "broker-user" => No(role),
                "broker-admin" when agencyState != "active" => new(role, false, false, "Agency access inactive"),
                "broker-admin" => new(role, bordereauGranted, false, bordereauGranted ? "Granted; downloads unavailable" : "Approval required"),
                _ => Future(role)
            }),
            Row("agency-user-manage", "Manage agency users", role => role.StartsWith("broker-", StringComparison.Ordinal)
                ? AgencyAccessRules.Allows(role, "agency-user-manage")
                    ? new(role, agencyState == "active", agencyState == "active", agencyState == "active" ? "Own agency API" : "Agency access inactive") : No(role)
                : Internal(role, "agency-admin") ? new(role, true, true, "Yes") : No(role)),
            Row("other-agency-read", "View other agencies’ data", role => Internal(role, "client-read") ? new(role, true, true, "Client records") : No(role)),
            Row("commission-amend", "Amend commission terms", role => Internal(role, "agency-admin") ? new(role, true, true, "Independent approval required") : No(role))
        ]);
    }
}
