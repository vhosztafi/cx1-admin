namespace BackOffice.Infrastructure.Persistence;

public sealed class CommercialExposureBook : StoredRecord
{
    public Guid ProductId { get; set; }
    public Guid ProviderId { get; set; }
    public string Code { get; set; } = "";
}

public sealed class CommercialExposureBinder : StoredRecord
{
    public Guid BookId { get; set; }
    public Guid BinderVersionId { get; set; }
}

public sealed class CommercialExposureLimitVersion : StoredRecord
{
    public Guid BookId { get; set; }
    public string District { get; set; } = "*";
    public int Version { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset EffectiveTo { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public Guid? SupersedesLimitId { get; set; }
    public string PublicationJson { get; set; } = "{}";
    public byte[] ContentHash { get; set; } = [];
}

// Source metadata is copied once and verified by SQL. Book assessment does not
// join or lock mutable foreign policies or close historical exposure intervals.
public sealed class CommercialExposureVersion : StoredRecord
{
    public Guid BookId { get; set; }
    public Guid BinderVersionId { get; set; }
    public Guid PolicyId { get; set; }
    public Guid TermId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid VersionId { get; set; }
    public byte[] SourceHash { get; set; } = [];
    public DateTimeOffset TermStartsAt { get; set; }
    public DateTimeOffset TermEndsAt { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
    public string TransactionKind { get; set; } = "";
    public int TransactionSequence { get; set; }
    public int SliceOrdinal { get; set; }
    public string LocationsJson { get; set; } = "[]";
}

// SQL materializes the complete set from the verified header in the same INSERT.
public sealed class CommercialExposureLocationRecord
{
    public Guid ExposureVersionId { get; set; }
    public Guid RiskItemId { get; set; }
    public string District { get; set; } = "";
    public decimal SumInsured { get; set; }
}

public sealed class CommercialExposureIssueDecision : StoredRecord
{
    public Guid ExposureVersionId { get; set; }
    public DateTimeOffset AssessedAt { get; set; }
    public string DecisionJson { get; set; } = "{}";
    public byte[] DecisionHash { get; set; } = [];
}
