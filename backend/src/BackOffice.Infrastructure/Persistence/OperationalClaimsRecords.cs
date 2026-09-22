namespace BackOffice.Infrastructure.Persistence;

public sealed class ClaimsAdministrator:MutableRecord
{
    public string Code {get;set;}="";
    public string Name {get;set;}="";
    public string State {get;set;}="active";
}
public sealed class ClaimsHandoff:MutableRecord
{
    public Guid IncidentId {get;set;}
    public Guid PolicyId {get;set;}
    public Guid RevisionId {get;set;}
    public Guid ResolutionId {get;set;}
    public Guid SourceVersionId {get;set;}
    public Guid AdministratorId {get;set;}
    public string RequestJson {get;set;}="{}";
    public string RequestHash {get;set;}="";
    public string State {get;set;}="queued";
    public string? OutcomeCode {get;set;}
    public string? ProviderReference {get;set;}
    public DateTimeOffset? CompletedAt {get;set;}
}
// Each request (including refresh/contact) has its own durable outbox identity.
public sealed class ClaimsRequest:StoredRecord
{
    public Guid HandoffId {get;set;}
    public Guid WorkId {get;set;}
    public Guid ScenarioVersionId {get;set;}
    public string Purpose {get;set;}="handoff";
    public string PayloadJson {get;set;}="{}";
    public string PayloadHash {get;set;}="";
}
public sealed class ClaimsSummary:StoredRecord
{
    public Guid HandoffId {get;set;}
    public Guid RequestId {get;set;}
    public Guid ProviderOperationId {get;set;}
    public string ProviderEventId {get;set;}="";
    public string ContentHash {get;set;}="";
    public string SummaryJson {get;set;}="{}";
    public DateTimeOffset AsOf {get;set;}
    public DateTimeOffset ReceivedAt {get;set;}
}
