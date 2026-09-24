namespace BackOffice.Infrastructure.Persistence;

public sealed class AccountingPeriod : MutableRecord
{
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public string State { get; set; } = "open";
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }
    public string? CloseReason { get; set; }
    public DateTimeOffset? SourceCutoff { get; set; }
    public string? CloseChecklistJson { get; set; }
}
