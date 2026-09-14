namespace BackOffice.Infrastructure.Persistence;

// Minimal agency identity; onboarding extends this same record in Phase 4.
public sealed class Agency : MutableRecord
{
    public string Reference {get;set;}="";
    public string LegalName {get;set;}="";
    public string State {get;set;}="draft";
}
public sealed class ClientAccount : MutableRecord
{
    public string Reference {get;set;}="";
    public string LegalName {get;set;}="";
    public string NormalizedName {get;set;}="";
    public string EntityType {get;set;}="limited-company";
    public string? CompanyNumber {get;set;}
    public string Address {get;set;}="{}";
    public string IdentityState {get;set;}="active";
}
public sealed class ClientAgencyRelationship : MutableRecord
{
    public Guid ClientId {get;set;}
    public Guid AgencyId {get;set;}
    public string State {get;set;}="active";
}
public sealed class ClientActivity : StoredRecord
{
    public Guid ClientId {get;set;}
    public Guid? RelationshipId {get;set;}
    public Guid? ActorId {get;set;}
    public string EventType {get;set;}="";
    public DateTimeOffset OccurredAt {get;set;}=DateTimeOffset.UtcNow;
    public Guid? RecordId {get;set;}
    public string? RecordKind {get;set;}
}
