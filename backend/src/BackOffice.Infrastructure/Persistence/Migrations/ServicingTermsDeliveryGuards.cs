using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static class ServicingTermsDeliveryGuards
{
    internal static void Up(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingTermsDelivery_Source ON ServicingTermsDelivery AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId JOIN ServicingDraft d ON d.Id=i.DraftId
              JOIN ServicingRatingResult r ON r.Id=i.RatingId JOIN ServicingTermsVersion t ON t.Id=i.TermsVersionId
              JOIN OutboxWork w ON w.Id=i.WorkId JOIN SettingVersion s ON s.Id=i.ScenarioVersionId
              WHERE i.State<>'queued' OR c.State<>'rated' OR d.State<>'draft' OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>i.CycleId
                OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId OR c.CurrentTermsVersionId IS NULL OR c.CurrentTermsVersionId<>i.TermsVersionId
                OR c.CurrentRatingId IS NULL OR c.CurrentRatingId<>i.RatingId OR i.CreatedAt<t.PreparedAt OR i.CreatedAt>=r.ExpiresAt
                OR w.Kind<>'servicing-delivery' OR w.SubjectRecordId IS NULL OR w.SubjectRecordId<>i.Id OR w.ScenarioVersionId<>i.ScenarioVersionId
                OR w.OperationKey<>'servicing-delivery/'+LOWER(REPLACE(CONVERT(varchar(36),i.Id),'-','')) OR w.State<>'pending'
                OR w.CreatedBy IS NULL OR w.CreatedBy<>i.SentBy OR s.Scope<>'servicing-delivery' OR s.EffectiveFrom>i.CreatedAt
                OR COALESCE(JSON_VALUE(i.PayloadJson,'$.format'),'')<>'servicing-delivery-1'
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.deliveryId')),'00000000-0000-0000-0000-000000000000')<>i.Id
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.draftId')),'00000000-0000-0000-0000-000000000000')<>i.DraftId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.cycleId')),'00000000-0000-0000-0000-000000000000')<>i.CycleId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.termsVersionId')),'00000000-0000-0000-0000-000000000000')<>i.TermsVersionId
                OR COALESCE(JSON_VALUE(i.PayloadJson,'$.termsHash'),'') COLLATE Latin1_General_100_BIN2<>t.TermsHash
                OR COALESCE(JSON_QUERY(i.PayloadJson,'$.document'),'') COLLATE Latin1_General_100_BIN2<>t.TermsJson COLLATE Latin1_General_100_BIN2
                OR COALESCE(JSON_QUERY(i.PayloadJson,'$.recipients'),'') COLLATE Latin1_General_100_BIN2<>i.RecipientSnapshotJson COLLATE Latin1_General_100_BIN2
                OR (SELECT COUNT(*) FROM OPENJSON(i.RecipientSnapshotJson)) NOT BETWEEN 1 AND 20
                OR (SELECT COUNT(DISTINCT TRY_CONVERT(uniqueidentifier,JSON_VALUE(x.value,'$.id'))) FROM OPENJSON(i.RecipientSnapshotJson) x)<>(SELECT COUNT(*) FROM OPENJSON(i.RecipientSnapshotJson))
                OR EXISTS(SELECT 1 FROM OPENJSON(i.RecipientSnapshotJson) x WHERE
                  COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(x.value,'$.id')),'00000000-0000-0000-0000-000000000000')='00000000-0000-0000-0000-000000000000'
                  OR LEN(TRIM(COALESCE(JSON_VALUE(x.value,'$.name'),'')))=0 OR LEN(TRIM(COALESCE(JSON_VALUE(x.value,'$.email'),'')))=0))
              THROW 51510,'Delivery requires exact current terms, recipients and a dedicated owned outbox request.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingTermsDelivery_History ON ServicingTermsDelivery AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT Id,DraftId,CycleId,RevisionId,RatingId,TermsVersionId,WorkId,ScenarioVersionId,RecipientSnapshotJson,PayloadJson,PayloadHash,AssuranceHashAtSend,SentBy,CreatedAt,CreatedBy FROM deleted
              EXCEPT SELECT Id,DraftId,CycleId,RevisionId,RatingId,TermsVersionId,WorkId,ScenarioVersionId,RecipientSnapshotJson,PayloadJson,PayloadHash,AssuranceHashAtSend,SentBy,CreatedAt,CreatedBy FROM inserted)
              THROW 51511,'Delivery request provenance is immutable.',1;
            IF EXISTS(SELECT 1 FROM deleted d WHERE d.State<>'queued')
              THROW 51512,'Completed delivery outcomes are immutable.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN OutboxWork w ON w.Id=i.WorkId LEFT JOIN DemoProviderOperation p ON p.Id=i.ProviderOperationId
              WHERE i.State='delivered' AND (p.Id IS NULL OR p.Kind<>w.Kind OR p.OperationKey<>w.OperationKey OR p.ScenarioVersionId<>i.ScenarioVersionId
                OR LOWER(CONVERT(varchar(64),p.RequestHash,2))<>i.PayloadHash OR p.State<>'succeeded'
                OR COALESCE(JSON_VALUE(p.Result,'$.state'),'')<>'delivered' OR p.CompletedAt IS NULL OR p.CompletedAt>i.CompletedAt))
              THROW 51513,'Delivered terms require the retained successful matching provider operation.',1;
            END;
            """);
    }

    internal static void Down(MigrationBuilder migration)
    {
        migration.Sql("DROP TRIGGER IF EXISTS TR_ServicingTermsDelivery_Source;");
        migration.Sql("DROP TRIGGER IF EXISTS TR_ServicingTermsDelivery_History;");
    }
}
