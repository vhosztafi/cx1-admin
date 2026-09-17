namespace BackOffice.Infrastructure.Persistence;

// Servicing ownership is independent of source-quote file associations and reviews.
// Bytes and screening outcome are immutable; approval is a separate owned event.
public sealed class ServicingEvidenceFile : StoredRecord
{
    public Guid DraftId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public int ByteLength { get; set; }
    public string Sha256 { get; set; } = "";
    public string ScreeningState { get; set; } = "accepted";
    public string ScreeningMethod { get; set; } = "demo-signature-v1";
}
