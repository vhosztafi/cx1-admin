using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class OperationalIncidents
{
    private static void AddIncidentGuards(MigrationBuilder migration)
    {
        foreach(var table in new[]{"IncidentRevision","IncidentOccurrenceResolution","IncidentResolutionSource","IncidentEvidence"})
            migration.Sql($"CREATE TRIGGER TR_{table}_Retained ON {table} AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 52000,'Incident history is immutable.',1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_Incident_Identity ON Incident AFTER INSERT,UPDATE,DELETE AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL)
                THROW 52000,'Incident history is retained.',1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE i.PolicyId<>d.PolicyId OR i.ProductCode<>d.ProductCode OR i.Reference<>d.Reference OR i.CreatedAt<>d.CreatedAt OR i.CreatedBy<>d.CreatedBy)
                THROW 52000,'Incident original identity is immutable.',1;
              IF EXISTS(SELECT 1 FROM inserted i JOIN Policy p ON p.Id=i.PolicyId JOIN Product product ON product.Id=p.ProductId WHERE product.Code<>i.ProductCode)
                THROW 52001,'Incident product must match its original policy.',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE i.CurrentRevisionId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM IncidentRevision r WHERE r.Id=i.CurrentRevisionId AND r.IncidentId=i.Id))
                THROW 52001,'Incident revision must belong to its original record.',1;
              IF EXISTS(SELECT 1 FROM inserted i WHERE i.CurrentResolutionId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM IncidentOccurrenceResolution r WHERE r.Id=i.CurrentResolutionId AND r.IncidentId=i.Id AND r.RevisionId=i.CurrentRevisionId))
                THROW 52001,'Incident resolution must belong to its current revision.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_IncidentRevision_Source ON IncidentRevision AFTER INSERT AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted r JOIN Incident i ON i.Id=r.IncidentId WHERE i.State IN ('queued','handed-off') OR
                ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.DraftJson,'$.policyId')),'00000000-0000-0000-0000-000000000000')<>i.PolicyId OR
                ISNULL(JSON_VALUE(r.DraftJson,'$.productCode'),'')<>i.ProductCode)
                THROW 52001,'Incident revision must retain original product and policy.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_IncidentOccurrenceResolution_Source ON IncidentOccurrenceResolution AFTER INSERT AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted r JOIN Incident i ON i.Id=r.IncidentId WHERE
                NOT EXISTS(SELECT 1 FROM IncidentRevision v WHERE v.Id=r.RevisionId AND v.IncidentId=i.Id) OR
                ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.ResolutionJson,'$.policyId')),'00000000-0000-0000-0000-000000000000')<>i.PolicyId OR
                ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.ResolutionJson,'$.revisionId')),'00000000-0000-0000-0000-000000000000')<>r.RevisionId OR
                ISNULL(JSON_VALUE(r.ResolutionJson,'$.state'),'')<>r.State)
                THROW 52001,'Occurrence resolution must retain its original revision.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_IncidentResolutionSource_Ownership ON IncidentResolutionSource AFTER INSERT AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted s JOIN IncidentOccurrenceResolution r ON r.Id=s.ResolutionId JOIN Incident i ON i.Id=r.IncidentId JOIN PolicyVersion v ON v.Id=s.VersionId
                WHERE v.PolicyId<>i.PolicyId OR LOWER(CONVERT(varchar(64),v.ContentHash,2))<>s.SourceHash OR v.ProcessedAt>r.KnownAt OR
                NOT EXISTS(SELECT 1 FROM OPENJSON(r.ResolutionJson,'$.candidates') WITH(versionId uniqueidentifier,sourceHash nvarchar(64),[from] datetimeoffset,[to] datetimeoffset) c
                  WHERE c.versionId=s.VersionId AND c.sourceHash=s.SourceHash AND c.[from]=s.[From] AND c.[to]=s.[To]))
                THROW 52001,'Occurrence sources must retain owned historical policy versions.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_IncidentEvidence_Ownership ON IncidentEvidence AFTER INSERT AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM inserted e JOIN IncidentRevision r ON r.Id=e.RevisionId JOIN Incident i ON i.Id=r.IncidentId
                JOIN DocumentVersion v ON v.Id=e.DocumentVersionId JOIN Document d ON d.Id=v.DocumentId JOIN OperationalSubject s ON s.Id=d.SubjectId
                WHERE s.Kind<>'policy' OR s.PolicyId IS NULL OR s.PolicyId<>i.PolicyId OR
                  NOT EXISTS(SELECT 1 FROM DocumentVersionContent c JOIN FileObject f ON f.Id=c.FileObjectId WHERE c.VersionId=v.Id AND f.State='ready'))
                THROW 52001,'Incident evidence must be ready original policy evidence.',1;
            END;
            """);
    }
}
