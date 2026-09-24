namespace BackOffice.Infrastructure.Persistence;

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
