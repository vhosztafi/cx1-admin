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

public sealed class Person : MutableRecord
{
    public string FullName {get;set;}="";
    public string? FirstName {get;set;}
    public string? Surname {get;set;}
}
public sealed class Contact : MutableRecord
{
    public Guid ClientId {get;set;}
    public Guid RelationshipId {get;set;}
    public Guid PersonId {get;set;}
    public string DeclaredFullName {get;set;}="";
    public string NormalizedName {get;set;}="";
    public string? DeclaredFirstName {get;set;}
    public string? DeclaredSurname {get;set;}
    public string Role {get;set;}="";
    public string? Email {get;set;}
    public string? Telephone {get;set;}
    public bool IsPrimary {get;set;}
    public string MarketingConsent {get;set;}="{}";
    public DateTimeOffset? EndedAt {get;set;}
    public Guid? EndedBy {get;set;}
    public string? EndReason {get;set;}
}

public sealed class SupportFlag : MutableRecord
{
    public Guid ClientId {get;set;}
    public Guid PersonId {get;set;}
    public Guid OriginRelationshipId {get;set;}
    public string TypeCode {get;set;}="";
    public string InternalCategory {get;set;}="";
    public string InternalInstruction {get;set;}="";
    public string? AgencyInstruction {get;set;}
    public string ConsentBasis {get;set;}="";
    public DateOnly ReviewOn {get;set;}
    public string Reason {get;set;}="";
    public DateTimeOffset? EndedAt {get;set;}
    public Guid? EndedBy {get;set;}
}
public sealed class FlagVisibility : StoredRecord
{
    public Guid FlagId {get;set;}
    public Guid ClientId {get;set;}
    public Guid RelationshipId {get;set;}
}
public sealed class SupportFlagHistory : StoredRecord
{
    public Guid FlagId {get;set;}
    public Guid ActorId {get;set;}
    public DateTimeOffset OccurredAt {get;set;}=DateTimeOffset.UtcNow;
    public string Action {get;set;}="";
    public string Reason {get;set;}="";
    public string Snapshot {get;set;}="{}";
}
