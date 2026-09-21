namespace BackOffice.Infrastructure.Persistence;

public sealed class OperationalSubject : StoredRecord
{
    public string Kind { get; set; } = "";
    public Guid? AgencyId { get; set; }
    public Guid? RelationshipId { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? PolicyId { get; set; }
    public Guid? ServicingDraftId { get; set; }
}

public sealed class OperationalTask : MutableRecord
{
    public Guid SubjectId { get; set; }
    public string Reference { get; set; } = "";
    public string TypeCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string Priority { get; set; } = "normal";
    public string State { get; set; } = "open";
    public Guid? OwnerId { get; set; }
    public Guid? TeamId { get; set; }
    public DateOnly? DueOn { get; set; }
    public int EventSequence { get; set; }
    public string? CompletionReason { get; set; }
    public bool SourceChanged { get; set; }
}

public sealed class OperationalTaskEvent : StoredRecord
{
    public Guid TaskId { get; set; }
    public int Sequence { get; set; }
    public string Kind { get; set; } = "";
    public string? Reason { get; set; }
    public string ActorLabel { get; set; } = "";
    public string SnapshotJson { get; set; } = "{}";
}

public sealed class OperationalTaskComment : StoredRecord
{
    public Guid TaskId { get; set; }
    public string Body { get; set; } = "";
    public string AuthorLabel { get; set; } = "";
}

public sealed class OperationalTaskChecklist : MutableRecord
{
    public Guid TaskId { get; set; }
    public int Ordinal { get; set; }
    public string Label { get; set; } = "";
    public bool Required { get; set; }
    public bool Completed { get; set; }
}
