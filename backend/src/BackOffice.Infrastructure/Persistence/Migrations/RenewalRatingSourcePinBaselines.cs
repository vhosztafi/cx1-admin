namespace BackOffice.Infrastructure.Persistence.Migrations;

// Exact prior trigger definitions retained here so additive upgrade and downgrade
// both preserve the existing adjustment contract without editing old migrations.
public partial class RenewalRatingSourcePins
{
    private const string PreviousCycleSource = """
CREATE TRIGGER TR_ServicingCycle_Source ON ServicingCycle AFTER INSERT AS BEGIN
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
OR COALESCE(JSON_VALUE(i.InputJson,'$.format'),'')<>'servicing-rating-input-1')
THROW 51223,'Cycle requires active owned revision, exact input and durable work provenance.',1;
END;
""";
    private const string PreviousCycleSetting = """
CREATE TRIGGER TR_ServicingCycle_SettingInsert ON ServicingCycle AFTER INSERT AS BEGIN
SET NOCOUNT ON;
IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN SettingVersion s ON s.Id=i.ServicingSettingVersionId
JOIN PolicyVersion b ON b.Id=i.BaseVersionId JOIN ServicingRevision r ON r.Id=i.RevisionId
WHERE i.ServicingSettingVersionId IS NULL OR s.Id IS NULL OR s.Scope<>'servicing-rating'
OR s.EffectiveFrom>i.CreatedAt
OR COALESCE(JSON_VALUE(s.[Values],'$.kind'),'')<>'servicing-rating'
OR COALESCE(JSON_VALUE(s.[Values],'$.demo'),'')<>'true'
OR COALESCE(JSON_VALUE(s.[Values],'$.schemaVersion'),'')<>'1'
OR COALESCE(JSON_VALUE(s.[Values],'$.currency'),'')<>'GBP'
OR COALESCE(JSON_VALUE(s.[Values],'$.earningBasis'),'')<>'london-calendar-days'
OR TRY_CONVERT(decimal(19,2),JSON_VALUE(s.[Values],'$.adjustmentFee')) IS NULL
OR TRY_CONVERT(decimal(19,2),JSON_VALUE(s.[Values],'$.adjustmentFee')) NOT BETWEEN 0 AND 9999999999999.99
OR TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.fee')) IS NULL
OR TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.fee'))<>TRY_CONVERT(decimal(19,2),JSON_VALUE(s.[Values],'$.adjustmentFee'))
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
""";
}
