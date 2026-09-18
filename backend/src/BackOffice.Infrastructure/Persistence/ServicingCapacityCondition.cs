namespace BackOffice.Infrastructure.Persistence;

public sealed class ServicingCapacityCondition : MutableRecord
{
    public Guid ResponseId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid CaseId { get; set; }
    public Guid DraftId { get; set; }
    public Guid CycleId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid RatingId { get; set; }
    public int Sequence { get; set; }
    public string Code { get; set; } = "";
    public string Kind { get; set; } = "";
    public string DefinitionJson { get; set; } = "{}";
    public string EffectiveDatesJson { get; set; } = "[]";
}
