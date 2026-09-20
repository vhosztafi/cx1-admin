using Microsoft.EntityFrameworkCore.Migrations;
namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CommercialServicingRating
{
    private static void AddCommercialRatingGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_ServicingReferral_Source ON ServicingReferral AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId JOIN ServicingDraft d ON d.Id=i.DraftId
                JOIN ServicingRatingResult r ON r.Id=i.RatingId WHERE i.State<>'open' OR i.LatestDecisionId IS NOT NULL OR d.State<>'draft'
                OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>c.Id OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
                OR c.State<>'rated' OR c.CurrentRatingId IS NULL OR c.CurrentRatingId<>i.RatingId OR r.Outcome<>'rated'
                OR i.CreatedAt<r.CompletedAt OR i.CreatedAt>=r.ExpiresAt)
              THROW 51340,'Referral requires the current rated draft revision and starts open.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId CROSS APPLY OPENJSON(i.RequiredAuthorityJson,'$.triggers') t
                WHERE COALESCE(JSON_VALUE(t.value,'$.source'),'') NOT IN ('binder','authority','source')
                OR COALESCE(JSON_VALUE(t.value,'$.requirement.ruleCode'),'')<>i.RuleCode OR COALESCE(JSON_VALUE(t.value,'$.requirement.dimension'),'')<>i.Dimension
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(t.value,'$.requirement.targetId')),'00000000-0000-0000-0000-000000000000')<>i.TargetKey
                OR (JSON_VALUE(t.value,'$.requirement.targetId') IS NOT NULL AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(t.value,'$.requirement.targetId')) IS NULL)
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                    WHERE TRY_CONVERT(datetimeoffset,JSON_VALUE(s.value,'$.effectiveAt'))=TRY_CONVERT(datetimeoffset,JSON_VALUE(t.value,'$.effectiveAt'))))
              THROW 51341,'Referral triggers must retain the owned rated dates, rule, dimension and target.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId WHERE i.RiskItemId IS NOT NULL AND NOT EXISTS(
                SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s CROSS APPLY OPENJSON(s.value,CASE WHEN JSON_VALUE(c.InputJson,'$.format')='commercial-servicing-rating-input-1' THEN '$.commercial.pricing.proposal.risk.locations' ELSE '$.input.drivers' END) WITH(Id uniqueidentifier '$.id') driver WHERE driver.Id=i.RiskItemId))
              THROW 51342,'Referral item must exist in the cumulative rated product risk.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId
                CROSS APPLY OPENJSON(i.RequiredAuthorityJson,'$.triggers') t
                WHERE JSON_VALUE(c.InputJson,'$.format')='commercial-servicing-rating-input-1' AND i.RiskItemId IS NOT NULL
                  AND NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                    CROSS APPLY OPENJSON(s.value,'$.commercial.pricing.proposal.risk.locations') WITH(Id uniqueidentifier '$.id') location
                    WHERE location.Id=i.RiskItemId AND TRY_CONVERT(datetimeoffset,JSON_VALUE(s.value,'$.effectiveAt'))=TRY_CONVERT(datetimeoffset,JSON_VALUE(t.value,'$.effectiveAt'))))
              THROW 51941,'Commercial referral target must belong to every retained trigger date.',1;
            END;
            """);

        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_ServicingCycle_Source ON ServicingCycle AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId
            JOIN PolicyVersion b ON b.Id=i.BaseVersionId
            JOIN Policy p ON p.Id=i.PolicyId JOIN PolicyTerm t ON t.Id=i.BaseTermId
            JOIN AgencyTermsVersion a ON a.Id=i.AgencyTermsVersionId JOIN OutboxWork w ON w.Id=i.WorkId
            WHERE d.State<>'draft' OR d.Kind NOT IN ('adjustment','renewal') OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
            OR i.State<>'rating-pending' OR a.AgencyId<>p.AgencyId
            OR (d.Kind='adjustment' AND t.ProductVersionId<>i.ProductVersionId)
            OR w.Kind<>'servicing-rating' OR w.SubjectRecordId IS NULL OR w.SubjectRecordId<>i.Id
            OR w.ScenarioVersionId IS NULL OR w.ScenarioVersionId<>i.ScenarioVersionId OR w.OperationKey<>'servicing-rating/'+LOWER(REPLACE(CONVERT(varchar(36),i.Id),'-',''))
            OR i.InputHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.InputJson COLLATE Latin1_General_100_BIN2_UTF8))
            OR COALESCE(JSON_VALUE(i.InputJson,'$.format'),'')<>CASE WHEN JSON_VALUE(b.SnapshotJson,'$.productCode')='commercial-combined' THEN 'commercial-servicing-rating-input-1' WHEN d.Kind='renewal' THEN 'servicing-rating-input-2' ELSE 'servicing-rating-input-1' END)
            THROW 51223,'Cycle requires active owned revision, exact input and durable work provenance.',1;
            END;
            """);
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_ServicingCycle_SettingInsert ON ServicingCycle AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN SettingVersion s ON s.Id=i.ServicingSettingVersionId
            JOIN PolicyVersion b ON b.Id=i.BaseVersionId JOIN ServicingRevision r ON r.Id=i.RevisionId
            WHERE i.ServicingSettingVersionId IS NULL OR s.Id IS NULL OR s.Scope<>CASE WHEN JSON_VALUE(b.SnapshotJson,'$.productCode')='commercial-combined' THEN 'commercial-servicing-rating' WHEN i.RenewalPreparationVersionId IS NULL THEN 'servicing-rating' ELSE 'renewal-preparation' END
            OR s.EffectiveFrom>i.CreatedAt
            OR COALESCE(JSON_VALUE(s.[Values],'$.kind'),'')<>s.Scope
            OR COALESCE(JSON_VALUE(s.[Values],'$.demo'),'')<>'true'
            OR COALESCE(JSON_VALUE(s.[Values],'$.schemaVersion'),'')<>'1'
            OR COALESCE(JSON_VALUE(s.[Values],'$.currency'),'')<>'GBP'
            OR (i.RenewalPreparationVersionId IS NULL AND COALESCE(JSON_VALUE(s.[Values],'$.earningBasis'),'')<>'london-calendar-days')
            OR TRY_CONVERT(decimal(19,2),CASE WHEN i.RenewalPreparationVersionId IS NULL THEN JSON_VALUE(s.[Values],'$.adjustmentFee') ELSE JSON_VALUE(s.[Values],'$.renewalFee') END) IS NULL
            OR TRY_CONVERT(decimal(19,2),CASE WHEN i.RenewalPreparationVersionId IS NULL THEN JSON_VALUE(s.[Values],'$.adjustmentFee') ELSE JSON_VALUE(s.[Values],'$.renewalFee') END) NOT BETWEEN 0 AND 9999999999999.99
            OR TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.fee')) IS NULL
            OR TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.fee'))<>TRY_CONVERT(decimal(19,2),CASE WHEN i.RenewalPreparationVersionId IS NULL THEN JSON_VALUE(s.[Values],'$.adjustmentFee') ELSE JSON_VALUE(s.[Values],'$.renewalFee') END)
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.servicingSettingVersionId')),'00000000-0000-0000-0000-000000000000')<>i.ServicingSettingVersionId
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.policyId')),'00000000-0000-0000-0000-000000000000')<>i.PolicyId
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.baseTermId')),'00000000-0000-0000-0000-000000000000')<>i.BaseTermId
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.requestedBy')),'00000000-0000-0000-0000-000000000000')<>i.RequestedBy
            OR TRY_CONVERT(datetimeoffset,JSON_VALUE(i.InputJson,'$.requestedAt')) IS NULL
            OR TRY_CONVERT(datetimeoffset,JSON_VALUE(i.InputJson,'$.requestedAt'))<>i.CreatedAt
            OR COALESCE(JSON_VALUE(i.InputJson,'$.baseContentHash'),'')<>LOWER(CONVERT(varchar(64),b.ContentHash,2))
            OR COALESCE(JSON_VALUE(i.InputJson,'$.revisionContentHash'),'')<>LOWER(CONVERT(varchar(64),r.ContentHash,2)))
            THROW 51240,'Rating requires its exact setting, fee, source hashes and request clock.',1;
            END;
            """);
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_ServicingCycle_CommercialSource ON ServicingCycle AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyVersion b ON b.Id=i.BaseVersionId
              JOIN ServicingDraft d ON d.Id=i.DraftId JOIN RatingRuleVersion r ON r.Id=i.RatingRuleVersionId
              WHERE JSON_VALUE(b.SnapshotJson,'$.productCode')='commercial-combined' AND
              (d.Kind<>'adjustment' OR i.RenewalPreparationVersionId IS NOT NULL
               OR COALESCE(JSON_VALUE(i.InputJson,'$.ratingDefinition.productCode'),'')<>'commercial-combined'
               OR COALESCE(JSON_VALUE(r.DefinitionJson,'$.productCode'),'')<>'commercial-combined'
               OR COALESCE(TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.fee')),-1)<>COALESCE(TRY_CONVERT(decimal(19,2),JSON_VALUE(r.DefinitionJson,'$.adjustmentFee')),-2)
               OR JSON_QUERY(i.InputJson,'$.slices') IS NULL OR (SELECT COUNT(*) FROM OPENJSON(i.InputJson,'$.slices')) NOT BETWEEN 1 AND 100
               OR EXISTS(SELECT 1 FROM OPENJSON(i.InputJson,'$.slices') s
                   WHERE JSON_QUERY(s.value,'$.input') IS NOT NULL OR JSON_QUERY(s.value,'$.commercial') IS NULL
                     OR COALESCE(JSON_VALUE(s.value,'$.commercial.pricing.proposal.productCode'),'')<>'commercial-combined')))
             THROW 51940,'Commercial servicing requires owned commercial adjustment inputs and its published adjustment fee.',1;
            END;
            """);
    }
    private static void RemoveCommercialRatingGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_ServicingReferral_Source ON ServicingReferral AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId JOIN ServicingDraft d ON d.Id=i.DraftId
                JOIN ServicingRatingResult r ON r.Id=i.RatingId WHERE i.State<>'open' OR i.LatestDecisionId IS NOT NULL OR d.State<>'draft'
                OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>c.Id OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
                OR c.State<>'rated' OR c.CurrentRatingId IS NULL OR c.CurrentRatingId<>i.RatingId OR r.Outcome<>'rated'
                OR i.CreatedAt<r.CompletedAt OR i.CreatedAt>=r.ExpiresAt)
              THROW 51340,'Referral requires the current rated draft revision and starts open.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId CROSS APPLY OPENJSON(i.RequiredAuthorityJson,'$.triggers') t
                WHERE COALESCE(JSON_VALUE(t.value,'$.source'),'') NOT IN ('binder','authority','source')
                OR COALESCE(JSON_VALUE(t.value,'$.requirement.ruleCode'),'')<>i.RuleCode OR COALESCE(JSON_VALUE(t.value,'$.requirement.dimension'),'')<>i.Dimension
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(t.value,'$.requirement.targetId')),'00000000-0000-0000-0000-000000000000')<>i.TargetKey
                OR (JSON_VALUE(t.value,'$.requirement.targetId') IS NOT NULL AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(t.value,'$.requirement.targetId')) IS NULL)
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                    WHERE TRY_CONVERT(datetimeoffset,JSON_VALUE(s.value,'$.effectiveAt'))=TRY_CONVERT(datetimeoffset,JSON_VALUE(t.value,'$.effectiveAt'))))
              THROW 51341,'Referral triggers must retain the owned rated dates, rule, dimension and target.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId WHERE i.RiskItemId IS NOT NULL AND NOT EXISTS(
                SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s CROSS APPLY OPENJSON(s.value,'$.input.drivers') WITH(Id uniqueidentifier '$.id') driver WHERE driver.Id=i.RiskItemId))
              THROW 51342,'Referral item must exist in the cumulative rated driver risk.',1;
            END;
            """);

        migration.Sql("DROP TRIGGER TR_ServicingCycle_CommercialSource;");
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_ServicingCycle_Source ON ServicingCycle AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId
            JOIN Policy p ON p.Id=i.PolicyId JOIN PolicyTerm t ON t.Id=i.BaseTermId
            JOIN AgencyTermsVersion a ON a.Id=i.AgencyTermsVersionId JOIN OutboxWork w ON w.Id=i.WorkId
            WHERE d.State<>'draft' OR d.Kind NOT IN ('adjustment','renewal') OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
            OR i.State<>'rating-pending' OR a.AgencyId<>p.AgencyId
            OR (d.Kind='adjustment' AND t.ProductVersionId<>i.ProductVersionId)
            OR w.Kind<>'servicing-rating' OR w.SubjectRecordId IS NULL OR w.SubjectRecordId<>i.Id
            OR w.ScenarioVersionId IS NULL OR w.ScenarioVersionId<>i.ScenarioVersionId OR w.OperationKey<>'servicing-rating/'+LOWER(REPLACE(CONVERT(varchar(36),i.Id),'-',''))
            OR i.InputHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.InputJson COLLATE Latin1_General_100_BIN2_UTF8))
            OR COALESCE(JSON_VALUE(i.InputJson,'$.format'),'')<>CASE WHEN d.Kind='renewal' THEN 'servicing-rating-input-2' ELSE 'servicing-rating-input-1' END)
            THROW 51223,'Cycle requires active owned revision, exact input and durable work provenance.',1;
            END;
            """);
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_ServicingCycle_SettingInsert ON ServicingCycle AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN SettingVersion s ON s.Id=i.ServicingSettingVersionId
            JOIN PolicyVersion b ON b.Id=i.BaseVersionId JOIN ServicingRevision r ON r.Id=i.RevisionId
            WHERE i.ServicingSettingVersionId IS NULL OR s.Id IS NULL OR s.Scope<>CASE WHEN i.RenewalPreparationVersionId IS NULL THEN 'servicing-rating' ELSE 'renewal-preparation' END
            OR s.EffectiveFrom>i.CreatedAt
            OR COALESCE(JSON_VALUE(s.[Values],'$.kind'),'')<>s.Scope
            OR COALESCE(JSON_VALUE(s.[Values],'$.demo'),'')<>'true'
            OR COALESCE(JSON_VALUE(s.[Values],'$.schemaVersion'),'')<>'1'
            OR COALESCE(JSON_VALUE(s.[Values],'$.currency'),'')<>'GBP'
            OR (i.RenewalPreparationVersionId IS NULL AND COALESCE(JSON_VALUE(s.[Values],'$.earningBasis'),'')<>'london-calendar-days')
            OR TRY_CONVERT(decimal(19,2),CASE WHEN i.RenewalPreparationVersionId IS NULL THEN JSON_VALUE(s.[Values],'$.adjustmentFee') ELSE JSON_VALUE(s.[Values],'$.renewalFee') END) IS NULL
            OR TRY_CONVERT(decimal(19,2),CASE WHEN i.RenewalPreparationVersionId IS NULL THEN JSON_VALUE(s.[Values],'$.adjustmentFee') ELSE JSON_VALUE(s.[Values],'$.renewalFee') END) NOT BETWEEN 0 AND 9999999999999.99
            OR TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.fee')) IS NULL
            OR TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.fee'))<>TRY_CONVERT(decimal(19,2),CASE WHEN i.RenewalPreparationVersionId IS NULL THEN JSON_VALUE(s.[Values],'$.adjustmentFee') ELSE JSON_VALUE(s.[Values],'$.renewalFee') END)
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.servicingSettingVersionId')),'00000000-0000-0000-0000-000000000000')<>i.ServicingSettingVersionId
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.policyId')),'00000000-0000-0000-0000-000000000000')<>i.PolicyId
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.baseTermId')),'00000000-0000-0000-0000-000000000000')<>i.BaseTermId
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.requestedBy')),'00000000-0000-0000-0000-000000000000')<>i.RequestedBy
            OR TRY_CONVERT(datetimeoffset,JSON_VALUE(i.InputJson,'$.requestedAt')) IS NULL
            OR TRY_CONVERT(datetimeoffset,JSON_VALUE(i.InputJson,'$.requestedAt'))<>i.CreatedAt
            OR COALESCE(JSON_VALUE(i.InputJson,'$.baseContentHash'),'')<>LOWER(CONVERT(varchar(64),b.ContentHash,2))
            OR COALESCE(JSON_VALUE(i.InputJson,'$.revisionContentHash'),'')<>LOWER(CONVERT(varchar(64),r.ContentHash,2)))
            THROW 51240,'Rating requires its exact setting, fee, source hashes and request clock.',1;
            END;
            """);
    }
}
