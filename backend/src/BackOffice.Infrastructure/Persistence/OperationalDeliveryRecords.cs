namespace BackOffice.Infrastructure.Persistence;

// Queued message content is immutable; delivery never reads the editable draft.
public sealed class OperationalMessageVersion:StoredRecord
{
    public Guid MessageId {get;set;}
    public string ContentJson {get;set;}="{}";
    public string ContentHash {get;set;}="";
}
public sealed class OperationalDelivery:MutableRecord
{
    public Guid SubjectId {get;set;}
    public Guid RelationshipId {get;set;}
    public Guid? MessageVersionId {get;set;}
    public Guid? ResendOfId {get;set;}
    public Guid WorkId {get;set;}
    public Guid ScenarioVersionId {get;set;}
    public string ContentJson {get;set;}="{}";
    public string ContentHash {get;set;}="";
    public string State {get;set;}="queued";
    public string? OutcomeCode {get;set;}
    public DateTimeOffset? CompletedAt {get;set;}
    public Guid? ProviderOperationId {get;set;}
    public Guid? AttemptId {get;set;}
}
public sealed class OperationalDeliveryRecipient:StoredRecord
{
    public Guid DeliveryId {get;set;}
    public Guid ContactId {get;set;}
    public string Name {get;set;}="";
    public string Email {get;set;}="";
}
public sealed class OperationalDeliveryAttachment:StoredRecord
{
    public Guid DeliveryId {get;set;}
    public Guid DocumentVersionId {get;set;}
    public Guid FileObjectId {get;set;}
    public string ContentHash {get;set;}="";
    public string OriginalName {get;set;}="";
    public string MediaType {get;set;}="";
    public long Length {get;set;}
}
