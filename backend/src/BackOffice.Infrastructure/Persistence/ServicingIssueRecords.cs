namespace BackOffice.Infrastructure.Persistence;

public sealed class ServicingIssueDecision : StoredRecord
{
    public Guid DraftId { get; set; }
    public Guid PolicyId { get; set; }
    public Guid BaseTermId { get; set; }
    public Guid BaseVersionId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid CycleId { get; set; }
    public Guid RatingId { get; set; }
    public Guid TermsVersionId { get; set; }
    public Guid AcceptanceId { get; set; }
    public Guid ActorId { get; set; }
    public Guid GrantId { get; set; }
    public Guid AuthorityVersionId { get; set; }
    public byte[] InputHash { get; set; } = [];
    public string TermsHash { get; set; } = "";
    public string AssuranceHash { get; set; } = "";
    public DateTimeOffset EffectiveAt { get; set; }
    public string Reason { get; set; } = "";
}

// Phase9 owns delivery/application; issue retains exact durable version intent.
public sealed class PolicyMidIntent : StoredRecord
{
    public Guid PolicyId { get; set; }
    public Guid TermId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid VersionId { get; set; }
    public Guid WorkId { get; set; }
    public string Purpose { get; set; } = "adjustment";
    public string PayloadJson { get; set; } = "{}";
    public byte[] PayloadHash { get; set; } = [];
}
