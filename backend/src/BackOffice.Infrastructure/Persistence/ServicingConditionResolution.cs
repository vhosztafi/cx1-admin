namespace BackOffice.Infrastructure.Persistence;

// Explicit resolution binds one condition to the exact reviewed evidence version.
// Its existence alone does not make subsequently withdrawn/rereviewed proof valid.
public sealed class ServicingConditionResolution : StoredRecord
{
    public Guid ConditionId {get;set;}
    public Guid ReferralId {get;set;}
    public Guid DraftId {get;set;}
    public Guid CycleId {get;set;}
    public Guid RevisionId {get;set;}
    public Guid RatingId {get;set;}
    public Guid AssociationId {get;set;}
    public Guid ReviewId {get;set;}
    public string InputFingerprint {get;set;}="";
    public int Sequence {get;set;}
    public string Outcome {get;set;}="";
    public string Reason {get;set;}="";
    public Guid ActorId {get;set;}
    public Guid AuthorityVersionId {get;set;}
    public Guid GrantId {get;set;}
    public DateTimeOffset RecordedAt {get;set;}
}
