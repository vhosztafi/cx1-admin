namespace BackOffice.Infrastructure.Persistence;

public sealed class ServicingUnderwritingSubmission : StoredRecord
{
    public Guid DraftId { get; set; }
    public Guid CycleId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid RatingId { get; set; }
    public byte[] InputHash { get; set; } = [];
    public string Reason { get; set; } = "";
    public Guid SubmittedBy { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
}
