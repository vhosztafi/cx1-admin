namespace BackOffice.Infrastructure.Persistence;

// Cancellation evidence belongs to an exact proposal revision, never to a
// fabricated rating cycle. Revisions and reviews remain independently retained.
public sealed class CancellationEvidence : StoredRecord
{
    public Guid DraftId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid FileId { get; set; }
    public string Purpose { get; set; } = "";
    public DateTimeOffset? NoticeDeliveredAt { get; set; }
}

public sealed class CancellationEvidenceReview : StoredRecord
{
    public Guid DraftId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid EvidenceId { get; set; }
    public int Sequence { get; set; }
    public string Outcome { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid AuthorityGrantId { get; set; }
    public Guid AuthorityVersionId { get; set; }
}

public sealed class CancellationPreview : StoredRecord
{
    public Guid DraftId { get; set; }
    public Guid PolicyId { get; set; }
    public Guid BaseTermId { get; set; }
    public Guid BaseVersionId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid RuleSettingVersionId { get; set; }
    public string RuleVersion { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public DateTimeOffset EffectiveAt { get; set; }
    public byte[] InputHash { get; set; } = [];
    public string InputJson { get; set; } = "{}";
    public string ResultJson { get; set; } = "{}";
}

public sealed class CancellationApproval : StoredRecord
{
    public Guid DraftId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid PreviewId { get; set; }
    public byte[] PreviewHash { get; set; } = [];
    public Guid AuthorityGrantId { get; set; }
    public Guid AuthorityVersionId { get; set; }
    public string Reason { get; set; } = "";
}
