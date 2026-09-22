using Microsoft.EntityFrameworkCore.Migrations;
namespace BackOffice.Infrastructure.Persistence.Migrations;
public partial class OperationalMid
{
    private static void AddMidGuards(MigrationBuilder migration)
    {
        foreach(var table in new[]{"MidSubmission","MidResult"})migration.Sql($"CREATE TRIGGER TR_{table}_Retained ON {table} AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 52020,'MID history is immutable.',1; END;");
        migration.Sql("""
        CREATE TRIGGER TR_MidSubmission_Source ON MidSubmission AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted s JOIN PolicyVersion v ON v.Id=s.VersionId JOIN OutboxWork w ON w.Id=s.WorkId
            LEFT JOIN PolicyMidIntent m ON m.Id=s.PolicyMidIntentId LEFT JOIN CancellationConsequence c ON c.Id=s.CancellationConsequenceId
            LEFT JOIN PolicyVersion b ON b.Id=s.BaseVersionId
            WHERE JSON_VALUE(v.SnapshotJson,'$.productCode') NOT IN ('motor-trade-road-risks','motor-trade-combined') OR
              (s.PolicyMidIntentId IS NOT NULL AND (m.WorkId<>s.WorkId OR m.VersionId<>s.VersionId OR m.PolicyId<>s.PolicyId OR w.Kind<>'mid-update' OR w.SubjectRecordId<>m.Id OR w.Payload<>m.PayloadJson)) OR
              (s.CancellationConsequenceId IS NOT NULL AND (c.WorkId<>s.WorkId OR c.VersionId<>s.VersionId OR c.PolicyId<>s.PolicyId OR c.Kind<>'mid-removal' OR w.Kind<>'cancellation-mid-removal' OR w.SubjectRecordId<>c.Id OR w.Payload<>c.PayloadJson)) OR
              (s.BaseVersionId IS NOT NULL AND (b.Id IS NULL OR b.PolicyId<>s.PolicyId)) OR w.ScenarioVersionId IS NULL OR w.CreatedBy IS NULL OR w.CreatedBy<>s.CreatedBy OR
              COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(s.RequestJson,'$.versionId')),'00000000-0000-0000-0000-000000000000')<>s.VersionId OR
              COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(s.RequestJson,'$.policyId')),'00000000-0000-0000-0000-000000000000')<>s.PolicyId OR
              COALESCE(JSON_VALUE(s.RequestJson,'$.contentHash'),'')<>LOWER(CONVERT(varchar(64),v.ContentHash,2)) OR
              s.RequestHash<>LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),s.RequestJson COLLATE Latin1_General_100_BIN2_UTF8)),2)))
            THROW 52021,'MID submission must own an exact Motor Trade source and original operation.',1;
        END;
        """);
        migration.Sql("""
        CREATE TRIGGER TR_MidResult_Source ON MidResult AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted r JOIN MidSubmission s ON s.Id=r.SubmissionId JOIN OutboxWork w ON w.Id=s.WorkId JOIN DemoProviderOperation p ON p.Id=r.ProviderOperationId
            WHERE p.Kind<>w.Kind OR p.OperationKey<>w.OperationKey OR p.ScenarioVersionId<>s.ScenarioVersionId OR
              p.RequestHash<>CONVERT(varbinary(32),s.RequestHash,2) OR p.Result IS NULL OR CONVERT(varbinary(max),p.Result)<>CONVERT(varbinary(max),r.ResultJson) OR
              COALESCE(JSON_VALUE(r.ResultJson,'$.eventId'),'')<>r.ProviderEventId OR
              COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.ResultJson,'$.submissionId')),'00000000-0000-0000-0000-000000000000')<>s.Id OR
              COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.ResultJson,'$.policyVersionId')),'00000000-0000-0000-0000-000000000000')<>s.VersionId OR
              r.ContentHash<>LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),r.ResultJson COLLATE Latin1_General_100_BIN2_UTF8)),2)))
            THROW 52022,'MID result must match its exact retained provider event.',1;
        END;
        """);
        migration.Sql("""
        CREATE TRIGGER TR_MidWork_Retained ON OutboxWork AFTER UPDATE,DELETE AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted d JOIN MidSubmission s ON s.WorkId=d.Id LEFT JOIN inserted i ON i.Id=d.Id WHERE
            i.Id IS NULL OR i.Kind<>d.Kind OR i.SubjectRecordId<>d.SubjectRecordId OR i.SubjectRecordId IS NULL OR
            i.OperationKey<>d.OperationKey OR CONVERT(varbinary(max),i.Payload)<>CONVERT(varbinary(max),d.Payload) OR
            i.ScenarioVersionId<>d.ScenarioVersionId OR i.ScenarioVersionId IS NULL OR i.CreatedBy<>d.CreatedBy OR i.CreatedBy IS NULL OR i.CreatedAt<>d.CreatedAt)
          THROW 52023,'Registered MID work retains its original request and identity.',1;
        END;
        """);
    }
    private static void RemoveMidGuards(MigrationBuilder migration)
    {
        migration.Sql("IF EXISTS(SELECT 1 FROM MidSubmission) OR EXISTS(SELECT 1 FROM MidResult) OR EXISTS(SELECT 1 FROM PolicyMidIntent WHERE Purpose='new-business') THROW 52024,'Retained MID history prevents downgrade.',1;");
        foreach(var name in new[]{"TR_MidSubmission_Retained","TR_MidResult_Retained","TR_MidSubmission_Source","TR_MidResult_Source","TR_MidWork_Retained"})migration.Sql($"DROP TRIGGER {name};");
    }
}
