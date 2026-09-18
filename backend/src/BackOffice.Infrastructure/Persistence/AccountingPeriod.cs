namespace BackOffice.Infrastructure.Persistence;

public sealed class AccountingPeriod : MutableRecord
{
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public string State { get; set; } = "open";
}
