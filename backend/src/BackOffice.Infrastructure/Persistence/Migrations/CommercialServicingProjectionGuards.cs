using Microsoft.EntityFrameworkCore.Migrations;
namespace BackOffice.Infrastructure.Persistence.Migrations;
public partial class CommercialServicingAtomicIssue
{
    private static void SetCommercialProjectionGuard(MigrationBuilder migration, bool servicing)
    {
        if (servicing)
        {
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_CommercialExposureVersion_Source ON CommercialExposureVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT i.Id,i.SourceHash,i.TermStartsAt,i.TermEndsAt,i.EffectiveAt,i.ProcessedAt,i.TransactionKind,i.TransactionSequence,i.SliceOrdinal FROM inserted i
              EXCEPT SELECT i.Id,v.ContentHash,t.StartsAt,t.EndsAt,v.EffectiveAt,v.ProcessedAt,tr.Kind,tr.Sequence,v.SliceOrdinal FROM inserted i
                JOIN PolicyVersion v ON v.Id=i.VersionId JOIN PolicyTerm t ON t.Id=v.TermId JOIN PolicyTransaction tr ON tr.Id=v.TransactionId)
              THROW 51423, 'Exposure metadata must match its immutable issued source.', 1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyVersion v ON v.Id=i.VersionId JOIN Policy p ON p.Id=i.PolicyId
              JOIN CommercialExposureBook b ON b.Id=i.BookId JOIN PolicyTransaction tr ON tr.Id=i.TransactionId
              LEFT JOIN UnderwritingCycle uc ON uc.Id=tr.CycleId LEFT JOIN ServicingCycle sc ON sc.Id=tr.ServicingCycleId
              LEFT JOIN CancellationIssueDecision cd ON cd.Id=tr.CancellationIssueDecisionId
              LEFT JOIN CommercialExposureVersion baseExposure ON baseExposure.VersionId=cd.BaseVersionId
              WHERE b.ProductId<>p.ProductId OR COALESCE(JSON_VALUE(v.SnapshotJson,'$.productCode'),'')<>'commercial-combined'
                OR (tr.Kind<>'cancellation' AND (COALESCE(JSON_VALUE(v.SnapshotJson,'$.snapshotFormat'),'')<>CASE WHEN tr.Kind='adjustment' THEN 'issued-commercial-servicing-1' ELSE 'issued-commercial-1' END
                  OR COALESCE(uc.BinderVersionId,sc.BinderVersionId,'00000000-0000-0000-0000-000000000000')<>i.BinderVersionId))
                OR (tr.Kind='cancellation' AND (baseExposure.Id IS NULL OR baseExposure.BookId<>i.BookId OR baseExposure.BinderVersionId<>i.BinderVersionId)))
              THROW 51423, 'Exposure requires its exact commercial product and source binder.', 1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyVersion v ON v.Id=i.VersionId
              WHERE (SELECT COUNT(*) FROM OPENJSON(i.LocationsJson))>100
                OR (i.TransactionKind<>'cancellation' AND (ISJSON(JSON_QUERY(v.SnapshotJson,'$.risk.locations'),ARRAY)<>1
                  OR JSON_QUERY(v.SnapshotJson,'$.risk.locations') IS NULL
                  OR (SELECT COUNT(*) FROM OPENJSON(i.LocationsJson))<>(SELECT COUNT(*) FROM OPENJSON(v.SnapshotJson,'$.risk.locations')))))
              THROW 51424, 'Exposure must retain every issued location.', 1;
            IF EXISTS(SELECT i.Id,j.riskItemId,j.district,j.sumInsured FROM inserted i CROSS APPLY OPENJSON(i.LocationsJson)
                WITH(riskItemId uniqueidentifier,district nvarchar(100),sumInsured decimal(38,10)) j WHERE i.TransactionKind<>'cancellation'
              EXCEPT SELECT i.Id,j.id,LEFT(UPPER(REPLACE(j.postcode,' ','')),LEN(REPLACE(j.postcode,' ',''))-3),j.buildings+j.contents+j.stock
                FROM inserted i JOIN PolicyVersion v ON v.Id=i.VersionId CROSS APPLY OPENJSON(v.SnapshotJson,'$.risk.locations')
                WITH(id uniqueidentifier,postcode nvarchar(100) '$.address.postcode',buildings decimal(38,10),contents decimal(38,10),stock decimal(38,10)) j
                WHERE i.TransactionKind<>'cancellation')
              THROW 51424, 'Exposure property values and districts must match the complete issued source.', 1;
            INSERT CommercialExposureLocation(ExposureVersionId,RiskItemId,District,SumInsured)
              SELECT i.Id,j.riskItemId,j.district,j.sumInsured FROM inserted i CROSS APPLY OPENJSON(i.LocationsJson)
                WITH(riskItemId uniqueidentifier,district nvarchar(100),sumInsured decimal(38,10)) j;
            END;
            """);
        }
        else
        {
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_CommercialExposureVersion_Source ON CommercialExposureVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT i.Id,i.SourceHash,i.TermStartsAt,i.TermEndsAt,i.EffectiveAt,i.ProcessedAt,i.TransactionKind,i.TransactionSequence,i.SliceOrdinal FROM inserted i
              EXCEPT SELECT i.Id,v.ContentHash,t.StartsAt,t.EndsAt,v.EffectiveAt,v.ProcessedAt,tr.Kind,tr.Sequence,v.SliceOrdinal FROM inserted i
                JOIN PolicyVersion v ON v.Id=i.VersionId JOIN PolicyTerm t ON t.Id=v.TermId JOIN PolicyTransaction tr ON tr.Id=v.TransactionId)
              THROW 51423, 'Exposure metadata must match its immutable issued source.', 1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyVersion v ON v.Id=i.VersionId JOIN Policy p ON p.Id=i.PolicyId
              JOIN CommercialExposureBook b ON b.Id=i.BookId JOIN PolicyTransaction tr ON tr.Id=i.TransactionId
              LEFT JOIN UnderwritingCycle uc ON uc.Id=tr.CycleId LEFT JOIN ServicingCycle sc ON sc.Id=tr.ServicingCycleId
              LEFT JOIN CancellationIssueDecision cd ON cd.Id=tr.CancellationIssueDecisionId
              LEFT JOIN CommercialExposureVersion baseExposure ON baseExposure.VersionId=cd.BaseVersionId
              WHERE b.ProductId<>p.ProductId OR COALESCE(JSON_VALUE(v.SnapshotJson,'$.productCode'),'')<>'commercial-combined'
                OR (tr.Kind<>'cancellation' AND (COALESCE(JSON_VALUE(v.SnapshotJson,'$.snapshotFormat'),'')<>'issued-commercial-1'
                  OR COALESCE(uc.BinderVersionId,sc.BinderVersionId,'00000000-0000-0000-0000-000000000000')<>i.BinderVersionId))
                OR (tr.Kind='cancellation' AND (baseExposure.Id IS NULL OR baseExposure.BookId<>i.BookId OR baseExposure.BinderVersionId<>i.BinderVersionId)))
              THROW 51423, 'Exposure requires its exact commercial product and source binder.', 1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyVersion v ON v.Id=i.VersionId
              WHERE (SELECT COUNT(*) FROM OPENJSON(i.LocationsJson))>100
                OR (i.TransactionKind<>'cancellation' AND (ISJSON(JSON_QUERY(v.SnapshotJson,'$.risk.locations'),ARRAY)<>1
                  OR JSON_QUERY(v.SnapshotJson,'$.risk.locations') IS NULL
                  OR (SELECT COUNT(*) FROM OPENJSON(i.LocationsJson))<>(SELECT COUNT(*) FROM OPENJSON(v.SnapshotJson,'$.risk.locations')))))
              THROW 51424, 'Exposure must retain every issued location.', 1;
            IF EXISTS(SELECT i.Id,j.riskItemId,j.district,j.sumInsured FROM inserted i CROSS APPLY OPENJSON(i.LocationsJson)
                WITH(riskItemId uniqueidentifier,district nvarchar(100),sumInsured decimal(38,10)) j WHERE i.TransactionKind<>'cancellation'
              EXCEPT SELECT i.Id,j.id,LEFT(UPPER(REPLACE(j.postcode,' ','')),LEN(REPLACE(j.postcode,' ',''))-3),j.buildings+j.contents+j.stock
                FROM inserted i JOIN PolicyVersion v ON v.Id=i.VersionId CROSS APPLY OPENJSON(v.SnapshotJson,'$.risk.locations')
                WITH(id uniqueidentifier,postcode nvarchar(100) '$.address.postcode',buildings decimal(38,10),contents decimal(38,10),stock decimal(38,10)) j
                WHERE i.TransactionKind<>'cancellation')
              THROW 51424, 'Exposure property values and districts must match the complete issued source.', 1;
            INSERT CommercialExposureLocation(ExposureVersionId,RiskItemId,District,SumInsured)
              SELECT i.Id,j.riskItemId,j.district,j.sumInsured FROM inserted i CROSS APPLY OPENJSON(i.LocationsJson)
                WITH(riskItemId uniqueidentifier,district nvarchar(100),sumInsured decimal(38,10)) j;
            END;
            """);
        }
    }
}
