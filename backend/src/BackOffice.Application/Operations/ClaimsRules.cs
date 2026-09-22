namespace BackOffice.Application.Operations;

public sealed record ClaimsAdministratorSummary(string ProviderReference,string EventId,DateTimeOffset AsOf,string Status,string? Paid,string? Reserved,string Currency="GBP");
public sealed class ClaimsRuleException(string code):Exception("The claims operation is invalid.") { public string Code {get;}=code; }
public static class ClaimsRules
{
    public static void Summary(ClaimsAdministratorSummary summary,DateTimeOffset requestedAt,DateTimeOffset receivedAt)
    {
        if(string.IsNullOrWhiteSpace(summary.ProviderReference)||summary.ProviderReference.Length>100||
           string.IsNullOrWhiteSpace(summary.EventId)||summary.EventId.Length>200||summary.Currency!="GBP"||
           summary.Status is not("notified" or "open" or "closed" or "rejected")||
           summary.AsOf<requestedAt||summary.AsOf>receivedAt||receivedAt<requestedAt||
           !Money(summary.Paid)||!Money(summary.Reserved))throw new ClaimsRuleException("claims-summary-invalid");
    }
    private static bool Money(string? value)=>value is null||System.Text.RegularExpressions.Regex.IsMatch(value,@"\A(0|[1-9][0-9]{0,12})\.[0-9]{2}\z",System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    public static bool CanCorrect(string incidentState,string? lastHandoffState,string? lastOutcome)=>
        incidentState is "draft" or "logged"&&lastHandoffState is null||
        incidentState=="failed"&&lastHandoffState=="rejected"&&lastOutcome=="provider-rejected";
}
