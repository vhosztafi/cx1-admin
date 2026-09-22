namespace BackOffice.Infrastructure.Persistence;

public sealed class TaskDocumentAttachment : StoredRecord
{
    public Guid TaskId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public string AuthorLabel { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset? RemovedAt { get; set; }
    public Guid? RemovedBy { get; set; }
    public string? RemovalReason { get; set; }
}
