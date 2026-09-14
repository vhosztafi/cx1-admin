namespace BackOffice.Infrastructure.Persistence;

// The immutable requested state is bound to an agency version. The decision is
// a single guarded transition; applying business effects belongs to its transaction.
public sealed class AgencyStateRequest:MutableRecord
{
    public Guid AgencyId {get;set;}
    public string Kind {get;set;}="activation";
    public string RequestedState {get;set;}="active";
    public byte[] BaseVersion {get;set;}=[];
    public string ProposedInputFingerprint {get;set;}="";
    public Guid RequestedBy {get;set;}
    public string RequestReason {get;set;}="";
    public string State {get;set;}="pending";
    public Guid? DecisionBy {get;set;}
    public string? DecisionReason {get;set;}
    public DateTimeOffset? DecidedAt {get;set;}
}

public sealed class AgencyTermsRequest:MutableRecord
{
    public Guid AgencyId {get;set;}
    public byte[] BaseVersion {get;set;}=[];
    public DateOnly EffectiveFrom {get;set;}
    public string ProposedSnapshot {get;set;}="{}";
    public string ProposedInputFingerprint {get;set;}="";
    public Guid RequestedBy {get;set;}
    public string RequestReason {get;set;}="";
    public string State {get;set;}="pending";
    public Guid? DecisionBy {get;set;}
    public string? DecisionReason {get;set;}
    public DateTimeOffset? DecidedAt {get;set;}
}

public sealed class AgencyTermsVersion:StoredRecord
{
    public Guid AgencyId {get;set;}
    public int Version {get;set;}
    public DateOnly EffectiveFrom {get;set;}
    public Guid? ApprovedStateRequestId {get;set;}
    public Guid? ApprovedTermsRequestId {get;set;}
    // Full canonical commercial/account/product snapshot; subsequent publication
    // must not mutate values already captured by downstream policies or finance.
    public string Snapshot {get;set;}="{}";
}

// Immutable relational projection of a published snapshot's product grants.
// SQL creates the full set with its parent version in the same statement.
public sealed class AgencyProduct:StoredRecord
{
    public Guid AgencyTermsVersionId {get;set;}
    public Guid ProductVersionId {get;set;}
    public DateOnly EffectiveFrom {get;set;}
    public int BrokerCommissionBasisPoints {get;set;}
}
