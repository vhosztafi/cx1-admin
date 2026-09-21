namespace BackOffice.Infrastructure.Persistence;

// Immutable link from one owned source event and logical rule to its human task.
// Republishing a rule never rewrites this first-applied rule/source snapshot.
public sealed class WorkflowTaskBinding : StoredRecord
{
    public Guid TaskId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid RuleVersionId { get; set; }
    public string RuleCode { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public Guid SourceEventId { get; set; }
    public Guid? QuoteReferralId { get; set; }
    public Guid? ServicingReferralId { get; set; }
    public Guid? QuoteQueryDecisionId { get; set; }
    public Guid? ServicingQueryDecisionId { get; set; }
    public Guid? MatchInformationRequestId { get; set; }
    public Guid? PolicyTermId { get; set; }
    public Guid? AgencyFollowUpId { get; set; }
    public Guid? JobExceptionId { get; set; }
    public string SourceSnapshotJson { get; set; } = "{}";
    public string SourceHash { get; set; } = "";
    public string RuleSnapshotJson { get; set; } = "{}";
    public string RuleHash { get; set; } = "";
}
