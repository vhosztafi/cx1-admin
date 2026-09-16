namespace BackOffice.Infrastructure.Persistence;

// Quote ownership is authoritative; no public or external storage locator.
public sealed class QuoteEvidenceFile : StoredRecord
{
    public Guid QuoteId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public int ByteLength { get; set; }
    public string Sha256 { get; set; } = "";
    public string ScreeningState { get; set; } = "accepted";
    public string ScreeningMethod { get; set; } = "demo-signature-v1";
}

// RowVersion is an opaque evidence ETag. Attestations are append-only;
// withdrawal is a separate immutable event and stale applicability is derived.
public sealed class QuoteCaptureEvidence : MutableRecord
{
    public Guid QuoteId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid FileId { get; set; }
    public string RequirementCode { get; set; } = "";
    public Guid? RiskItemId { get; set; }
    public string InputFingerprint { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid ActorId { get; set; }
}

public sealed class QuoteEvidenceWithdrawal : StoredRecord
{
    public Guid QuoteId { get; set; }
    public Guid EvidenceId { get; set; }
    public string Reason { get; set; } = "";
    public Guid ActorId { get; set; }
}
