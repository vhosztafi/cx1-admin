namespace BackOffice.Infrastructure.Persistence;

// The import identity is original bank evidence. Equal bank fields are only a
// candidate duplicate and never collapse distinct ImportKeys.
public sealed class BankLine : StoredRecord
{
    public Guid AgencyId { get; set; }
    public string ImportKey { get; set; } = "";
    public DateOnly ValueDate { get; set; }
    public string Reference { get; set; } = "";
    public decimal SignedAmount { get; set; }
    public string Currency { get; set; } = "GBP";
    public string RawJson { get; set; } = "{}";
    public byte[] RawHash { get; set; } = [];
    public DateTimeOffset ImportedAt { get; set; }
}

public sealed class Reconciliation : MutableRecord
{
    public Guid AgencyId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }
}

public sealed class ReconciliationMatch : StoredRecord
{
    public Guid ReconciliationId { get; set; }
    public Guid BankLineId { get; set; }
    public Guid FinancePostingId { get; set; }
    public decimal SignedAmount { get; set; }
    public Guid? ReversalOfId { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset MatchedAt { get; set; }
}

public sealed class BankLineExclusion : StoredRecord
{
    public Guid ReconciliationId { get; set; }
    public Guid BankLineId { get; set; }
    public Guid DuplicateOfBankLineId { get; set; }
    public string EvidenceReference { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset ExcludedAt { get; set; }
}

public sealed class ReconciliationVariance : StoredRecord
{
    public Guid ReconciliationId { get; set; }
    public Guid BankLineId { get; set; }
    public decimal SignedResidual { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset ExplainedAt { get; set; }
}

// Unmatched posted cash is a separate visible exception. An explanation never
// changes a bank line or creates a synthetic cash posting.
public sealed class ReconciliationTargetVariance : StoredRecord
{
    public Guid ReconciliationId { get; set; }
    public Guid FinancePostingId { get; set; }
    public decimal SignedResidual { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset ExplainedAt { get; set; }
}
