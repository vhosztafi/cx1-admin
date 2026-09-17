namespace BackOffice.Infrastructure.Persistence;

public sealed class IssueFinancialObligation : StoredRecord
{
    public Guid PolicyId { get; set; }
    public Guid TermId { get; set; }
    public Guid TransactionId { get; set; }
    public string Purpose { get; set; } = "first-issue";
    public Guid AgencyId { get; set; }
    public Guid ClientId { get; set; }
    public Guid RelationshipId { get; set; }
    public Guid ProviderId { get; set; }
    public Guid AgencyTermsVersionId { get; set; }
    public string DebtorKind { get; set; } = "agency";
    public Guid? DebtorAgencyId { get; set; }
    public Guid? DebtorRelationshipId { get; set; }
    public string Currency { get; set; } = "GBP";
    public string Settlement { get; set; } = "net-remittance";
    public decimal Premium { get; set; }
    public decimal Tax { get; set; }
    public decimal Fee { get; set; }
    public decimal Commission { get; set; }
    public decimal FeeShare { get; set; }
    public decimal GrossDue { get; set; }
    public decimal InvoiceDue { get; set; }
    public decimal NetDue { get; set; }
    public decimal BrokerPayable { get; set; }
    public string TermsSnapshotJson { get; set; } = "{}";
}
// One immutable identity per original component makes later earning/reversals
// reference an actual stored amount and coverage interval, not a recomputed rate.
public sealed class IssueFinancialComponent : StoredRecord
{
    public Guid ObligationId { get; set; }
    public Guid TransactionId { get; set; }
    public string Code { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTimeOffset CoverageStartsAt { get; set; }
    public DateTimeOffset CoverageEndsAt { get; set; }
}
public sealed class Journal : StoredRecord
{
    public Guid TransactionId { get; set; }
    public Guid ObligationId { get; set; }
    public string Purpose { get; set; } = "first-issue";
    public string Currency { get; set; } = "GBP";
    // Created draft with all component lines, then sealed atomically. SQL checks
    // exact component amounts, settlement accounts and balance on posting.
    public DateTimeOffset? PostedAt { get; set; }
}
public sealed class JournalLine : StoredRecord
{
    public Guid JournalId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid SourceComponentId { get; set; }
    public string ComponentCode { get; set; } = "";
    public string AccountCode { get; set; } = "";
    public string PartyKind { get; set; } = "";
    public Guid? PartyId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public DateTimeOffset CoverageStartsAt { get; set; }
    public DateTimeOffset CoverageEndsAt { get; set; }
}
