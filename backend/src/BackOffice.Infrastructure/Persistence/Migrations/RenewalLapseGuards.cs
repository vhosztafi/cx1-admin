using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class RenewalLapseLifecycle
{
    private static void AddLapseGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_RenewalLapse_WorkInput ON OutboxWork AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.Kind='renewal-lapse-notification' AND
                (i.Kind<>d.Kind OR i.OperationKey<>d.OperationKey OR i.SubjectRecordId<>d.SubjectRecordId OR i.SubjectRecordId IS NULL
                OR i.ScenarioVersionId<>d.ScenarioVersionId OR i.ScenarioVersionId IS NULL OR i.CreatedAt<>d.CreatedAt
                OR ISNULL(CONVERT(varchar(36),i.CreatedBy),'')<>ISNULL(CONVERT(varchar(36),d.CreatedBy),'')
                OR CONVERT(varbinary(max),i.Payload)<>CONVERT(varbinary(max),d.Payload)))
                THROW 51766,'Renewal notification inputs are immutable.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_RenewalLapse_ReceiptSource ON RenewalLapseNotificationReceipt AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN RenewalLapseEvent l ON l.Id=i.LapseEventId JOIN OutboxWork w ON w.Id=i.WorkId
                WHERE i.CreatedAt<l.CreatedAt OR w.Kind<>'renewal-lapse-notification' OR w.State<>'leased'
                OR i.PayloadHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),w.Payload COLLATE Latin1_General_100_BIN2_UTF8))
                OR (i.Outcome='demo-delivered' AND NOT EXISTS(SELECT 1 FROM OPENJSON(l.RecipientSnapshotJson)))
                OR (i.Outcome='demo-no-recipient' AND EXISTS(SELECT 1 FROM OPENJSON(l.RecipientSnapshotJson))))
                THROW 51767,'Renewal notification receipt requires the exact leased payload and recipient outcome.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_RenewalLapseEvent_Immutable ON RenewalLapseEvent AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; THROW 51760,'Renewal lapse history is immutable.',1; END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_RenewalLapseNotificationReceipt_Immutable ON RenewalLapseNotificationReceipt AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; THROW 51761,'Renewal notification receipts are immutable.',1; END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_RenewalLapseEvent_Source ON RenewalLapseEvent AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyTerm t WITH(UPDLOCK,HOLDLOCK) ON t.Id=i.TermId
                JOIN SettingVersion s ON s.Id=i.RuleSettingVersionId JOIN OutboxWork w ON w.Id=i.WorkId
                WHERE i.PolicyId<>t.PolicyId OR i.EffectiveAt<>t.EndsAt OR s.Scope<>'renewal-preparation' OR s.EffectiveFrom>i.CreatedAt
                OR ISNULL(JSON_VALUE(s.[Values],'$.demo'),'')<>'true' OR ISNULL(TRY_CONVERT(int,JSON_VALUE(s.[Values],'$.lapseDaysAfterExpiry')),-1) NOT BETWEEN 0 AND 365
                OR i.AutoLapseAt<>SWITCHOFFSET(DATEADD(day,TRY_CONVERT(int,JSON_VALUE(s.[Values],'$.lapseDaysAfterExpiry')),
                    CONVERT(datetime2,t.EndsAt AT TIME ZONE 'GMT Standard Time')) AT TIME ZONE 'GMT Standard Time','+00:00')
                OR w.Kind<>'renewal-lapse-notification' OR w.SubjectRecordId<>i.Id OR w.ScenarioVersionId<>i.RuleSettingVersionId
                OR w.SubjectRecordId IS NULL OR w.ScenarioVersionId IS NULL
                OR w.OperationKey<>'renewal-lapse/'+LOWER(REPLACE(CONVERT(varchar(36),i.TermId),'-',''))
                OR ISNULL(JSON_VALUE(w.Payload,'$.format'),'')<>'renewal-lapse-notification-1' OR ISNULL(JSON_VALUE(w.Payload,'$.demo'),'')<>'true'
                OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.eventId')) IS NULL
                OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.eventId'))<>i.Id
                OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.termId')) IS NULL
                OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.termId'))<>i.TermId
                OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.policyId')) IS NULL
                OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.policyId'))<>i.PolicyId
                OR ISNULL(JSON_VALUE(w.Payload,'$.reason'),'')<>i.Reason
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(w.Payload,'$.effectiveAt')) IS NULL
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(w.Payload,'$.effectiveAt'))<>i.EffectiveAt
                OR JSON_QUERY(w.Payload,'$.recipients') IS NULL
                OR CONVERT(varbinary(max),JSON_QUERY(w.Payload,'$.recipients'))<>CONVERT(varbinary(max),i.RecipientSnapshotJson)
                OR EXISTS(SELECT 1 FROM PolicyTerm next WITH(UPDLOCK,HOLDLOCK) WHERE next.PolicyId=i.PolicyId AND next.Id<>t.Id AND next.StartsAt=t.EndsAt)
                OR EXISTS(SELECT 1 FROM ServicingDraft d WITH(UPDLOCK,HOLDLOCK) JOIN ServicingCycle c ON c.Id=d.CurrentCycleId
                    WHERE d.BaseTermId=t.Id AND d.Kind='renewal' AND d.State='draft' AND c.State='rated' AND c.CurrentAcceptanceId IS NOT NULL)
                OR (SELECT TOP(1) tx.Kind FROM PolicyVersion v JOIN PolicyTransaction tx ON tx.Id=v.TransactionId
                    WHERE v.TermId=t.Id AND v.EffectiveAt<t.EndsAt AND v.ProcessedAt<=i.CreatedAt
                    ORDER BY v.EffectiveAt DESC,tx.Sequence DESC,v.SliceOrdinal DESC)='cancellation')
                THROW 51762,'Lapse requires the expiring term, exact configured deadline and durable notification, without an accepted or issued renewal.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_RenewalLapse_Acceptance ON ServicingAcceptance AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId
                JOIN RenewalLapseEvent l WITH(UPDLOCK,HOLDLOCK) ON l.TermId=d.BaseTermId WHERE d.Kind='renewal')
                THROW 51763,'A lapsed renewal cannot be accepted.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_RenewalLapse_IssueDecision ON ServicingIssueDecision AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId
                JOIN RenewalLapseEvent l WITH(UPDLOCK,HOLDLOCK) ON l.TermId=i.BaseTermId WHERE d.Kind='renewal')
                THROW 51764,'A lapsed renewal cannot be issued.',1;
            END;
            """);
    }
}
