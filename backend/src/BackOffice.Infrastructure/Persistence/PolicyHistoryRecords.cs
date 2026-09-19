namespace BackOffice.Infrastructure.Persistence;

public sealed class PolicyReconstructionRequest : StoredRecord
{
    public Guid PolicyId {get;set;}
    public Guid TermId {get;set;}
    public Guid? VersionId {get;set;}
    public byte[]? VersionHash {get;set;}
    public DateTimeOffset EffectiveAt {get;set;}
    public DateTimeOffset KnownAt {get;set;}
    public string CoverageState {get;set;}="not-covered";
    public string ManifestJson {get;set;}="{}";
    public byte[] ManifestHash {get;set;}=[];
    public Guid WorkId {get;set;}
    public Guid ActorId {get;set;}
    public string Reason {get;set;}="";
}

public sealed class PolicyQuoteClone : StoredRecord
{
    public Guid PolicyId {get;set;}
    public Guid VersionId {get;set;}
    public byte[] VersionHash {get;set;}=[];
    public Guid QuoteId {get;set;}
    public Guid RevisionId {get;set;}
    public Guid ActorId {get;set;}
    public string Reason {get;set;}="";
    public string ItemMapJson {get;set;}="{}";
}
