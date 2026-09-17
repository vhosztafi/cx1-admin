namespace BackOffice.Infrastructure.Persistence;

public sealed class QuoteReferralDecision : StoredRecord
{
    public Guid ReferralId { get; set; }
    public Guid CycleId { get; set; }
    public Guid QuoteId { get; set; }
    public int Sequence { get; set; }
    public string Outcome { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? Question { get; set; }
    public Guid ActorId { get; set; }
    public Guid AuthorityVersionId { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public string ConditionsJson { get; set; } = "[]";
}

public sealed class QuoteCondition : MutableRecord
{
    public Guid DecisionId { get; set; }
    public Guid ReferralId { get; set; }
    public Guid CycleId { get; set; }
    public Guid QuoteId { get; set; }
    public int Sequence { get; set; }
    public string Kind { get; set; } = "";
    public string Code { get; set; } = "";
    public string DefinitionJson { get; set; } = "{}";
    public string Wording { get; set; } = "";
    public string? EndorsementCode { get; set; }
    public Guid? LatestResolutionId { get; set; }
}

public sealed class QuoteConditionResolution : StoredRecord
{
    public Guid ConditionId { get; set; }
    public Guid CycleId { get; set; }
    public Guid QuoteId { get; set; }
    public int Sequence { get; set; }
    public string Outcome { get; set; } = "";
    public Guid EvidenceAssociationId { get; set; }
    public Guid EvidenceReviewId { get; set; }
    public Guid ActorId { get; set; }
    public Guid AuthorityVersionId { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class UnderwritingEvidenceAssociation : MutableRecord
{
    public Guid? CapacitySubmissionId { get; set; }
    public Guid QuoteId { get; set; }
    public Guid CycleId { get; set; }
    public Guid FileId { get; set; }
    public string RequirementCode { get; set; } = "";
    public Guid? RiskItemId { get; set; }
    public Guid? ConditionId { get; set; }
    public Guid? TermsVersionId { get; set; }
    public string InputFingerprint { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid? LatestReviewId { get; set; }
    public Guid? WithdrawnEventId { get; set; }
}

// One append-only event stream matches the public review/withdrawal history.
// Reviews and withdrawals remain distinct kinds with enforced result shapes.
public sealed class UnderwritingEvidenceEvent : StoredRecord
{
    public Guid AssociationId { get; set; }
    public Guid CycleId { get; set; }
    public Guid QuoteId { get; set; }
    public int Sequence { get; set; }
    public string Kind { get; set; } = "";
    public string? Outcome { get; set; }
    public string Reason { get; set; } = "";
    public Guid ActorId { get; set; }
    public Guid? AuthorityVersionId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string InputFingerprint { get; set; } = "";
}
