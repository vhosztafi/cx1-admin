namespace BackOffice.Infrastructure.Persistence;

public sealed class OperationalDocument : StoredRecord
{
    public Guid SubjectId { get; set; }
    public string Kind { get; set; } = "";
    public string Visibility { get; set; } = "internal";
    public Guid? RelationshipId { get; set; }
}

// Source identity is fixed when generation is requested. The content binding
// is inserted separately once bytes exist; neither row is subsequently edited.
public sealed class DocumentVersion : StoredRecord
{
    public Guid DocumentId { get; set; }
    public int Number { get; set; }
    public string SourceKind { get; set; } = "";
    public Guid? PolicyVersionId { get; set; }
    public Guid? QuoteRevisionId { get; set; }
    public Guid? QuoteTermsVersionId { get; set; }
    public Guid? ServicingTermsVersionId { get; set; }
    public Guid? TemplateVersionId { get; set; }
    public string? SourceHash { get; set; }
    public string? TermsHash { get; set; }
    public string? TemplateHash { get; set; }
    public Guid? PolicyDocumentRequestId { get; set; }
    public Guid? CancellationConsequenceId { get; set; }
    public Guid WorkId { get; set; }
    public string OriginalName { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class DocumentVersionContent : StoredRecord
{
    public Guid VersionId { get; set; }
    public Guid FileObjectId { get; set; }
    public int? PageCount { get; set; }
    public string? RendererVersion { get; set; }
    public string? ProjectionVersion { get; set; }
    public string? FontVersion { get; set; }
}
