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
}
