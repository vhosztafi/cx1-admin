using Microsoft.EntityFrameworkCore.Migrations;
namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CancellationOperations
{
    private static readonly string[] RetainedTables = ["CancellationOperationalReceipt", "CancellationNoticeDispatch", "CertificateWithdrawal", "CancellationTaskClosure"];
    private static void AddCancellationGuards(MigrationBuilder migration)
    {
        foreach (var table in RetainedTables)
            migration.Sql($"CREATE TRIGGER TR_{table}_Retained ON {table} AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 52030,'Cancellation operational history is immutable.',1; END;");
        migration.Sql("""
        CREATE TRIGGER TR_CancellationNoticeReceipt_Dispatch ON CancellationNoticeReceipt AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted receipt JOIN CancellationNoticeDispatch dispatch ON dispatch.ConsequenceId=receipt.ConsequenceId)
            THROW 52036,'An adapter-backed cancellation notice cannot receive a legacy delivery receipt.',1;
        END;
        """);
        migration.Sql("""
        CREATE TRIGGER TR_CancellationOperationalReceipt_Source ON CancellationOperationalReceipt AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted r JOIN CancellationConsequence c ON c.Id=r.ConsequenceId JOIN CancellationIssueDecision d ON d.Id=c.DecisionId
            WHERE r.WorkId<>c.WorkId OR r.EffectiveAt<>d.EffectiveAt OR r.CreatedBy<>c.CreatedBy OR
              r.PayloadHash<>LOWER(CONVERT(varchar(64),c.PayloadHash,2)) OR c.Kind='mid-removal' OR
              (c.Kind IN ('task-close','certificate-withdrawal') AND r.CreatedAt<d.EffectiveAt))
            THROW 52031,'Cancellation receipt must own an effective original consequence.',1;
        END;
        """);
        migration.Sql("""
        CREATE TRIGGER TR_CertificateWithdrawal_Source ON CertificateWithdrawal AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted r JOIN CancellationConsequence c ON c.Id=r.ConsequenceId JOIN CancellationIssueDecision d ON d.Id=c.DecisionId
            WHERE c.Kind<>'certificate-withdrawal' OR c.TermId<>r.TermId OR r.EffectiveAt<>d.EffectiveAt OR r.CreatedAt<d.EffectiveAt OR
              r.CertificateKind<>'policy-certificate' OR r.CreatedBy IS NULL OR r.CreatedBy<>c.CreatedBy)
            THROW 52032,'Certificate withdrawal must be effective and retain its term.',1;
        END;
        """);
        migration.Sql("""
        CREATE TRIGGER TR_CancellationNoticeDispatch_Source ON CancellationNoticeDispatch AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted r JOIN CancellationConsequence c ON c.Id=r.ConsequenceId JOIN DocumentVersion v ON v.Id=r.DocumentVersionId
            JOIN Document doc ON doc.Id=v.DocumentId JOIN OperationalSubject s ON s.Id=doc.SubjectId JOIN OperationalDelivery delivery ON delivery.Id=r.DeliveryId
            WHERE c.Kind<>'notice' OR v.CancellationConsequenceId IS NULL OR v.CancellationConsequenceId<>c.Id OR v.PolicyVersionId<>c.VersionId OR
              doc.Kind<>'cancellation-notice' OR s.PolicyId IS NULL OR s.PolicyId<>c.PolicyId OR delivery.SubjectId<>s.Id OR
              r.CreatedBy IS NULL OR r.CreatedBy<>c.CreatedBy OR delivery.CreatedBy<>c.CreatedBy OR r.PayloadHash<>LOWER(CONVERT(varchar(64),c.PayloadHash,2)) OR
              EXISTS(SELECT 1 FROM CancellationNoticeReceipt legacy WHERE legacy.ConsequenceId=c.Id))
            THROW 52033,'Cancellation dispatch must own its original notice and cannot resend legacy evidence.',1;
        END;
        """);
        ReplaceAttachmentGuard(migration,true);
        migration.Sql("""
        CREATE TRIGGER TR_CancellationTaskClosure_Source ON CancellationTaskClosure AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted r JOIN CancellationConsequence c ON c.Id=r.ConsequenceId JOIN CancellationIssueDecision d ON d.Id=c.DecisionId
            JOIN OperationalTask t ON t.Id=r.TaskId JOIN OperationalTaskEvent e ON e.Id=r.TaskEventId
            WHERE c.Kind<>'task-close' OR r.CreatedAt<d.EffectiveAt OR r.CreatedBy IS NULL OR r.CreatedBy<>c.CreatedBy OR
              t.TypeCode<>'renewal' OR t.State<>'cancelled' OR t.SourceChanged=1 OR e.TaskId<>t.Id OR e.Kind<>'task.cancelled-by-policy' OR
              NOT EXISTS(SELECT 1 FROM WorkflowTaskBinding b JOIN OperationalSubject s ON s.Id=b.SubjectId WHERE b.TaskId=t.Id AND b.SourceKind='policy-term' AND b.PolicyTermId=c.TermId AND s.PolicyId=c.PolicyId))
            THROW 52034,'Cancellation may close only its bound renewal tasks at the effective instant.',1;
        END;
        """);
    }
    private static void RemoveCancellationGuards(MigrationBuilder migration)
    {
        foreach (var table in RetainedTables) migration.Sql($"IF EXISTS(SELECT 1 FROM {table}) THROW 52035,'Retained cancellation operations prevent downgrade.',1;");
        ReplaceAttachmentGuard(migration,false);
        migration.Sql("DROP TRIGGER TR_CancellationNoticeReceipt_Dispatch;");
        foreach (var table in RetainedTables) migration.Sql($"DROP TRIGGER TR_{table}_Retained;");
        foreach (var table in RetainedTables) migration.Sql($"DROP TRIGGER TR_{table}_Source;");
    }
    private static void ReplaceAttachmentGuard(MigrationBuilder migration,bool cancellation)
    {
        var visibility="(doc.Visibility<>'agency' OR doc.RelationshipId IS NULL OR doc.RelationshipId<>d.RelationshipId)";
        if(cancellation)visibility+=" AND NOT EXISTS(SELECT 1 FROM CancellationNoticeDispatch dispatch WHERE dispatch.DeliveryId=d.Id AND dispatch.DocumentVersionId=v.Id)";
        migration.Sql($"""
        ALTER TRIGGER TR_OperationalDeliveryAttachment_Source ON OperationalDeliveryAttachment AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalDelivery d ON d.Id=i.DeliveryId JOIN DocumentVersion v ON v.Id=i.DocumentVersionId
            JOIN Document doc ON doc.Id=v.DocumentId JOIN FileObject f ON f.Id=i.FileObjectId
            WHERE doc.SubjectId<>d.SubjectId OR ({visibility}) OR
              f.State<>'ready' OR f.Sha256<>i.ContentHash COLLATE Latin1_General_100_BIN2 OR f.ByteLength<>i.Length OR f.MediaType<>i.MediaType OR v.OriginalName<>i.OriginalName OR
              NOT EXISTS(SELECT 1 FROM DocumentVersionContent c WHERE c.VersionId=i.DocumentVersionId AND c.FileObjectId=i.FileObjectId) OR
              NOT EXISTS(SELECT 1 FROM OPENJSON(d.ContentJson,'$.attachments') WITH(versionId uniqueidentifier,fileId uniqueidentifier,hash nvarchar(64),name nvarchar(255),mediaType nvarchar(100),length bigint) a
                WHERE a.versionId=i.DocumentVersionId AND a.fileId=i.FileObjectId AND a.hash=i.ContentHash AND a.name=i.OriginalName AND a.mediaType=i.MediaType AND a.length=i.Length))
            THROW 52001,'Delivery attachment must retain exact ready owned content.',1;
        END;
        """);
    }
}
