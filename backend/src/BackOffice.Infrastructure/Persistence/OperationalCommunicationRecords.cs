namespace BackOffice.Infrastructure.Persistence;

public sealed class InternalNote:StoredRecord
{
    public Guid SubjectId {get;set;}
    public string Body {get;set;}="";
    public string AuthorLabel {get;set;}="";
}
public sealed class OperationalThread:StoredRecord
{
    public Guid SubjectId {get;set;}
    public string Visibility {get;set;}="internal";
    public Guid? RelationshipId {get;set;}
    public string Subject {get;set;}="";
    public string AuthorLabel {get;set;}="";
}
public sealed class OperationalMessageDraft:MutableRecord
{
    public Guid ThreadId {get;set;}
    public string Body {get;set;}="";
    public string AuthorLabel {get;set;}="";
    public string State {get;set;}="draft";
}
public sealed class MessageDraftRecipient:StoredRecord
{
    public Guid MessageId {get;set;}
    public Guid ContactId {get;set;}
}
public sealed class MessageDraftAttachment:StoredRecord
{
    public Guid MessageId {get;set;}
    public Guid DocumentVersionId {get;set;}
}
