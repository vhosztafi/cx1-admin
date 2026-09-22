using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class OperationalCommunication
{
    private static void AddCommunicationGuards(MigrationBuilder migration)
    {
        foreach(var table in new[]{"InternalNote","OperationalThread"})
            migration.Sql($"CREATE TRIGGER TR_{table}_Retained ON {table} AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 52000,'Communication history is immutable.',1; END;");
        foreach(var table in new[]{"InternalNote","OperationalMessageDraft"})
            migration.Sql($"CREATE TRIGGER TR_{table}_BodyBounded ON {table} AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted WHERE DATALENGTH(Body)>16000) THROW 52001,'Communication text exceeds its storage limit.',1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_OperationalThread_OwnedAudience ON OperationalThread AFTER INSERT AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalSubject s ON s.Id=i.SubjectId
                WHERE i.Visibility='agency' AND NOT EXISTS(SELECT 1 FROM ClientAgencyRelationship r WHERE r.Id=i.RelationshipId AND
                  ((s.Kind='agency' AND r.AgencyId=s.AgencyId) OR (s.Kind='relationship' AND r.Id=s.RelationshipId) OR
                   (s.Kind='quote' AND EXISTS(SELECT 1 FROM Quote q WHERE q.Id=s.QuoteId AND q.RelationshipId=r.Id)) OR
                   (s.Kind='policy' AND EXISTS(SELECT 1 FROM Policy p WHERE p.Id=s.PolicyId AND p.RelationshipId=r.Id)) OR
                   (s.Kind='servicing-draft' AND EXISTS(SELECT 1 FROM ServicingDraft d JOIN Policy p ON p.Id=d.PolicyId WHERE d.Id=s.ServicingDraftId AND p.RelationshipId=r.Id)))))
                THROW 52001,'Thread audience must belong to its original subject.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_OperationalMessageDraft_Retained ON OperationalMessageDraft AFTER UPDATE,DELETE AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL)
                THROW 52000,'Message history is retained.',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE i.ThreadId<>d.ThreadId OR i.CreatedAt<>d.CreatedAt OR
                i.CreatedBy<>d.CreatedBy OR i.AuthorLabel<>d.AuthorLabel OR (d.State<>'draft' AND (i.Body<>d.Body OR i.State='draft')))
                THROW 52000,'Message identity and queued content are immutable.',1;
            END;
            """);
        foreach(var table in new[]{"MessageDraftRecipient","MessageDraftAttachment"})
            migration.Sql($"""
                CREATE TRIGGER TR_{table}_DraftOnly ON {table} AFTER INSERT,UPDATE,DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalMessageDraft m ON m.Id=i.MessageId WHERE m.State<>'draft') OR
                     EXISTS(SELECT 1 FROM deleted d JOIN OperationalMessageDraft m ON m.Id=d.MessageId WHERE m.State<>'draft')
                    THROW 52000,'Queued message selections are immutable.',1;
                END;
                """);
        migration.Sql("""
            CREATE TRIGGER TR_MessageDraftRecipient_Audience ON MessageDraftRecipient AFTER INSERT,UPDATE AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalMessageDraft m ON m.Id=i.MessageId JOIN OperationalThread t ON t.Id=m.ThreadId
                JOIN Contact c ON c.Id=i.ContactId WHERE t.Visibility<>'agency' OR t.RelationshipId<>c.RelationshipId OR c.EndedAt IS NOT NULL)
                THROW 52001,'Message recipient must belong to the current thread audience.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_MessageDraftAttachment_Audience ON MessageDraftAttachment AFTER INSERT,UPDATE AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalMessageDraft m ON m.Id=i.MessageId JOIN OperationalThread t ON t.Id=m.ThreadId
                JOIN DocumentVersion v ON v.Id=i.DocumentVersionId JOIN Document d ON d.Id=v.DocumentId
                WHERE d.SubjectId<>t.SubjectId OR (t.Visibility='agency' AND (d.Visibility<>'agency' OR d.RelationshipId IS NULL OR d.RelationshipId<>t.RelationshipId)))
                THROW 52001,'Message attachment must retain original subject and audience.',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM DocumentVersionContent c JOIN FileObject f ON f.Id=c.FileObjectId WHERE c.VersionId=i.DocumentVersionId AND f.State='ready'))
                THROW 52001,'Message attachment must be ready.',1;
            END;
            """);
    }
}
