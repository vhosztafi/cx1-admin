using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class OperationalDelivery
{
    private static void AddDeliveryGuards(MigrationBuilder migration)
    {
        foreach(var table in new[]{"OperationalMessageVersion","OperationalDeliveryRecipient","OperationalDeliveryAttachment"})
            migration.Sql($"CREATE TRIGGER TR_{table}_Retained ON {table} AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 52000,'Delivery snapshots are immutable.',1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_OperationalDelivery_Retained ON OperationalDelivery AFTER UPDATE,DELETE AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL)
                THROW 52000,'Delivery history is retained.',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE i.SubjectId<>d.SubjectId OR i.RelationshipId<>d.RelationshipId OR
                ISNULL(i.MessageVersionId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.MessageVersionId,'00000000-0000-0000-0000-000000000000') OR
                ISNULL(i.ResendOfId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.ResendOfId,'00000000-0000-0000-0000-000000000000') OR
                i.WorkId<>d.WorkId OR i.ScenarioVersionId<>d.ScenarioVersionId OR CONVERT(varbinary(max),i.ContentJson)<>CONVERT(varbinary(max),d.ContentJson) OR i.ContentHash<>d.ContentHash OR
                i.CreatedBy<>d.CreatedBy OR i.CreatedAt<>d.CreatedAt)
                THROW 52000,'Delivery identity and frozen contents are immutable.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_OperationalMessageVersion_Source ON OperationalMessageVersion AFTER INSERT AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalMessageDraft m ON m.Id=i.MessageId JOIN OperationalThread t ON t.Id=m.ThreadId
                WHERE t.Visibility<>'agency' OR m.State<>'draft' OR
                  ISNULL((SELECT body FROM OPENJSON(i.ContentJson) WITH(body nvarchar(max))),'')<>m.Body OR ISNULL(JSON_VALUE(i.ContentJson,'$.subject'),'')<>t.Subject OR
                  ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContentJson,'$.subjectId')),'00000000-0000-0000-0000-000000000000')<>t.SubjectId OR
                  ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContentJson,'$.relationshipId')),'00000000-0000-0000-0000-000000000000')<>t.RelationshipId)
                THROW 52001,'Message version must freeze its original agency draft.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_OperationalDelivery_Source ON OperationalDelivery AFTER INSERT AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalSubject s ON s.Id=i.SubjectId
                WHERE NOT EXISTS(SELECT 1 FROM ClientAgencyRelationship r WHERE r.Id=i.RelationshipId AND r.State='active' AND
                  ((s.Kind='agency' AND r.AgencyId=s.AgencyId) OR (s.Kind='relationship' AND r.Id=s.RelationshipId) OR
                   (s.Kind='quote' AND EXISTS(SELECT 1 FROM Quote q WHERE q.Id=s.QuoteId AND q.RelationshipId=r.Id)) OR
                   (s.Kind='policy' AND EXISTS(SELECT 1 FROM Policy p WHERE p.Id=s.PolicyId AND p.RelationshipId=r.Id)) OR
                   (s.Kind='servicing-draft' AND EXISTS(SELECT 1 FROM ServicingDraft d JOIN Policy p ON p.Id=d.PolicyId WHERE d.Id=s.ServicingDraftId AND p.RelationshipId=r.Id)))))
                THROW 52001,'Delivery audience must belong to its original subject.',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE
                ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContentJson,'$.subjectId')),'00000000-0000-0000-0000-000000000000')<>i.SubjectId OR
                ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContentJson,'$.relationshipId')),'00000000-0000-0000-0000-000000000000')<>i.RelationshipId OR
                (i.MessageVersionId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM OperationalMessageVersion v WHERE v.Id=i.MessageVersionId AND CONVERT(varbinary(max),v.ContentJson)=CONVERT(varbinary(max),i.ContentJson) AND v.ContentHash=i.ContentHash)) OR
                (i.ResendOfId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM OperationalDelivery d WHERE d.Id=i.ResendOfId AND d.SubjectId=i.SubjectId AND d.RelationshipId=i.RelationshipId AND CONVERT(varbinary(max),d.ContentJson)=CONVERT(varbinary(max),i.ContentJson) AND d.ContentHash=i.ContentHash)))
                THROW 52001,'Delivery must retain its selected original snapshot.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_OperationalDeliveryRecipient_Source ON OperationalDeliveryRecipient AFTER INSERT AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalDelivery d ON d.Id=i.DeliveryId JOIN Contact c ON c.Id=i.ContactId
                WHERE c.RelationshipId<>d.RelationshipId OR c.EndedAt IS NOT NULL OR c.Email IS NULL OR c.Email<>i.Email OR c.DeclaredFullName<>i.Name OR
                NOT EXISTS(SELECT 1 FROM OPENJSON(d.ContentJson,'$.recipients') WITH(contactId uniqueidentifier, name nvarchar(300), email nvarchar(254)) r
                  WHERE r.contactId=i.ContactId AND r.name=i.Name AND r.email=i.Email))
                THROW 52001,'Delivery recipient must match its frozen owned contact.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_OperationalDeliveryAttachment_Source ON OperationalDeliveryAttachment AFTER INSERT AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalDelivery d ON d.Id=i.DeliveryId JOIN DocumentVersion v ON v.Id=i.DocumentVersionId
                JOIN Document doc ON doc.Id=v.DocumentId JOIN FileObject f ON f.Id=i.FileObjectId
                WHERE doc.SubjectId<>d.SubjectId OR doc.Visibility<>'agency' OR doc.RelationshipId IS NULL OR doc.RelationshipId<>d.RelationshipId OR
                  f.State<>'ready' OR f.Sha256<>i.ContentHash COLLATE Latin1_General_100_BIN2 OR f.ByteLength<>i.Length OR f.MediaType<>i.MediaType OR v.OriginalName<>i.OriginalName OR
                  NOT EXISTS(SELECT 1 FROM DocumentVersionContent c WHERE c.VersionId=i.DocumentVersionId AND c.FileObjectId=i.FileObjectId) OR
                  NOT EXISTS(SELECT 1 FROM OPENJSON(d.ContentJson,'$.attachments') WITH(versionId uniqueidentifier,fileId uniqueidentifier,hash nvarchar(64),name nvarchar(255),mediaType nvarchar(100),length bigint) a
                    WHERE a.versionId=i.DocumentVersionId AND a.fileId=i.FileObjectId AND a.hash=i.ContentHash AND a.name=i.OriginalName AND a.mediaType=i.MediaType AND a.length=i.Length))
                THROW 52001,'Delivery attachment must retain exact ready owned content.',1;
            END;
            """);
    }
}
