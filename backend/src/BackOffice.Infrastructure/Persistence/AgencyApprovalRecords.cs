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
