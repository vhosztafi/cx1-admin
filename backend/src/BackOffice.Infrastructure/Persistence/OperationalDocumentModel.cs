using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureOperationalDocuments(ModelBuilder model)
    {
        var document = Record<OperationalDocument>(model, "Document"); document.ToTable(t=>t.UseSqlOutputClause(false));
        Text(document,("Kind",60),("Visibility",20));
        document.HasOne<OperationalSubject>().WithMany().HasForeignKey(x=>x.SubjectId).OnDelete(DeleteBehavior.NoAction);
        document.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x=>x.RelationshipId).OnDelete(DeleteBehavior.NoAction);
        document.HasIndex(x=>new{x.SubjectId,x.CreatedAt,x.Id});
        Check(document,"Creator","[CreatedBy] IS NOT NULL");
        Check(document,"Kind","[Kind] IN ('quotation','statement-of-fact','policy-schedule','policy-certificate','endorsement','renewal-invitation','cancellation-notice','evidence')");
        Check(document,"Audience","([Visibility] IN ('internal','insurer') AND [RelationshipId] IS NULL) OR ([Visibility]='agency' AND [RelationshipId] IS NOT NULL)");

        var version = Record<DocumentVersion>(model,"DocumentVersion"); version.ToTable(t=>t.UseSqlOutputClause(false));
        Text(version,("SourceKind",30),("SourceHash",64),("TermsHash",64),("TemplateHash",64),("OriginalName",255),("Reason",1000));
        version.HasOne<OperationalDocument>().WithMany().HasForeignKey(x=>x.DocumentId).OnDelete(DeleteBehavior.NoAction);
        version.HasOne<PolicyVersion>().WithMany().HasForeignKey(x=>x.PolicyVersionId).OnDelete(DeleteBehavior.NoAction);
        version.HasOne<QuoteRevision>().WithMany().HasForeignKey(x=>x.QuoteRevisionId).OnDelete(DeleteBehavior.NoAction);
        version.HasOne<QuoteTermsVersion>().WithMany().HasForeignKey(x=>x.QuoteTermsVersionId).OnDelete(DeleteBehavior.NoAction);
        version.HasOne<ServicingTermsVersion>().WithMany().HasForeignKey(x=>x.ServicingTermsVersionId).OnDelete(DeleteBehavior.NoAction);
        version.HasOne<TemplateVersion>().WithMany().HasForeignKey(x=>x.TemplateVersionId).OnDelete(DeleteBehavior.NoAction);
        version.HasOne<PolicyDocumentRequest>().WithMany().HasForeignKey(x=>x.PolicyDocumentRequestId).OnDelete(DeleteBehavior.NoAction);
        version.HasOne<CancellationConsequence>().WithMany().HasForeignKey(x=>x.CancellationConsequenceId).OnDelete(DeleteBehavior.NoAction);
        version.HasOne<OutboxWork>().WithMany().HasForeignKey(x=>x.WorkId).OnDelete(DeleteBehavior.NoAction);
        version.HasIndex(x=>new{x.DocumentId,x.Number}).IsUnique(); version.HasIndex(x=>x.WorkId).IsUnique();
        foreach(var field in new[]{"PolicyDocumentRequestId","CancellationConsequenceId"}) version.HasIndex(field).IsUnique().HasFilter($"[{field}] IS NOT NULL");
        foreach(var field in new[]{"SourceHash","TemplateHash","TermsHash"})
        {
            version.Property<string?>(field).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
            Check(version,field,$"[{field}] IS NULL OR (DATALENGTH([{field}])=64 AND [{field}] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2)");
        }
        Check(version,"Identity","[CreatedBy] IS NOT NULL AND [Number]>0 AND LEN(TRIM([OriginalName]))>0 AND LEN(TRIM([Reason]))>0");
        Check(version,"Request","NOT ([PolicyDocumentRequestId] IS NOT NULL AND [CancellationConsequenceId] IS NOT NULL) AND (([PolicyDocumentRequestId] IS NULL AND [CancellationConsequenceId] IS NULL) OR [SourceKind]='policy-version')");
        Check(version,"Source","""
            ([SourceKind]='policy-version' AND [PolicyVersionId] IS NOT NULL AND [QuoteRevisionId] IS NULL AND [QuoteTermsVersionId] IS NULL AND [ServicingTermsVersionId] IS NULL AND [TermsHash] IS NULL) OR
            ([SourceKind]='quote-revision' AND [QuoteRevisionId] IS NOT NULL AND [PolicyVersionId] IS NULL AND [ServicingTermsVersionId] IS NULL AND (([QuoteTermsVersionId] IS NULL AND [TermsHash] IS NULL) OR ([QuoteTermsVersionId] IS NOT NULL AND [TermsHash] IS NOT NULL))) OR
            ([SourceKind]='servicing-terms' AND [ServicingTermsVersionId] IS NOT NULL AND [PolicyVersionId] IS NULL AND [QuoteRevisionId] IS NULL AND [QuoteTermsVersionId] IS NULL AND [TermsHash] IS NULL) OR
            ([SourceKind]='upload' AND [PolicyVersionId] IS NULL AND [QuoteRevisionId] IS NULL AND [QuoteTermsVersionId] IS NULL AND [ServicingTermsVersionId] IS NULL AND [TermsHash] IS NULL)
            """);
        Check(version,"Template","([SourceKind]='upload' AND [TemplateVersionId] IS NULL AND [SourceHash] IS NULL AND [TemplateHash] IS NULL) OR ([SourceKind]<>'upload' AND [TemplateVersionId] IS NOT NULL AND [SourceHash] IS NOT NULL AND [TemplateHash] IS NOT NULL)");

        var content = Record<DocumentVersionContent>(model,"DocumentVersionContent"); content.ToTable(t=>t.UseSqlOutputClause(false));
        Text(content,("RendererVersion",100),("ProjectionVersion",100),("FontVersion",100));
        content.HasOne<DocumentVersion>().WithMany().HasForeignKey(x=>x.VersionId).OnDelete(DeleteBehavior.NoAction);
        content.HasOne<FileObject>().WithMany().HasForeignKey(x=>x.FileObjectId).OnDelete(DeleteBehavior.NoAction);
        content.HasIndex(x=>x.VersionId).IsUnique(); content.HasIndex(x=>x.FileObjectId).IsUnique();
        Check(content,"Creator","[CreatedBy] IS NOT NULL");
        Check(content,"Renderer","([PageCount] IS NULL AND [RendererVersion] IS NULL AND [ProjectionVersion] IS NULL AND [FontVersion] IS NULL) OR ([PageCount] BETWEEN 1 AND 300 AND [RendererVersion] IS NOT NULL AND [ProjectionVersion] IS NOT NULL AND [FontVersion] IS NOT NULL)");
    }
}
