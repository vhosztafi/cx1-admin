namespace BackOffice.Infrastructure.Persistence;

public sealed class RefundApprovalRule : StoredRecord
{
    public string Code { get; set; } = "finance-refund-rule-v1";
    public int Version { get; set; } = 1;
    public decimal SecondApprovalThreshold { get; set; } = 250m;
    public int SmallApprovalCount { get; set; } = 1;
    public int LargeApprovalCount { get; set; } = 2;
}

// Current role amount authority is read before every decision and replay.
public sealed class RefundRoleAuthority : MutableRecord
{
    public string RoleCode { get; set; } = "finance";
    public decimal Limit { get; set; } = 10000m;
    public bool Active { get; set; } = true;
}

public sealed class RefundRequest : MutableRecord
{
    public Guid AgencyId { get; set; }
    public Guid PolicyId { get; set; }
    public Guid CreditObligationId { get; set; }
    public string DebtorKind { get; set; } = "agency";
    public Guid DebtorId { get; set; }
    public string Currency { get; set; } = "GBP";
    public decimal Amount { get; set; }
    public string State { get; set; } = "building";
    public Guid RuleId { get; set; }
    public Guid RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string Reason { get; set; } = "";
}

// The original positive invoice application proves collection from a specific
// receipt/payee. Rejection releases its reservation without deleting evidence.
public sealed class RefundCashReservation : StoredRecord
{
    public Guid RefundRequestId { get; set; }
    public Guid AllocationId { get; set; }
    public decimal Amount { get; set; }
}

public sealed class RefundDecision : StoredRecord
{
    public Guid RefundRequestId { get; set; }
    public string Kind { get; set; } = "approve";
    public Guid ActorId { get; set; }
    public decimal AuthorityLimitSnapshot { get; set; }
    public Guid RuleId { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset DecidedAt { get; set; }
}
