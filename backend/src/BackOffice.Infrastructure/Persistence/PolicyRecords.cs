namespace BackOffice.Infrastructure.Persistence;

public sealed class Policy : MutableRecord
{
    public long Number { get; set; }
    public string Reference { get; set; } = "";
    public Guid SourceQuoteId { get; set; }
    public Guid AgencyId { get; set; }
    public Guid ClientId { get; set; }
    public Guid RelationshipId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? CurrentTermId { get; set; }
}
public sealed class PolicyTerm : MutableRecord
{
    public Guid PolicyId { get; set; }
    public int Number { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string LocalTermIntentJson { get; set; } = "{}";
    public Guid ProductId { get; set; }
    public Guid ProductVersionId { get; set; }
    public Guid? CurrentVersionId { get; set; }
}
public sealed class PolicyTransaction : StoredRecord
{
    public Guid PolicyId { get; set; }
    public Guid TermId { get; set; }
    public Guid SourceQuoteId { get; set; }
    public Guid? CycleId { get; set; }
    public Guid? QuoteRevisionId { get; set; }
    public Guid? RatingId { get; set; }
    public Guid? AcceptanceId { get; set; }
    public Guid? ServicingDraftId { get; set; }
    public Guid? ServicingRevisionId { get; set; }
    public Guid? ServicingCycleId { get; set; }
    public Guid? ServicingRatingId { get; set; }
    public Guid? ServicingAcceptanceId { get; set; }
    public Guid? ServicingIssueDecisionId { get; set; }
    public int Sequence { get; set; }
    public string Kind { get; set; } = "new-business";
    public DateTimeOffset EffectiveAt { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
    public string Reason { get; set; } = "";
    public string OperationKey { get; set; } = "";
}
public sealed class PolicyVersion : StoredRecord
{
    public Guid PolicyId { get; set; }
    public Guid TermId { get; set; }
    public Guid TransactionId { get; set; }
    public int Sequence { get; set; }
    public int SliceOrdinal { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public string SchemaVersion { get; set; } = "1.0";
    public byte[] ContentHash { get; set; } = [];
    public DateTimeOffset EffectiveAt { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
public sealed class PolicyRegistration
{
    public Guid PolicyId { get; set; }
    public Guid VersionId { get; set; }
    public Guid RiskItemId { get; set; }
    public string NormalizedRegistration { get; set; } = "";
}
public sealed class PolicyDocumentRequest : MutableRecord
{
    public Guid PolicyId { get; set; }
    public Guid TermId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid VersionId { get; set; }
    public string Kind { get; set; } = "";
    public string Purpose { get; set; } = "first-issue";
    public Guid TemplateVersionId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public byte[] PayloadHash { get; set; } = [];
    public Guid WorkId { get; set; }
    public string State { get; set; } = "requested";
}
