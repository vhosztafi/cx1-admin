namespace BackOffice.Infrastructure.Persistence;

public sealed class ServicingTermsVersion : StoredRecord
{
    public Guid DraftId { get; set; }
    public Guid CycleId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid BaseVersionId { get; set; }
    public Guid RatingId { get; set; }
    public Guid TemplateVersionId { get; set; }
    public int Sequence { get; set; }
    public string TermsJson { get; set; } = "{}";
    public string TermsHash { get; set; } = "";
    public string AssuranceHashAtPreparation { get; set; } = "";
    public Guid PreparedBy { get; set; }
    public DateTimeOffset PreparedAt { get; set; }
}
