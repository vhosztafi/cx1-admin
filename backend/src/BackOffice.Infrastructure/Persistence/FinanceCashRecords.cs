namespace BackOffice.Infrastructure.Persistence;

// Receipt is the original cash fact. Applying it moves value out of suspense;
// the receipt posting alone increases cash. Neither assignment nor allocation
// rewrites the original amount, bank reference or origin identity.
public sealed class Receipt : StoredRecord
{
    public Guid AgencyId { get; set; }
    public string OriginKind { get; set; } = "manual";
    public Guid OriginId { get; set; }
    public string BankReference { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "GBP";
    public DateOnly ReceivedOn { get; set; }
    public Guid AccountingPeriodId { get; set; }
    public DateOnly PostingDate { get; set; }
    public DateTimeOffset PostedAt { get; set; }
}

public sealed class ReceiptPayerAssignment : StoredRecord
{
    public Guid ReceiptId { get; set; }
    public int Ordinal { get; set; }
    public string PayerKind { get; set; } = "unidentified";
    public Guid? PayerAgencyId { get; set; }
    public Guid? PayerRelationshipId { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset AssignedAt { get; set; }
    public Guid OperationId { get; set; }
}

// Reversals are new rows with the same positive amount and a unique link to
// one original application. Net applied = originals minus their reversals.
public sealed class Allocation : StoredRecord
{
    public Guid ReceiptId { get; set; }
    public Guid ObligationId { get; set; }
    public decimal Amount { get; set; }
    public Guid? ReversalOfId { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset AppliedAt { get; set; }
    public Guid OperationId { get; set; }
    public int Ordinal { get; set; }
    public Guid AccountingPeriodId { get; set; }
    public DateOnly PostingDate { get; set; }
}
