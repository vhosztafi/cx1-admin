using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static class OperationalDocumentStorageGuards
{
    internal static void Up(MigrationBuilder migration)
    {
        foreach(var table in new[]{"Document","DocumentVersion","DocumentVersionContent"})
            migration.Sql($"CREATE TRIGGER TR_{table}_AppendOnly ON [{table}] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 52000,'Document history is immutable.',1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_Document_Audience ON Document AFTER INSERT AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalSubject s ON s.Id=i.SubjectId
                JOIN ClientAgencyRelationship r ON r.Id=i.RelationshipId
                LEFT JOIN Quote q ON q.Id=s.QuoteId LEFT JOIN Policy p ON p.Id=s.PolicyId
                LEFT JOIN ServicingDraft d ON d.Id=s.ServicingDraftId LEFT JOIN Policy dp ON dp.Id=d.PolicyId
                WHERE i.Visibility='agency' AND NOT
                  ((s.Kind='agency' AND s.AgencyId=r.AgencyId) OR (s.Kind='relationship' AND s.RelationshipId=r.Id) OR
                   (s.Kind='quote' AND q.RelationshipId=r.Id) OR (s.Kind='policy' AND p.RelationshipId=r.Id) OR (s.Kind='servicing-draft' AND dp.RelationshipId=r.Id)))
                THROW 52001,'Document audience must belong to its original subject.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_DocumentVersion_Source ON DocumentVersion AFTER INSERT AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted i JOIN Document d ON d.Id=i.DocumentId JOIN OperationalSubject s ON s.Id=d.SubjectId
                JOIN PolicyVersion v ON v.Id=i.PolicyVersionId JOIN Policy p ON p.Id=v.PolicyId JOIN TemplateVersion t ON t.Id=i.TemplateVersionId
                WHERE s.PolicyId IS NULL OR s.PolicyId<>p.Id OR t.ProductId<>p.ProductId OR i.SourceHash<>LOWER(CONVERT(varchar(64),v.ContentHash,2)) OR
                  d.Kind NOT IN ('statement-of-fact','policy-schedule','policy-certificate','endorsement','cancellation-notice') OR
                  t.Kind<>CASE WHEN d.Kind='statement-of-fact' THEN 'policy-statement' ELSE d.Kind END)
                THROW 52002,'Policy document source or template does not belong to its subject.',1;
              IF EXISTS(SELECT 1 FROM inserted i JOIN Document d ON d.Id=i.DocumentId JOIN OperationalSubject s ON s.Id=d.SubjectId
                JOIN QuoteRevision r ON r.Id=i.QuoteRevisionId JOIN TemplateVersion t ON t.Id=i.TemplateVersionId
                LEFT JOIN QuoteTermsVersion qt ON qt.Id=i.QuoteTermsVersionId LEFT JOIN UnderwritingCycle c ON c.Id=qt.CycleId
                WHERE s.QuoteId IS NULL OR s.QuoteId<>r.QuoteId OR t.ProductId<>r.ProductId OR i.SourceHash<>LOWER(CONVERT(varchar(64),r.ContentHash,2)) OR
                  d.Kind NOT IN ('quotation','statement-of-fact') OR (d.Kind='quotation' AND i.QuoteTermsVersionId IS NULL) OR
                  (i.QuoteTermsVersionId IS NOT NULL AND (qt.QuoteId<>r.QuoteId OR c.QuoteRevisionId<>r.Id OR qt.TemplateVersionId<>t.Id OR i.TermsHash<>qt.TermsHash COLLATE Latin1_General_100_BIN2)) OR
                  t.Kind NOT IN ('quote-terms','policy-statement'))
                THROW 52003,'Quote document source must pin owned revision and exact terms.',1;
              IF EXISTS(SELECT 1 FROM inserted i JOIN Document doc ON doc.Id=i.DocumentId JOIN OperationalSubject s ON s.Id=doc.SubjectId
                JOIN ServicingTermsVersion st ON st.Id=i.ServicingTermsVersionId JOIN ServicingDraft d ON d.Id=st.DraftId JOIN Policy p ON p.Id=d.PolicyId
                JOIN TemplateVersion t ON t.Id=i.TemplateVersionId
                WHERE NOT ((s.ServicingDraftId IS NOT NULL AND s.ServicingDraftId=d.Id) OR (s.PolicyId IS NOT NULL AND s.PolicyId=p.Id)) OR
                  t.ProductId<>p.ProductId OR st.TemplateVersionId<>t.Id OR i.SourceHash<>st.TermsHash COLLATE Latin1_General_100_BIN2 OR
                  doc.Kind NOT IN ('quotation','statement-of-fact','renewal-invitation') OR
                  (doc.Kind='renewal-invitation' AND d.Kind<>'renewal') OR (doc.Kind='quotation' AND d.Kind='renewal'))
                THROW 52004,'Servicing document source must pin owned exact terms.',1;
              IF EXISTS(SELECT 1 FROM inserted i JOIN TemplateVersion t ON t.Id=i.TemplateVersionId WHERE
                i.TemplateHash<>LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),t.ContentJson COLLATE Latin1_General_100_BIN2_UTF8)),2)))
                THROW 52005,'Document template hash must match retained content.',1;
              IF EXISTS(SELECT 1 FROM inserted i JOIN Document d ON d.Id=i.DocumentId JOIN PolicyDocumentRequest r ON r.Id=i.PolicyDocumentRequestId
                WHERE i.PolicyVersionId<>r.VersionId OR i.TemplateVersionId<>r.TemplateVersionId OR i.WorkId<>r.WorkId OR i.CreatedBy<>r.CreatedBy OR
                  d.Kind<>CASE WHEN r.Kind='policy-statement' THEN 'statement-of-fact' ELSE r.Kind END)
                THROW 52006,'Document request association must preserve original work and source.',1;
              IF EXISTS(SELECT 1 FROM inserted i JOIN Document d ON d.Id=i.DocumentId JOIN CancellationConsequence c ON c.Id=i.CancellationConsequenceId
                WHERE i.PolicyVersionId<>c.VersionId OR d.Kind<>'cancellation-notice' OR c.Kind<>'notice')
                THROW 52007,'Cancellation document must preserve its notice consequence.',1;
              IF EXISTS(SELECT 1 FROM inserted i JOIN Document d ON d.Id=i.DocumentId JOIN OutboxWork w ON w.Id=i.WorkId WHERE
                (i.PolicyDocumentRequestId IS NOT NULL AND w.Kind<>'policy-document') OR
                (i.SourceKind='upload' AND (w.Kind<>'file-finalization' OR w.SubjectRecordId IS NULL OR w.SubjectRecordId<>d.SubjectId)) OR
                (i.SourceKind<>'upload' AND i.PolicyDocumentRequestId IS NULL AND (w.Kind<>'document-generation' OR w.SubjectRecordId IS NULL OR w.SubjectRecordId<>i.Id)))
                THROW 52008,'Document work association is invalid.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_DocumentVersionContent_Source ON DocumentVersionContent AFTER INSERT AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted i JOIN DocumentVersion v ON v.Id=i.VersionId JOIN Document d ON d.Id=v.DocumentId JOIN FileObject f ON f.Id=i.FileObjectId
                WHERE f.SubjectId<>d.SubjectId OR f.CreatedBy<>v.CreatedBy OR i.CreatedBy<>v.CreatedBy OR
                  f.FileName COLLATE Latin1_General_100_BIN2<>v.OriginalName COLLATE Latin1_General_100_BIN2 OR
                  (v.SourceKind='upload' AND (i.PageCount IS NOT NULL OR f.WorkId IS NULL OR f.WorkId<>v.WorkId)) OR
                  (v.SourceKind<>'upload' AND (i.PageCount IS NULL OR f.MediaType<>'application/pdf')))
                THROW 52009,'Document content must preserve its version and original file owner.',1;
            END;
            """);
    }

    internal static void BeforeDown(MigrationBuilder migration) => migration.Sql("IF EXISTS(SELECT 1 FROM Document) OR EXISTS(SELECT 1 FROM DocumentVersion) OR EXISTS(SELECT 1 FROM DocumentVersionContent) THROW 52010,'Retained document identities cannot be discarded by downgrade.',1;");
}
