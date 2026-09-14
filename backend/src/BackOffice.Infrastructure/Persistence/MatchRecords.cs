namespace BackOffice.Infrastructure.Persistence;

public sealed class MatchSubmission : MutableRecord
{
    public string Reference {get;set;}="";
    public Guid AgencyId {get;set;}
    public string IdentitySnapshot {get;set;}="{}";
    public Guid? LinkedClientId {get;set;}
    public Guid? LinkedRelationshipId {get;set;}
    // Retained across reopen/link so repeated Separate never creates another identity.
    public Guid? SeparateClientId {get;set;}
}
public sealed class MatchReview : MutableRecord
{
    public Guid SubmissionId {get;set;}
    public Guid CandidateClientId {get;set;}
    public Guid CandidateRelationshipId {get;set;}
    public Guid RuleVersionId {get;set;}
    public string RuleSnapshot {get;set;}="{}";
    public string Signals {get;set;}="[]";
    public string Confidence {get;set;}="medium";
    public string State {get;set;}="pending";
}
public sealed class MatchDecision : StoredRecord
{
    public Guid MatchId {get;set;}
    public string Outcome {get;set;}="";
    public string Reason {get;set;}="";
    public Guid ActorId {get;set;}
    public DateTimeOffset OccurredAt {get;set;}=DateTimeOffset.UtcNow;
    public Guid? ClientId {get;set;}
    public Guid? RelationshipId {get;set;}
    public Guid? InformationRequestId {get;set;}
}
public sealed class MatchInformationRequest : StoredRecord
{
    public Guid MatchId {get;set;}
    public string Description {get;set;}="";
    public Guid ActorId {get;set;}
    public DateTimeOffset RecordedAt {get;set;}=DateTimeOffset.UtcNow;
    public string DeliveryState {get;set;}="recorded";
}
