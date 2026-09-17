using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingRatingSettingPin
{
    private static void AddSettingPinGuards(MigrationBuilder migration)
    {
        // Nullable only for preservation of legacy prerequisite cycle records.
        // Every new cycle requires a concrete setting; no old input is rewritten.
        migration.Sql("""
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
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCycle_SettingUpdate ON ServicingCycle AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF UPDATE(ServicingSettingVersionId) THROW 51241,'Servicing rating setting provenance is immutable.',1;
            IF EXISTS(SELECT 1 FROM inserted WHERE State='rated' AND ServicingSettingVersionId IS NULL)
              THROW 51242,'A legacy cycle without servicing configuration cannot grant rating authority.',1;
            END;
            """);
    }
}
