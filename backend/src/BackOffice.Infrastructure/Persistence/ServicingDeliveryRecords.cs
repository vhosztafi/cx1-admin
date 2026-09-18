namespace BackOffice.Infrastructure.Persistence;

public sealed class ServicingTermsDelivery : MutableRecord
{
    public Guid DraftId { get; set; }
    public Guid CycleId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid RatingId { get; set; }
    public Guid TermsVersionId { get; set; }
    public Guid WorkId { get; set; }
    public Guid ScenarioVersionId { get; set; }
    public string RecipientSnapshotJson { get; set; }="[]";
    public string PayloadJson { get; set; }="{}";
    public string PayloadHash { get; set; }="";
    public string AssuranceHashAtSend { get; set; }="";
    public Guid SentBy { get; set; }
    public string State { get; set; }="queued";
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? ProviderOperationId { get; set; }
    public Guid? AttemptId { get; set; }
    public string? OutcomeCode { get; set; }
}
