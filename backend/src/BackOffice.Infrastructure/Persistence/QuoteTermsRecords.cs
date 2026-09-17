namespace BackOffice.Infrastructure.Persistence;

public sealed class TemplateVersion : StoredRecord
{
    public string Code { get; set; } = "";
    public int Version { get; set; }
    public string Kind { get; set; } = "quote-terms";
    public Guid ProductId { get; set; }
    public string State { get; set; } = "published";
    public string ContentJson { get; set; } = "{}";
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset EffectiveTo { get; set; }
}

public sealed class QuoteTermsVersion : StoredRecord
{
    public Guid QuoteId { get; set; }
    public Guid CycleId { get; set; }
    public Guid RatingId { get; set; }
    public int Number { get; set; }
    public Guid TemplateVersionId { get; set; }
    public string TermsHash { get; set; } = "";
    public string AssuranceHashAtPreparation { get; set; } = "";
    public string TermsJson { get; set; } = "{}";
    public DateTimeOffset PreparedAt { get; set; }
    public Guid PreparedBy { get; set; }
}

public sealed class QuoteTermsDelivery : MutableRecord
{
    public Guid QuoteId { get; set; }
    public Guid CycleId { get; set; }
    public Guid TermsVersionId { get; set; }
    public Guid WorkId { get; set; }
    public Guid ScenarioVersionId { get; set; }
    public string RecipientSnapshotJson { get; set; } = "[]";
    public string PayloadJson { get; set; } = "{}";
    public string PayloadHash { get; set; } = "";
    public string AssuranceHashAtSend { get; set; } = "";
    public Guid SentBy { get; set; }
    public string State { get; set; } = "queued";
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? ProviderOperationId { get; set; }
    public Guid? AttemptId { get; set; }
    public string? OutcomeCode { get; set; }
}

public sealed class QuoteAcceptance : StoredRecord
{
    public Guid QuoteId { get; set; }
    public Guid CycleId { get; set; }
    public Guid RatingId { get; set; }
    public Guid TermsVersionId { get; set; }
    public Guid DeliveryId { get; set; }
    public string TermsHash { get; set; } = "";
    public string AssuranceHash { get; set; } = "";
    public string AccepterLabel { get; set; } = "";
    public DateTimeOffset AcceptedAt { get; set; }
    public string Channel { get; set; } = "";
    public Guid EvidenceAssociationId { get; set; }
    public Guid EvidenceReviewId { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
