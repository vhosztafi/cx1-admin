namespace BackOffice.Infrastructure.Persistence;

public sealed class AgencyResponseRequest : MutableRecord
{
    public Guid MessageVersionId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid RelationshipId { get; set; }
    public string Reference { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Instruction { get; set; } = "";
    public string Reason { get; set; } = "";
    public string State { get; set; } = "awaiting-response";
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? ResolutionReason { get; set; }
}
