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


public sealed class ServicingEvidenceAssociation : MutableRecord
{
    public Guid DraftId { get; set; }
    public Guid CycleId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid RatingId { get; set; }
    public Guid FileId { get; set; }
    public string RequirementCode { get; set; } = "";
    public Guid? RiskItemId { get; set; }
    public Guid? CapacitySubmissionId { get; set; }
    public string InputFingerprint { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid? LatestReviewId { get; set; }
    public Guid? WithdrawnEventId { get; set; }
}

public sealed class ServicingEvidenceEvent : StoredRecord
{
    public Guid AssociationId { get; set; }
    public Guid DraftId { get; set; }
    public Guid CycleId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid RatingId { get; set; }
    public int Sequence { get; set; }
    public string Kind { get; set; } = "";
    public string? Outcome { get; set; }
    public string Reason { get; set; } = "";
    public Guid ActorId { get; set; }
    public Guid? AuthorityVersionId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string InputFingerprint { get; set; } = "";
}
