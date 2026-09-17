namespace BackOffice.Infrastructure.Persistence;

public sealed class ServicingDraft : MutableRecord
{
    public Guid PolicyId { get; set; }
    public Guid BaseTermId { get; set; }
    public Guid BaseVersionId { get; set; }
    public string Kind { get; set; } = "adjustment";
    public string State { get; set; } = "draft";
    public Guid? CurrentRevisionId { get; set; }
}

public sealed class ServicingRevision : StoredRecord
{
    public Guid DraftId { get; set; }
    public int Sequence { get; set; }
    public string SchemaVersion { get; set; } = "1.0";
    public string ProposalJson { get; set; } = "{}";
    public byte[] ContentHash { get; set; } = [];
}

public sealed class ServicingLease : MutableRecord
{
    public Guid DraftId { get; set; }
    public Guid HolderId { get; set; }
    public Guid Token { get; set; }
    public int Generation { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Active { get; set; }
}
