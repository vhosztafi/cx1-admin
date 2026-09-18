namespace BackOffice.Infrastructure.Persistence;

// Capacity ownership is separate from the original quote decision graph.
public sealed class ServicingCapacityCase : MutableRecord
{
    public Guid DraftId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid CycleId { get; set; }
    public Guid RatingId { get; set; }
    public Guid ReferralId { get; set; }
    public Guid ProviderId { get; set; }
    public Guid BinderVersionId { get; set; }
    public Guid RaisedBy { get; set; }
    public string Reason { get; set; } = "";
    public string State { get; set; } = "draft";
    public Guid? CurrentSubmissionId { get; set; }
}

public sealed class ServicingCapacitySubmission : StoredRecord
{
    public Guid CaseId { get; set; }
    public Guid DraftId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid CycleId { get; set; }
    public Guid RatingId { get; set; }
    public int Sequence { get; set; }
    public string Body { get; set; } = "";
    public string Reason { get; set; } = "";
    public string ContextJson { get; set; } = "{}";
    public byte[] ContextHash { get; set; } = [];
    public Guid WorkId { get; set; }
    public Guid ScenarioVersionId { get; set; }
    public Guid SubmittedBy { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset ResponseDueAt { get; set; }
}
