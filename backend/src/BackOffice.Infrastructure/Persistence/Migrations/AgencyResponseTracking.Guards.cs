using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class AgencyResponseTracking
{
    private static void AddResponseGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_AgencyResponseRequest_Retained ON AgencyResponseRequest AFTER INSERT,UPDATE,DELETE AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL)
                THROW 52000,'Agency response history is retained.',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE
                i.MessageVersionId<>d.MessageVersionId OR i.SubjectId<>d.SubjectId OR i.RelationshipId<>d.RelationshipId OR
                i.Reference COLLATE Latin1_General_100_BIN2<>d.Reference COLLATE Latin1_General_100_BIN2 OR
                i.Subject COLLATE Latin1_General_100_BIN2<>d.Subject COLLATE Latin1_General_100_BIN2 OR
                i.Instruction COLLATE Latin1_General_100_BIN2<>d.Instruction COLLATE Latin1_General_100_BIN2 OR
                i.Reason COLLATE Latin1_General_100_BIN2<>d.Reason COLLATE Latin1_General_100_BIN2 OR
                DATALENGTH(i.Instruction)<>DATALENGTH(d.Instruction) OR DATALENGTH(i.Subject)<>DATALENGTH(d.Subject) OR
                DATALENGTH(i.Reason)<>DATALENGTH(d.Reason) OR DATALENGTH(i.Reference)<>DATALENGTH(d.Reference) OR
                i.CreatedAt<>d.CreatedAt OR i.CreatedBy<>d.CreatedBy OR d.State<>'awaiting-response' OR i.State='awaiting-response')
                THROW 52000,'Only an open agency response request can be closed; its public content is immutable.',1;
              IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND
                (i.State<>'awaiting-response' OR NOT EXISTS(
                  SELECT 1 FROM OperationalMessageVersion v JOIN OperationalMessageDraft m ON m.Id=v.MessageId
                  JOIN OperationalThread t ON t.Id=m.ThreadId
                  CROSS APPLY OPENJSON(v.ContentJson) WITH(subject nvarchar(300) '$.subject',body nvarchar(max) '$.body') c
                  WHERE v.Id=i.MessageVersionId AND t.Visibility='agency' AND t.SubjectId=i.SubjectId AND t.RelationshipId=i.RelationshipId
                    AND c.subject COLLATE Latin1_General_100_BIN2=i.Subject COLLATE Latin1_General_100_BIN2
                    AND c.body COLLATE Latin1_General_100_BIN2=i.Instruction COLLATE Latin1_General_100_BIN2
                    AND DATALENGTH(c.subject)=DATALENGTH(i.Subject) AND DATALENGTH(c.body)=DATALENGTH(i.Instruction)
                    AND EXISTS(SELECT 1 FROM OperationalDelivery od WHERE od.MessageVersionId=v.Id AND od.State='delivered'
                      AND od.SubjectId=i.SubjectId AND od.RelationshipId=i.RelationshipId))))
                THROW 52001,'Agency response requests require an exact delivered public message.',1;
              IF EXISTS(SELECT 1 FROM inserted WHERE DATALENGTH(Instruction)>16000)
                THROW 52001,'Agency response instructions exceed their storage limit.',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE i.ResolvedAt<i.CreatedAt OR
                (i.ResolvedBy IS NOT NULL AND NOT EXISTS(SELECT 1 FROM [User] u WHERE u.Id=i.ResolvedBy)))
                THROW 52001,'Agency response closure requires a retained actor and valid chronology.',1;
            END;
            """);
    }
}
