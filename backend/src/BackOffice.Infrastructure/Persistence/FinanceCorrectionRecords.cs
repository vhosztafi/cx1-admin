namespace BackOffice.Infrastructure.Persistence;

// Source evidence for a new balanced FinancePosting. Never edits the original.
public sealed class FinanceCorrection : StoredRecord
{
    public string OriginalSourceKind { get; set; } = "";
    public Guid OriginalSourceId { get; set; }
    public Guid AgencyId { get; set; }
    public Guid? RelationshipId { get; set; }
    public Guid? PolicyId { get; set; }
    public Guid? TransactionId { get; set; }
    public string DebtorKind { get; set; } = "agency";
    public string Reason { get; set; } = "";
}
