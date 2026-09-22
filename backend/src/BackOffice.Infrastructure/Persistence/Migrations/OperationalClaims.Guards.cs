using Microsoft.EntityFrameworkCore.Migrations;
namespace BackOffice.Infrastructure.Persistence.Migrations;
public partial class OperationalClaims
{
    private static void AddClaimsGuards(MigrationBuilder migration)
    {
        foreach(var table in new[]{"ClaimsRequest","ClaimsSummary"})migration.Sql($"CREATE TRIGGER TR_{table}_Retained ON {table} AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 52000,'Claims history is immutable.',1; END;");
        migration.Sql("""
        CREATE TRIGGER TR_ClaimsHandoff_Identity ON ClaimsHandoff AFTER INSERT,UPDATE,DELETE AS
        BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL) THROW 52000,'Claims history is retained.',1;
          IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE i.IncidentId<>d.IncidentId OR i.PolicyId<>d.PolicyId OR i.RevisionId<>d.RevisionId OR i.ResolutionId<>d.ResolutionId OR i.SourceVersionId<>d.SourceVersionId OR i.AdministratorId<>d.AdministratorId OR i.RequestJson<>d.RequestJson OR i.RequestHash<>d.RequestHash OR i.CreatedBy<>d.CreatedBy OR i.CreatedAt<>d.CreatedAt) THROW 52000,'Claims submission is immutable.',1;
          IF EXISTS(SELECT 1 FROM inserted h JOIN Incident i ON i.Id=h.IncidentId JOIN IncidentRevision r ON r.Id=h.RevisionId JOIN IncidentOccurrenceResolution o ON o.Id=h.ResolutionId JOIN PolicyVersion v ON v.Id=h.SourceVersionId
            WHERE h.PolicyId<>i.PolicyId OR r.IncidentId<>i.Id OR o.IncidentId<>i.Id OR o.RevisionId<>r.Id OR o.State<>'resolved' OR v.PolicyId<>i.PolicyId OR
              NOT EXISTS(SELECT 1 FROM IncidentResolutionSource s WHERE s.ResolutionId=o.Id AND s.VersionId=v.Id) OR
              ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(h.RequestJson,'$.incidentId')),'00000000-0000-0000-0000-000000000000')<>i.Id OR
              ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(h.RequestJson,'$.revisionId')),'00000000-0000-0000-0000-000000000000')<>r.Id OR
              ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(h.RequestJson,'$.resolutionId')),'00000000-0000-0000-0000-000000000000')<>o.Id)
            THROW 52001,'Claims submission must pin its original incident source.',1;
        END;
        """);
        migration.Sql("""
        CREATE TRIGGER TR_ClaimsRequest_Source ON ClaimsRequest AFTER INSERT AS
        BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted r JOIN ClaimsHandoff h ON h.Id=r.HandoffId JOIN OutboxWork w ON w.Id=r.WorkId WHERE
            w.Kind<>'operational-claims' OR w.SubjectRecordId<>r.Id OR w.ScenarioVersionId<>r.ScenarioVersionId OR w.Payload<>r.PayloadJson OR
            ISNULL(JSON_VALUE(r.PayloadJson,'$.handoffHash'),'')<>h.RequestHash OR ISNULL(JSON_VALUE(r.PayloadJson,'$.purpose'),'')<>r.Purpose OR
            ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.PayloadJson,'$.handoffId')),'00000000-0000-0000-0000-000000000000')<>h.Id OR
            ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.PayloadJson,'$.requestId')),'00000000-0000-0000-0000-000000000000')<>r.Id)
            THROW 52001,'Claims request must match its original submission and work.',1;
        END;
        """);
        migration.Sql("""
        CREATE TRIGGER TR_ClaimsSummary_Source ON ClaimsSummary AFTER INSERT AS
        BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted s JOIN ClaimsRequest r ON r.Id=s.RequestId JOIN ClaimsHandoff h ON h.Id=s.HandoffId JOIN OutboxWork w ON w.Id=r.WorkId JOIN DemoProviderOperation p ON p.Id=s.ProviderOperationId WHERE
            r.HandoffId<>h.Id OR r.Purpose='contact' OR p.Kind<>w.Kind OR p.OperationKey<>w.OperationKey OR p.ScenarioVersionId<>r.ScenarioVersionId OR
            s.AsOf<h.CreatedAt OR ISNULL(JSON_VALUE(s.SummaryJson,'$.eventId'),'')<>s.ProviderEventId OR
            ISNULL(TRY_CONVERT(datetimeoffset,JSON_VALUE(s.SummaryJson,'$.asOf')),'19000101')<>s.AsOf OR
            ISNULL(JSON_VALUE(p.Result,'$.eventId'),'')<>s.ProviderEventId)
            THROW 52001,'Claims summary must retain provider provenance.',1;
        END;
        """);
        migration.Sql("""
        CREATE TRIGGER TR_Incident_ClaimsPin ON Incident AFTER UPDATE AS
        BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id JOIN ClaimsHandoff h ON h.RevisionId=d.CurrentRevisionId WHERE
            h.State<>'rejected' AND (i.CurrentRevisionId<>d.CurrentRevisionId OR ISNULL(i.CurrentResolutionId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentResolutionId,'00000000-0000-0000-0000-000000000000')))
            THROW 52000,'Unresolved or acknowledged claims submission remains pinned.',1;
        END;
        """);
        migration.Sql("""
        CREATE TRIGGER TR_IncidentRevision_ClaimsPin ON IncidentRevision AFTER INSERT AS
        BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted r JOIN Incident i ON i.Id=r.IncidentId JOIN ClaimsHandoff h ON h.RevisionId=i.CurrentRevisionId WHERE h.State<>'rejected')
            THROW 52000,'Claims uncertainty cannot be corrected into a new submission.',1;
        END;
        """);
    }
}
