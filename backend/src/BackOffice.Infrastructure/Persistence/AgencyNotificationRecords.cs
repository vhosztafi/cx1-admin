namespace BackOffice.Infrastructure.Persistence;

// Immutable message identity/content. Delivery progress belongs to OutboxWork/AdapterAttempt.
public sealed class AgencyNotification:StoredRecord
{
    public Guid AgencyId {get;set;}
    public Guid WorkId {get;set;}
    public string Purpose {get;set;}="agency-activated";
    public string ProtectedPayload {get;set;}="";
    public byte[] ContentHash {get;set;}=[];
}

// A terminal simulated provider effect, committed independently of local job completion.
public sealed class AgencyNotificationReceipt:StoredRecord
{
    public Guid NotificationId {get;set;}
    public Guid AgencyId {get;set;}
    public bool Accepted {get;set;}
    public string ResultCode {get;set;}="";
    public DateTimeOffset CompletedAt {get;set;}
}
