using Microsoft.EntityFrameworkCore.Migrations;
namespace BackOffice.Infrastructure.Persistence.Migrations;
public partial class CommercialServicingAtomicIssue
{
    private static void AddCommercialAtomicGuards(MigrationBuilder migration)
    {
        SetCommercialProjectionGuard(migration, true);
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_PolicyVersion_Servicing ON PolicyVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted v JOIN PolicyTransaction t ON t.Id=v.TransactionId
              JOIN ServicingIssueDecision d ON d.Id=t.ServicingIssueDecisionId JOIN ServicingCycle c ON c.Id=d.CycleId JOIN Product product ON product.Id=c.ProductId
              WHERE COALESCE(JSON_VALUE(v.SnapshotJson,'$.snapshotFormat'),'')<>CASE WHEN product.Code='commercial-combined' THEN 'issued-commercial-servicing-1' ELSE 'issued-servicing-1' END
                OR COALESCE(JSON_VALUE(v.SnapshotJson,'$.productCode'),'')<>product.Code
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.servicingIssueDecisionId')),'00000000-0000-0000-0000-000000000000')<>d.Id
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.sourceQuoteId')),'00000000-0000-0000-0000-000000000000')<>t.SourceQuoteId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.baseVersionId')),'00000000-0000-0000-0000-000000000000')<>d.BaseVersionId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.revisionId')),'00000000-0000-0000-0000-000000000000')<>d.RevisionId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.transactionId')),'00000000-0000-0000-0000-000000000000')<>t.Id
                OR COALESCE(TRY_CONVERT(int,JSON_VALUE(v.SnapshotJson,'$.provenance.sliceOrdinal')),0)<>v.SliceOrdinal
                OR COALESCE(JSON_VALUE(v.SnapshotJson,'$.provenance.inputHash'),'')<>LOWER(CONVERT(varchar(64),d.InputHash,2))
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.effectiveAt')) IS NULL
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.effectiveAt'))<>v.EffectiveAt
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.processedAt')) IS NULL
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.processedAt'))<>v.ProcessedAt
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                  WHERE CONVERT(int,s.[key])+1=v.SliceOrdinal AND TRY_CONVERT(datetimeoffset,JSON_VALUE(s.value,'$.effectiveAt'))=v.EffectiveAt))
              THROW 51526,'Each servicing version must retain its exact approved slice and immutable decision provenance.',1;
            END;
            """);
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_ServicingDraft_Issued ON ServicingDraft AFTER INSERT,UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.State='issued' AND
              (i.State<>d.State OR i.IssuedTransactionId<>d.IssuedTransactionId OR i.CurrentRevisionId<>d.CurrentRevisionId
                OR ISNULL(i.CurrentCycleId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentCycleId,'00000000-0000-0000-0000-000000000000')))
              THROW 51525,'Issued drafts cannot be reopened or revised.',1;
            IF EXISTS(SELECT 1 FROM inserted d WHERE d.State='issued' AND d.Kind<>'cancellation' AND NOT EXISTS(
              SELECT 1 FROM PolicyTransaction t JOIN ServicingIssueDecision s ON s.Id=t.ServicingIssueDecisionId
                JOIN IssueFinancialObligation o ON o.TransactionId=t.Id JOIN Journal j ON j.ObligationId=o.Id AND j.PostedAt IS NOT NULL
                JOIN ServicingCycle c ON c.Id=s.CycleId
              WHERE t.Id=d.IssuedTransactionId AND t.ServicingDraftId=d.Id AND t.ServicingRevisionId=d.CurrentRevisionId AND t.ServicingCycleId=d.CurrentCycleId
                AND (SELECT COUNT(*) FROM PolicyVersion v WHERE v.TransactionId=t.Id)=(SELECT COUNT(*) FROM OPENJSON(c.InputJson,'$.slices'))
                AND NOT EXISTS(SELECT 1 FROM PolicyVersion v WHERE v.TransactionId=t.Id AND
                  ((JSON_VALUE(c.InputJson,'$.format')<>'commercial-servicing-rating-input-1' AND
                    ((SELECT COUNT(DISTINCT doc.Kind) FROM PolicyDocumentRequest doc WHERE doc.VersionId=v.Id AND doc.Purpose=t.Kind)<>3
                      OR NOT EXISTS(SELECT 1 FROM PolicyMidIntent m WHERE m.VersionId=v.Id AND m.Purpose=t.Kind)))
                  OR (JSON_VALUE(c.InputJson,'$.format')='commercial-servicing-rating-input-1' AND
                    (COALESCE(APPLOCK_MODE('public','CoverMGA.CommercialExposure','Transaction'),'')<>'Exclusive'
                      OR NOT EXISTS(SELECT 1 FROM PolicyDocumentRequest doc WHERE doc.VersionId=v.Id AND doc.Purpose=t.Kind AND doc.Kind='policy-schedule')
                      OR NOT EXISTS(SELECT 1 FROM PolicyDocumentRequest doc WHERE doc.VersionId=v.Id AND doc.Purpose=t.Kind AND doc.Kind='policy-statement')
                      OR (SELECT COUNT(*) FROM PolicyDocumentRequest doc WHERE doc.VersionId=v.Id AND doc.Purpose=t.Kind)<>
                        2+CASE WHEN EXISTS(SELECT 1 FROM OPENJSON(v.SnapshotJson,'$.cover.sections') WITH(code nvarchar(100)) section WHERE section.code='employers-liability') THEN 1 ELSE 0 END
                      OR (SELECT COUNT(*) FROM PolicyDocumentRequest doc WHERE doc.VersionId=v.Id AND doc.Purpose=t.Kind AND doc.Kind='policy-certificate')<>
                        CASE WHEN EXISTS(SELECT 1 FROM OPENJSON(v.SnapshotJson,'$.cover.sections') WITH(code nvarchar(100)) section WHERE section.code='employers-liability') THEN 1 ELSE 0 END
                      OR EXISTS(SELECT 1 FROM PolicyMidIntent m WHERE m.VersionId=v.Id)
                      OR NOT EXISTS(SELECT 1 FROM CommercialExposureVersion e JOIN CommercialExposureIssueDecision decision ON decision.ExposureVersionId=e.Id
                        WHERE e.VersionId=v.Id AND JSON_VALUE(decision.DecisionJson,'$.format')='commercial-servicing-exposure-decision-1')))))
                AND NOT EXISTS(SELECT 1 FROM ServicingLease l WHERE l.DraftId=d.Id AND l.Active=1)))
              THROW 51525,'Issued draft requires its complete atomic policy, posting, documents and MID intent graph.',1;
            END;
            """);
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_PolicyMidIntent_Commercial ON PolicyMidIntent AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN Policy p ON p.Id=i.PolicyId JOIN Product product ON product.Id=p.ProductId WHERE product.Code='commercial-combined')
             THROW 51944,'Commercial policies have no Motor Insurance Database intent.',1;
            END;
            """);
        AddCommercialServicingExposureGuards(migration);
    }
    private static void RemoveCommercialAtomicGuards(MigrationBuilder migration)
    {
        SetCommercialProjectionGuard(migration, false);
        RemoveCommercialServicingExposureGuards(migration);
        migration.Sql("DROP TRIGGER TR_PolicyMidIntent_Commercial;");
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_PolicyVersion_Servicing ON PolicyVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted v JOIN PolicyTransaction t ON t.Id=v.TransactionId
              JOIN ServicingIssueDecision d ON d.Id=t.ServicingIssueDecisionId JOIN ServicingCycle c ON c.Id=d.CycleId
              WHERE COALESCE(JSON_VALUE(v.SnapshotJson,'$.snapshotFormat'),'')<>'issued-servicing-1'
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.servicingIssueDecisionId')),'00000000-0000-0000-0000-000000000000')<>d.Id
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.sourceQuoteId')),'00000000-0000-0000-0000-000000000000')<>t.SourceQuoteId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.baseVersionId')),'00000000-0000-0000-0000-000000000000')<>d.BaseVersionId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.revisionId')),'00000000-0000-0000-0000-000000000000')<>d.RevisionId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.transactionId')),'00000000-0000-0000-0000-000000000000')<>t.Id
                OR COALESCE(TRY_CONVERT(int,JSON_VALUE(v.SnapshotJson,'$.provenance.sliceOrdinal')),0)<>v.SliceOrdinal
                OR COALESCE(JSON_VALUE(v.SnapshotJson,'$.provenance.inputHash'),'')<>LOWER(CONVERT(varchar(64),d.InputHash,2))
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.effectiveAt')) IS NULL
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.effectiveAt'))<>v.EffectiveAt
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.processedAt')) IS NULL
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.processedAt'))<>v.ProcessedAt
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                  WHERE CONVERT(int,s.[key])+1=v.SliceOrdinal AND TRY_CONVERT(datetimeoffset,JSON_VALUE(s.value,'$.effectiveAt'))=v.EffectiveAt))
              THROW 51526,'Each servicing version must retain its exact approved slice and immutable decision provenance.',1;
            END;
            """);
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_ServicingDraft_Issued ON ServicingDraft AFTER INSERT,UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.State='issued' AND
              (i.State<>d.State OR i.IssuedTransactionId<>d.IssuedTransactionId OR i.CurrentRevisionId<>d.CurrentRevisionId
                OR ISNULL(i.CurrentCycleId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentCycleId,'00000000-0000-0000-0000-000000000000')))
              THROW 51525,'Issued drafts cannot be reopened or revised.',1;
            IF EXISTS(SELECT 1 FROM inserted d WHERE d.State='issued' AND d.Kind<>'cancellation' AND NOT EXISTS(
              SELECT 1 FROM PolicyTransaction t JOIN ServicingIssueDecision s ON s.Id=t.ServicingIssueDecisionId
                JOIN IssueFinancialObligation o ON o.TransactionId=t.Id JOIN Journal j ON j.ObligationId=o.Id AND j.PostedAt IS NOT NULL
                JOIN ServicingCycle c ON c.Id=s.CycleId
              WHERE t.Id=d.IssuedTransactionId AND t.ServicingDraftId=d.Id AND t.ServicingRevisionId=d.CurrentRevisionId AND t.ServicingCycleId=d.CurrentCycleId
                AND (SELECT COUNT(*) FROM PolicyVersion v WHERE v.TransactionId=t.Id)=(SELECT COUNT(*) FROM OPENJSON(c.InputJson,'$.slices'))
                AND NOT EXISTS(SELECT 1 FROM PolicyVersion v WHERE v.TransactionId=t.Id AND
                  ((SELECT COUNT(DISTINCT doc.Kind) FROM PolicyDocumentRequest doc WHERE doc.VersionId=v.Id AND doc.Purpose=t.Kind)<>3
                    OR NOT EXISTS(SELECT 1 FROM PolicyMidIntent m WHERE m.VersionId=v.Id AND m.Purpose=t.Kind)))
                AND NOT EXISTS(SELECT 1 FROM ServicingLease l WHERE l.DraftId=d.Id AND l.Active=1)))
              THROW 51525,'Issued draft requires its complete atomic policy, posting, documents and MID intent graph.',1;
            END;
            """);
    }
}
