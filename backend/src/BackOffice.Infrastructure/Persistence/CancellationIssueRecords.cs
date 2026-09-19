namespace BackOffice.Infrastructure.Persistence;

public sealed class CancellationIssueDecision : StoredRecord
{
    public Guid DraftId { get; set; }
    public Guid PolicyId { get; set; }
    public Guid BaseTermId { get; set; }
    public Guid BaseVersionId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid PreviewId { get; set; }
    public Guid ApprovalId { get; set; }
    public byte[] PreviewHash { get; set; } = [];
    public Guid AuthorityGrantId { get; set; }
    public Guid AuthorityVersionId { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class CancellationConsequence : StoredRecord
{
    public Guid PolicyId { get; set; }
    public Guid TermId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid VersionId { get; set; }
    public Guid DecisionId { get; set; }
    public Guid WorkId { get; set; }
    public string Kind { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public byte[] PayloadHash { get; set; } = [];
}

public sealed class CancellationNoticeReceipt : StoredRecord
{
    public Guid ConsequenceId { get; set; }
    public Guid WorkId { get; set; }
    public byte[] PayloadHash { get; set; } = [];
    public string Outcome { get; set; } = "";
}
