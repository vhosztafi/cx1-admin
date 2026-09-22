namespace BackOffice.Infrastructure.Persistence;
public sealed class MidSubmission:StoredRecord
{
    public Guid PolicyId{get;set;}
    public Guid TermId{get;set;}
    public Guid TransactionId{get;set;}
    public Guid VersionId{get;set;}
    public Guid? BaseVersionId{get;set;}
    public Guid? PolicyMidIntentId{get;set;}
    public Guid? CancellationConsequenceId{get;set;}
    public Guid WorkId{get;set;}
    public Guid ScenarioVersionId{get;set;}
    public string RequestJson{get;set;}="{}";
    public string RequestHash{get;set;}="";
}
public sealed class MidResult:StoredRecord
{
    public Guid SubmissionId{get;set;}
    public Guid ProviderOperationId{get;set;}
    public string ProviderEventId{get;set;}="";
    public string ResultJson{get;set;}="{}";
    public string ContentHash{get;set;}="";
}
