namespace BackOffice.Infrastructure.Persistence;

// One immutable provider intent. A definite rejection permits a reviewed successor;
// an uncertain or accepted intent must always recover this exact operation.
public sealed class FinanceRefundPayment : MutableRecord
{
    public Guid RefundRequestId { get; set; }
    public Guid AgencyId { get; set; }
    public Guid CreditObligationId { get; set; }
    public string DebtorKind { get; set; } = "agency";
    public Guid DebtorId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "GBP";
    public Guid WorkId { get; set; }
    public Guid ScenarioVersionId { get; set; }
    public string OperationKey { get; set; } = "";
    public byte[] RequestHash { get; set; } = [];
    public Guid? PriorPaymentId { get; set; }
    public string ReviewReason { get; set; } = "";
    public string State { get; set; } = "queued";
    public string? ProviderState { get; set; }
    public Guid? ProviderOperationId { get; set; }
    public string? ProviderEventId { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}
