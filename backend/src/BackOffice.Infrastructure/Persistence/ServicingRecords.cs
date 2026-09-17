namespace BackOffice.Infrastructure.Persistence;

public sealed class ServicingDraft : MutableRecord
{
    public Guid PolicyId { get; set; }
    public Guid BaseTermId { get; set; }
    public Guid BaseVersionId { get; set; }
    public string Kind { get; set; } = "adjustment";
    public string State { get; set; } = "draft";
    public Guid? CurrentRevisionId { get; set; }
    public Guid? CurrentCycleId { get; set; }
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


public sealed class ServicingCycle : MutableRecord
{
    public Guid DraftId { get; set; }
    public Guid PolicyId { get; set; }
    public Guid BaseTermId { get; set; }
    public Guid BaseVersionId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid ProductId { get; set; }
    public Guid ProductVersionId { get; set; }
    public Guid AgencyTermsVersionId { get; set; }
    public Guid RatingRuleVersionId { get; set; }
    public Guid BinderVersionId { get; set; }
    public Guid AuthorityVersionId { get; set; }
    public Guid RuntimeVersionId { get; set; }
    public Guid ScenarioVersionId { get; set; }
    public int Sequence { get; set; }
    public Guid WorkId { get; set; }
    public byte[] InputHash { get; set; } = [];
    public string InputJson { get; set; } = "{}";
    public Guid RequestedBy { get; set; }
    public string State { get; set; } = "rating-pending";
    public DateTimeOffset? SupersededAt { get; set; }
    public string? SupersededReason { get; set; }
}
