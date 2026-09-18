using System.Globalization;
using BackOffice.Application.Quotes;

namespace BackOffice.Application.Policies;

public sealed record RenewalTimeline(DateTimeOffset InvitationDueAt,DateTimeOffset ExpiringEnd,DateTimeOffset RenewalInception,DateTimeOffset AutoLapseAt);

public static class RenewalLifecycleRules
{
    private static readonly TimeZoneInfo London=TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public static RenewalTimeline Timeline(DateTimeOffset expiringEnd,int invitationDaysBeforeExpiry,int lapseDaysAfterExpiry,
        int? invitationOffsetMinutes=null,int? lapseOffsetMinutes=null)
    {
        if(!Utc(expiringEnd) || expiringEnd.Ticks%TimeSpan.TicksPerMinute!=0 || invitationDaysBeforeExpiry is <0 or >365 ||
            lapseDaysAfterExpiry is <0 or >365 || invitationOffsetMinutes is not(null or 0 or 60) || lapseOffsetMinutes is not(null or 0 or 60))
            throw new ArgumentException("Renewal timeline requires exact UTC expiry and bounded calendar-day configuration.");
        var local=TimeZoneInfo.ConvertTime(expiringEnd,London).DateTime;
        DateTimeOffset Shift(int days,int? offset)
        {
            DateTime date;try{date=local.AddDays(days);}catch(ArgumentOutOfRangeException){throw new ArgumentException("Renewal timeline is outside the supported calendar.");}
            var resolved=QuoteTerm.ResolveLondonTime(date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),date.ToString("HH:mm",CultureInfo.InvariantCulture),offset);
            return resolved.Instant??throw new ArgumentException("Renewal timeline has an invalid or ambiguous London time: "+resolved.Code);
        }
        return new(Shift(-invitationDaysBeforeExpiry,invitationOffsetMinutes),expiringEnd,expiringEnd,Shift(lapseDaysAfterExpiry,lapseOffsetMinutes));
    }

    // Receiving acceptance late cannot manufacture continuous cover. The owning
    // service also checks exact delivered terms, reviewed proof and authority.
    public static bool WithinIssueWindow(DateTimeOffset now,DateTimeOffset inception)=>Utc(now)&&Utc(inception)&&now<=inception;

    // Manual lapse may be recorded ahead of expiry but its effective instant is
    // still the expiring end. The owning SQL transaction deduplicates by term and
    // serializes this decision with acceptance/issue before writing any event.
    public static bool CanLapse(DateTimeOffset now,DateTimeOffset autoLapseAt,bool manual,bool accepted,bool issued,bool lapsed)=>
        Utc(now)&&Utc(autoLapseAt)&&!accepted&&!issued&&!lapsed&&(manual||now>=autoLapseAt);

    private static bool Utc(DateTimeOffset value)=>value.Offset==TimeSpan.Zero;
}
