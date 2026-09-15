namespace BackOffice.Application.Agencies;

public sealed record AgencyActionCounts(int StateRequests, int TermsRequests, int PermissionRequests, int DueFollowUps)
{
    // Only pending decision records are open actions. Due obligations have no
    // completion state yet and must not be presented as open workflow tasks.
    public int OpenActions => checked(StateRequests + TermsRequests + PermissionRequests);
    public static AgencyActionCounts Empty { get; } = new(0, 0, 0, 0);
    public static AgencyActionCounts Total(IEnumerable<AgencyActionCounts> values)
    {
        var rows = values.ToArray();
        return new(rows.Sum(x => x.StateRequests), rows.Sum(x => x.TermsRequests), rows.Sum(x => x.PermissionRequests), rows.Sum(x => x.DueFollowUps));
    }
    public static DateOnly LondonDate(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
}
