namespace BackOffice.Infrastructure.Persistence;

// The head is the only mutable part of a batch. Every published draft state is
// represented by a new immutable version and a complete set of pinned members.
public sealed class FinanceBordereauBatch : MutableRecord
{
    public Guid ProviderId { get; set; }
    public Guid AccountingPeriodId { get; set; }
    public Guid CurrentVersionId { get; set; }
}

public sealed class FinanceBordereauVersion : StoredRecord
{
    public Guid BatchId { get; set; }
    public int Number { get; set; }
    public Guid? ParentVersionId { get; set; }
    public DateTimeOffset SourceCutoff { get; set; }
    public byte[] SourceHash { get; set; } = [];
    public byte[] MembersHash { get; set; } = [];
    public string SchemaVersion { get; set; } = "1";
    public string State { get; set; } = "unvalidated";
    public string ValidationJson { get; set; } = "[]";
    public byte[]? ContentBytes { get; set; }
    public byte[]? ContentHash { get; set; }
}

public sealed class FinanceBordereauMember : StoredRecord
{
    public Guid VersionId { get; set; }
    public Guid SourceJournalId { get; set; }
    public Guid PolicyId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid AgencyId { get; set; }
    public Guid ProductVersionId { get; set; }
    public Guid AgencyTermsVersionId { get; set; }
    public DateOnly PostingDate { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public string Currency { get; set; } = "GBP";
    public decimal Premium { get; set; }
    public decimal Tax { get; set; }
    public decimal Fee { get; set; }
    public decimal Commission { get; set; }
    public decimal NetDue { get; set; }
    public string PolicyReference { get; set; } = "";
    public string ProviderProductCode { get; set; } = "";
    public string AgencyReference { get; set; } = "";
    public Guid? CorrectionActorId { get; set; }
    public string? CorrectionReason { get; set; }
    public DateTimeOffset? CorrectedAt { get; set; }
    public Guid? ExclusionActorId { get; set; }
    public string? ExclusionReason { get; set; }
    public DateTimeOffset? ExcludedAt { get; set; }
}
