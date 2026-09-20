namespace BackOffice.Infrastructure.Persistence;

public sealed class RenewalPreparationVersion : StoredRecord
{
    public Guid DraftId { get; set; }
    public Guid PolicyId { get; set; }
    public Guid BaseTermId { get; set; }
    public Guid BaseVersionId { get; set; }
    public int Sequence { get; set; }
    public int TermMonths { get; set; }
    public int? EndUtcOffsetMinutes { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string TermIntentJson { get; set; } = "{}";
    public Guid ProductId { get; set; }
    public Guid ProductVersionId { get; set; }
    public Guid BinderVersionId { get; set; }
    public Guid AgencyTermsVersionId { get; set; }
    public Guid RuleSettingVersionId { get; set; }
    public Guid? FairValueAssessmentId { get; set; }
}

// Experience is supplied before rating. Its evidence association therefore owns
// exact immutable facts and file bytes independently of a priced cycle.
public sealed class RenewalExperienceVersion : StoredRecord
{
    public Guid DraftId { get; set; }
    public int Sequence { get; set; }
    public DateOnly ObservationStartsOn { get; set; }
    public DateOnly ObservationEndsOn { get; set; }
    public int ClaimCount { get; set; }
    public decimal Paid { get; set; }
    public decimal Outstanding { get; set; }
    public decimal EarnedPremium { get; set; }
    public string SourceCode { get; set; } = "";
    public string SourceReference { get; set; } = "";
    public Guid EvidenceAssociationId { get; set; }
    public Guid? CommercialRevisionId { get; set; }
    public string? CommercialSubjectsJson { get; set; }
}

public sealed class RenewalExperienceEvidence : StoredRecord
{
    public Guid DraftId { get; set; }
    public Guid FileId { get; set; }
}

// Reviews refer to an exact version. A fresh set of supplied amounts can never
// inherit approval just because the uploader chose the same file again.
public sealed class RenewalExperienceReview : StoredRecord
{
    public Guid DraftId { get; set; }
    public Guid ExperienceVersionId { get; set; }
    public int Sequence { get; set; }
    public string Outcome { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid AuthorityVersionId { get; set; }
    public Guid AuthorityGrantId { get; set; }
}

// Product assessment evidence has product ownership, never a fictional quote
// or servicing draft created solely to hold configuration files.
public sealed class ProductEvidenceFileVersion : StoredRecord
{
    public Guid ProductId { get; set; }
    public Guid ProductVersionId { get; set; }
    public Guid BinderVersionId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public int ByteLength { get; set; }
    public string Sha256 { get; set; } = "";
    public string ScreeningState { get; set; } = "accepted";
    public string ScreeningMethod { get; set; } = "demo-signature-v1";
}

public sealed class FairValueAssessmentVersion : StoredRecord
{
    public Guid ProductId { get; set; }
    public Guid ProductVersionId { get; set; }
    public Guid BinderVersionId { get; set; }
    public Guid EvidenceFileVersionId { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }
    public string Outcome { get; set; } = "";
    public Guid ApprovedBy { get; set; }
    public DateTimeOffset ApprovedAt { get; set; }
    public string Reason { get; set; } = "";
}
