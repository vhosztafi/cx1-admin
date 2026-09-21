namespace BackOffice.Infrastructure.Persistence;

// Content identity and ownership never change. Only finalization/quarantine
// state changes; existing evidence remains in its original SQL byte record.
public sealed class FileObject : MutableRecord
{
    public Guid SubjectId { get; set; }
    public string StorageKind { get; set; } = "local";
    public string FileName { get; set; } = "";
    public string MediaType { get; set; } = "";
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = "";
    public string State { get; set; } = "pending";
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? FailureCode { get; set; }
    public Guid? WorkId { get; set; }
    public Guid? AgencyEvidenceFileId { get; set; }
    public Guid? QuoteEvidenceFileId { get; set; }
    public Guid? ServicingEvidenceFileId { get; set; }
}
