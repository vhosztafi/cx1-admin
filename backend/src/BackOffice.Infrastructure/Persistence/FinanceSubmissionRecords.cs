namespace BackOffice.Infrastructure.Persistence;

// One pinned insurer operation per batch. Provider outcome and local application
// are separate so a lost response or process crash cannot create a second effect.
public sealed class FinanceBordereauSubmission : MutableRecord
{
    public Guid BatchId { get; set; }
    public Guid VersionId { get; set; }
    public byte[] ContentHash { get; set; } = [];
    public byte[] RequestHash { get; set; } = [];
    public Guid WorkId { get; set; }
    public Guid ScenarioVersionId { get; set; }
    public string OperationKey { get; set; } = "";
    public string State { get; set; } = "queued";
    public string? ProviderState { get; set; }
    public Guid? ProviderOperationId { get; set; }
    public string? ProviderEventId { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}
