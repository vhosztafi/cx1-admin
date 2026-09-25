namespace BackOffice.Infrastructure.Persistence;

// A permanent monthly view of one posted premium component. These rows never
// create a JournalLine or change the sealed insurance posting.
public sealed class FinanceEarningSlice : StoredRecord
{
    public Guid SourceComponentId { get; set; }
    public Guid ObligationId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid AgencyId { get; set; }
    public Guid PolicyId { get; set; }
    public long PremiumPence { get; set; }
    public DateTimeOffset CoverageStartsAt { get; set; }
    public DateTimeOffset CoverageEndsAt { get; set; }
    public DateOnly MonthStart { get; set; }
    public long EarnedPence { get; set; }
    public int AlgorithmVersion { get; set; } = 1;
    public byte[] SourceHash { get; set; } = [];
    public DateTimeOffset SourcePostedAt { get; set; }
}

// Additive non-insurance movement. Insurance remains in sealed Journal/JournalLine;
// one business source may be applied at most once in this separate posting book.
public sealed class FinancePosting : StoredRecord
{
    public string SourceKind { get; set; } = "";
    public Guid SourceId { get; set; }
    public Guid AgencyId { get; set; }
    public Guid? RelationshipId { get; set; }
    public Guid? PolicyId { get; set; }
    public Guid? TransactionId { get; set; }
    public string DebtorKind { get; set; } = "agency";
    public Guid AccountingPeriodId { get; set; }
    public DateOnly PostingDate { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public string Currency { get; set; } = "GBP";
    public decimal DebtorDelta { get; set; }
    public decimal ProviderDelta { get; set; }
    public decimal CashDelta { get; set; }
    public decimal InternalDelta { get; set; }
    public string Reason { get; set; } = "";
}
