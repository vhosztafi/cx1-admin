namespace BackOffice.Infrastructure.Persistence;

public sealed class OperationalIncident:MutableRecord
{
    public Guid PolicyId {get;set;}
    public string ProductCode {get;set;}="";
    public string Reference {get;set;}="";
    public string State {get;set;}="draft";
    public Guid? CurrentRevisionId {get;set;}
    public Guid? CurrentResolutionId {get;set;}
}
public sealed class IncidentRevision:StoredRecord
{
    public Guid IncidentId {get;set;}
    public int Number {get;set;}
    public string DraftJson {get;set;}="{}";
    public string ContentHash {get;set;}="";
    public string Reason {get;set;}="";
    public string AuthorLabel {get;set;}="";
}
public sealed class IncidentOccurrenceRecord:StoredRecord
{
    public Guid IncidentId {get;set;}
    public Guid RevisionId {get;set;}
    public DateTimeOffset KnownAt {get;set;}
    public string State {get;set;}="incomplete";
    public string ResolutionJson {get;set;}="{}";
    public string ContentHash {get;set;}="";
}
public sealed class IncidentResolutionSource:StoredRecord
{
    public Guid ResolutionId {get;set;}
    public Guid VersionId {get;set;}
    public string SourceHash {get;set;}="";
    public DateTimeOffset From {get;set;}
    public DateTimeOffset To {get;set;}
}
public sealed class IncidentEvidence:StoredRecord
{
    public Guid RevisionId {get;set;}
    public Guid DocumentVersionId {get;set;}
}
