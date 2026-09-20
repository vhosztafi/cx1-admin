using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CommercialExposureStorage
{
    private static void AddExposureGuards(MigrationBuilder migration)
    {
        foreach (var table in new[] { "CommercialExposureBook", "CommercialExposureBinder", "CommercialExposureLimitVersion", "CommercialExposureVersion", "CommercialExposureLocation" })
            migration.Sql($"CREATE TRIGGER TR_{table}_AppendOnly ON [{table}] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51420, 'Exposure publications and history are immutable.', 1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_CommercialExposureBook_Source ON CommercialExposureBook AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN Product p ON p.Id=i.ProductId WHERE p.Code<>'commercial-combined')
              THROW 51421, 'Exposure books require Commercial Combined.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CommercialExposureBinder_Source ON CommercialExposureBinder AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CommercialExposureBook b ON b.Id=i.BookId JOIN BinderVersion v ON v.Id=i.BinderVersionId
              WHERE b.ProductId<>v.ProductId OR b.ProviderId<>v.ProviderId OR v.State<>'published')
              THROW 51421, 'Binder publication must share the stable product/provider book.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CommercialExposureLimitVersion_Source ON CommercialExposureLimitVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i WHERE i.ContentHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.PublicationJson COLLATE Latin1_General_100_BIN2_UTF8))
              OR COALESCE(JSON_VALUE(i.PublicationJson,'$.schemaVersion'),'')<>'commercial-exposure-limit-1'
              OR LEN(TRIM(COALESCE(JSON_VALUE(i.PublicationJson,'$.reason'),'')))=0)
              THROW 51422, 'Limit publication requires exact content hash and provenance.', 1;
            IF EXISTS(SELECT Id,BookId,District,Version,Amount,EffectiveFrom,EffectiveTo,PublishedAt,CreatedBy,SupersedesLimitId FROM inserted
              EXCEPT SELECT i.Id,j.bookId,j.district,j.version,j.amount,j.effectiveFrom,j.effectiveTo,j.publishedAt,j.publishedBy,j.supersedesLimitId
              FROM inserted i CROSS APPLY OPENJSON(i.PublicationJson) WITH(bookId uniqueidentifier,district nvarchar(100),version int,amount decimal(38,10),
                effectiveFrom datetimeoffset,effectiveTo datetimeoffset,publishedAt datetimeoffset,publishedBy uniqueidentifier,supersedesLimitId uniqueidentifier) j)
              THROW 51422, 'Limit fields must agree with the retained publication.', 1;
            IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN CommercialExposureLimitVersion p ON p.Id=i.SupersedesLimitId
              WHERE i.SupersedesLimitId IS NOT NULL AND (p.Id IS NULL OR p.BookId<>i.BookId OR p.District<>i.District OR p.Version>=i.Version OR p.PublishedAt>i.PublishedAt))
              THROW 51422, 'A replacement limit requires an earlier same-scope publication.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CommercialExposureVersion_Source ON CommercialExposureVersion AFTER INSERT AS BEGIN
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
        migration.Sql("""
            CREATE TRIGGER TR_CommercialExposureLocation_Source ON CommercialExposureLocation AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT ExposureVersionId,RiskItemId,District,SumInsured FROM inserted
              EXCEPT SELECT i.ExposureVersionId,j.riskItemId,j.district,j.sumInsured FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
                CROSS APPLY OPENJSON(v.LocationsJson) WITH(riskItemId uniqueidentifier,district nvarchar(100),sumInsured decimal(38,10)) j)
              THROW 51424, 'Exposure children require their exact immutable header.', 1;
            END;
            """);
    }

    private static void DropExposureGuards(MigrationBuilder migration)
    {
        foreach (var table in new[] { "CommercialExposureBook", "CommercialExposureBinder", "CommercialExposureLimitVersion", "CommercialExposureVersion", "CommercialExposureLocation" })
        {
            migration.Sql($"DROP TRIGGER IF EXISTS TR_{table}_Source;");
            migration.Sql($"DROP TRIGGER IF EXISTS TR_{table}_AppendOnly;");
        }
    }
}
