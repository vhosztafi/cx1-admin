namespace BackOffice.Infrastructure.Persistence;

public sealed class AgencyEvidenceFile:StoredRecord
{
    public Guid AgencyId {get;set;}
    public string FileName {get;set;}="";
    public string ContentType {get;set;}="";
    public byte[] Content {get;set;}=[];
    public int ByteLength {get;set;}
    public string Sha256 {get;set;}="";
    public string ScreeningState {get;set;}="demo-cleared";
}
public sealed class AgencyEvidence:StoredRecord
{
    public long Ordinal {get;set;}
    public Guid AgencyId {get;set;}
    public string Kind {get;set;}="";
    public string State {get;set;}="pending";
    public string InputFingerprint {get;set;}="";
    public string InputSnapshot {get;set;}="{}";
    public Guid RuleVersionId {get;set;}
    public Guid? FileId {get;set;}
    public Guid? AttestedBy {get;set;}
    public DateTimeOffset? VerifiedAt {get;set;}
    public DateOnly? ExpiresOn {get;set;}
    public string ResultCode {get;set;}="";
    public string? Notes {get;set;}
}
public sealed class AgencyCheckAttempt:StoredRecord
{
    public long Ordinal {get;set;}
    public Guid AgencyId {get;set;}
    public string Kind {get;set;}="";
    public string State {get;set;}="unavailable";
    public string InputFingerprint {get;set;}="";
    public Guid RuleVersionId {get;set;}
    public string Scenario {get;set;}="unavailable";
    public Guid EvidenceId {get;set;}
    public DateTimeOffset CompletedAt {get;set;}
    public string ResultCode {get;set;}="";
}
