namespace BackOffice.Infrastructure.Persistence;

public sealed class Quote : MutableRecord
{
    public long Number { get; set; }
    public string Reference { get; set; } = "";
    public Guid AgencyId { get; set; }
    public Guid ClientId { get; set; }
    public Guid RelationshipId { get; set; }
    public Guid ProductId { get; set; }
    public string State { get; set; } = "draft";
    // Nullable only for the initial in-transaction insert. Commands must set it
    // before commit; privileged unfinished rows are detected by integrity checks.
    public Guid? CurrentRevisionId { get; set; }
    public Guid? CurrentUnderwritingCycleId { get; set; }
    public Guid? BoundPolicyId { get; set; }
    public DateTimeOffset? CaptureClosedAt { get; set; }
    public string? CaptureClosedReason { get; set; }
    public Guid? AssignedUserId { get; set; }
    public Guid? ClonedFromQuoteRevisionId { get; set; }
}

public sealed class QuoteRevision : StoredRecord
{
    // Retained ownership at save time; reassociation never rewrites old revisions.
    public Guid ClientId { get; set; }
    public Guid RelationshipId { get; set; }
    public Guid QuoteId { get; set; }
    public Guid AgencyId { get; set; }
    public Guid ProductId { get; set; }
    public int Number { get; set; }
    public Guid ProductVersionId { get; set; }
    public Guid AgencyTermsVersionId { get; set; }
    public string SchemaVersion { get; set; } = "1.0";
    public string QuestionSetVersion { get; set; } = "";
    public string ReferenceVersionsJson { get; set; } = "{}";
    public string ProposalJson { get; set; } = "{}";
    public string TermIntentJson { get; set; } = "{}";
    public byte[] ContentHash { get; set; } = [];
    public string? Reason { get; set; }
    public Guid SavedBy { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Rebuilt from the current revision in the same transaction as pointer changes.
public sealed class QuoteRegistration
{
    public Guid QuoteId { get; set; }
    public Guid VehicleId { get; set; }
    public string NormalizedRegistration { get; set; } = "";
}

public sealed class QuoteActivity : StoredRecord
{
    public Guid QuoteId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid ActorId { get; set; }
    public string EventType { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
