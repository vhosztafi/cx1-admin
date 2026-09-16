namespace BackOffice.Infrastructure.Persistence;

// Provider query/result data stays in quote-owned tables, never generic job payloads.
public sealed class QuoteLookup : MutableRecord
{
    public Guid QuoteId { get; set; }
    public Guid RevisionId { get; set; }
    public string Kind { get; set; } = "";
    public string TargetScope { get; set; } = "";
    public Guid? RiskItemId { get; set; }
    public string Query { get; set; } = "";
    public string InputFingerprint { get; set; } = "";
    public byte[] RequestHash { get; set; } = [];
    public string ReferenceVersion { get; set; } = "";
    public Guid ScenarioVersionId { get; set; }
    public string Scenario { get; set; } = "";
    public Guid WorkId { get; set; }
    public string State { get; set; } = "pending";
    public string? ResultJson { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class QuoteLookupSelection : StoredRecord
{
    public Guid LookupId { get; set; }
    public Guid QuoteId { get; set; }
    public Guid SourceRevisionId { get; set; }
    public string InputFingerprint { get; set; } = "";
    public Guid NewRevisionId { get; set; }
    public Guid? CandidateId { get; set; }
    public string? ManualReason { get; set; }
    public Guid ActorId { get; set; }
}
