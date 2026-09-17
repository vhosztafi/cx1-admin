namespace BackOffice.Infrastructure.Persistence;

public sealed class ServicingReferral : MutableRecord
{
    public Guid DraftId {get;set;}
    public Guid CycleId {get;set;}
    public Guid RevisionId {get;set;}
    public Guid RatingId {get;set;}
    public int Sequence {get;set;}
    public string RuleCode {get;set;}="";
    public string Dimension {get;set;}="";
    public Guid? RiskItemId {get;set;}
    public Guid TargetKey {get;set;}
    public string RequiredAuthorityJson {get;set;}="{}";
    public string Reason {get;set;}="";
    public string State {get;set;}="open";
    public Guid? AssignedUserId {get;set;}
    public Guid? LatestDecisionId {get;set;}
}

public sealed class ServicingReferralDecision : StoredRecord
{
    public Guid ReferralId {get;set;}
    public Guid DraftId {get;set;}
    public Guid CycleId {get;set;}
    public Guid RevisionId {get;set;}
    public Guid RatingId {get;set;}
    public int Sequence {get;set;}
    public string Outcome {get;set;}="";
    public string Reason {get;set;}="";
    public string? Question {get;set;}
    public Guid ActorId {get;set;}
    public Guid AuthorityVersionId {get;set;}
    public Guid GrantId {get;set;}
    public DateTimeOffset DecidedAt {get;set;}
    public string ConditionsJson {get;set;}="[]";
}
