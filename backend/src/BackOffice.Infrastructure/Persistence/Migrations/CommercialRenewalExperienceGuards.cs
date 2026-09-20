using Microsoft.EntityFrameworkCore.Migrations;
namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CommercialRenewalPreparation
{
    private static void AddCommercialExperienceGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_RenewalExperienceVersion_Commercial ON RenewalExperienceVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId
              JOIN Policy p ON p.Id=d.PolicyId JOIN Product product ON product.Id=p.ProductId
              WHERE (product.Code='commercial-combined' AND (i.CommercialRevisionId IS NULL OR i.CommercialSubjectsJson IS NULL
                OR d.CurrentRevisionId IS NULL OR i.CommercialRevisionId<>d.CurrentRevisionId
                OR COALESCE(JSON_VALUE(i.CommercialSubjectsJson,'$.format'),'')<>'commercial-renewal-subjects-1'
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.CommercialSubjectsJson,'$.baseVersionId')),'00000000-0000-0000-0000-000000000000')<>d.BaseVersionId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.CommercialSubjectsJson,'$.revisionId')),'00000000-0000-0000-0000-000000000000')<>i.CommercialRevisionId
                OR LEN(COALESCE(JSON_VALUE(i.CommercialSubjectsJson,'$.inputHash'),''))<>64
                OR JSON_VALUE(i.CommercialSubjectsJson,'$.inputHash') COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9a-f]%'
                OR (SELECT COUNT(*) FROM OPENJSON(i.CommercialSubjectsJson))<>8
                OR (SELECT COUNT(DISTINCT [key]) FROM OPENJSON(i.CommercialSubjectsJson))<>8
                OR EXISTS(SELECT 1 FROM OPENJSON(i.CommercialSubjectsJson) j WHERE j.[key] NOT IN ('format','baseVersionId','revisionId','inputHash','propertyLocationIds','wageCategoryIds','lossRecordIds','liabilitySections'))))
                OR (product.Code<>'commercial-combined' AND (i.CommercialRevisionId IS NOT NULL OR i.CommercialSubjectsJson IS NOT NULL)))
              THROW 51950,'Commercial renewal experience requires its owned current revision and closed subject manifest.',1;
            IF EXISTS(SELECT 1 FROM inserted i CROSS APPLY (VALUES('propertyLocationIds'),('wageCategoryIds'),('lossRecordIds'),('liabilitySections')) a(Name)
              WHERE i.CommercialRevisionId IS NOT NULL AND (COALESCE(ISJSON(JSON_QUERY(i.CommercialSubjectsJson,'$.'+a.Name),ARRAY),0)<>1
                OR (SELECT COUNT(*) FROM OPENJSON(i.CommercialSubjectsJson,'$.'+a.Name))>CASE WHEN a.Name='liabilitySections' THEN 3 ELSE 500 END
                OR (SELECT COUNT(*) FROM OPENJSON(i.CommercialSubjectsJson,'$.'+a.Name))<>(SELECT COUNT(DISTINCT [value]) FROM OPENJSON(i.CommercialSubjectsJson,'$.'+a.Name))))
              THROW 51950,'Commercial renewal subject collections must be bounded distinct arrays.',1;
            IF EXISTS(SELECT 1 FROM inserted i CROSS APPLY OPENJSON(i.CommercialSubjectsJson,'$.liabilitySections') j
              WHERE j.[type]<>1 OR j.[value] NOT IN ('employers-liability','public-liability','products-liability'))
              THROW 51950,'Commercial renewal liability subjects must be product-specific.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId JOIN PolicyVersion b ON b.Id=d.BaseVersionId
              JOIN ServicingRevision r ON r.Id=i.CommercialRevisionId AND r.DraftId=i.DraftId
              CROSS APPLY (VALUES('propertyLocationIds','locations','commercial-location'),('wageCategoryIds','wages','commercial-wage'),('lossRecordIds','losses','commercial-loss')) a(Name,Source,Kind)
              CROSS APPLY OPENJSON(i.CommercialSubjectsJson,'$.'+a.Name) j
              WHERE j.[type]<>1 OR TRY_CONVERT(uniqueidentifier,j.[value]) IS NULL OR TRY_CONVERT(uniqueidentifier,j.[value])='00000000-0000-0000-0000-000000000000'
                OR (NOT EXISTS(SELECT 1 FROM OPENJSON(b.SnapshotJson,'$.risk.'+a.Source) s WHERE TRY_CONVERT(uniqueidentifier,JSON_VALUE(s.value,'$.id'))=TRY_CONVERT(uniqueidentifier,j.[value]))
                  AND NOT EXISTS(SELECT 1 FROM OPENJSON(r.ProposalJson,'$.changes') c WHERE JSON_VALUE(c.value,'$.kind')=a.Kind AND JSON_VALUE(c.value,'$.operation')='add'
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(c.value,'$.riskItemId'))=TRY_CONVERT(uniqueidentifier,j.[value])))
                OR EXISTS(SELECT 1 FROM OPENJSON(r.ProposalJson,'$.changes') c WHERE JSON_VALUE(c.value,'$.kind')=a.Kind AND JSON_VALUE(c.value,'$.operation')='remove'
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(c.value,'$.riskItemId'))=TRY_CONVERT(uniqueidentifier,j.[value])))
              THROW 51950,'Commercial experience cannot borrow foreign or removed risk subjects.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_RenewalExperienceReview_Commercial ON RenewalExperienceReview AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN RenewalExperienceVersion e ON e.Id=i.ExperienceVersionId JOIN ServicingDraft d ON d.Id=i.DraftId
              WHERE e.CommercialRevisionId IS NOT NULL AND (d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>e.CommercialRevisionId))
              THROW 51951,'Commercial experience review cannot approve an obsolete risk revision.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCycle_CommercialRenewalExperience ON ServicingCycle AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN RenewalExperienceVersion e ON e.Id=i.RenewalExperienceVersionId
              JOIN Product p ON p.Id=i.ProductId WHERE
              (p.Code='commercial-combined' AND (e.CommercialRevisionId IS NULL OR e.CommercialRevisionId<>i.RevisionId OR e.CommercialSubjectsJson IS NULL
                OR COALESCE(JSON_QUERY(i.InputJson,'$.renewal.experience.commercialSubjects'),'') COLLATE Latin1_General_100_BIN2<>e.CommercialSubjectsJson COLLATE Latin1_General_100_BIN2))
              OR (p.Code<>'commercial-combined' AND (e.CommercialRevisionId IS NOT NULL OR JSON_QUERY(i.InputJson,'$.renewal.experience.commercialSubjects') IS NOT NULL)))
              THROW 51952,'Renewal rating must retain the exact owned reviewed commercial experience context.',1;
            END;
            """);
    }
}
