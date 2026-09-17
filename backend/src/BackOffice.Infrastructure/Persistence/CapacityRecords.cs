namespace BackOffice.Infrastructure.Persistence;

public sealed class CapacityEscalation : MutableRecord
{
    public Guid QuoteId { get; set; }
    public Guid CycleId { get; set; }
    public Guid ReferralId { get; set; }
    public Guid ProviderId { get; set; }
    public Guid BinderVersionId { get; set; }
    public Guid RaisedBy { get; set; }
    public string Reason { get; set; } = "";
    public string State { get; set; } = "draft";
    public Guid? CurrentSubmissionId { get; set; }
    public Guid? CurrentResponseId { get; set; }
}
public sealed class CapacitySubmission : StoredRecord
{
    public Guid QuoteId { get; set; }
    public Guid CycleId { get; set; }
    public Guid EscalationId { get; set; }
    public int Sequence { get; set; }
    public string Body { get; set; } = "";
    public string ContextJson { get; set; } = "{}";
    public string ContextHash { get; set; } = "";
    public Guid WorkId { get; set; }
    public Guid ScenarioVersionId { get; set; }
    public Guid SubmittedBy { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset ResponseDueAt { get; set; }
}
public sealed class CapacitySubmissionEvidence : StoredRecord
{
    public Guid QuoteId { get; set; }
    public Guid CycleId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid EvidenceAssociationId { get; set; }
}
public sealed class CapacityMessage : StoredRecord
{
    public Guid QuoteId { get; set; }
    public Guid CycleId { get; set; }
    public Guid EscalationId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid ReferralId { get; set; }
    public Guid ProviderId { get; set; }
    public int Sequence { get; set; }
    public string Direction { get; set; } = "outbound";
    public string Provenance { get; set; } = "staff-submission";
    public string? Outcome { get; set; }
    public string? ProviderUnderwriter { get; set; }
    public string? ProviderReference { get; set; }
    public string? ProviderEventId { get; set; }
    public Guid? InboxId { get; set; }
    public string Body { get; set; } = "";
    public string DefinitionJson { get; set; } = "{}";
    public byte[] ContentHash { get; set; } = [];
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public Guid RecordedBy { get; set; }
    public Guid? EvidenceAssociationId { get; set; }
    public Guid? EvidenceReviewId { get; set; }
    public Guid? DecisionId { get; set; }
    public string ApplicationState { get; set; } = "applied";
}
