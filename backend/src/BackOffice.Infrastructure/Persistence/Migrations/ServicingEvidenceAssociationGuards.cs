using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingEvidenceAssociations
{
    private static void AddEvidenceGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingEvidenceAssociation_Source ON ServicingEvidenceAssociation AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId
                JOIN ServicingDraft d ON d.Id=i.DraftId JOIN ServicingRatingResult r ON r.Id=i.RatingId
                JOIN ServicingEvidenceFile f ON f.Id=i.FileId
                WHERE d.State<>'draft' OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>i.CycleId
                  OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
                  OR c.State<>'rated' OR c.CurrentRatingId IS NULL OR c.CurrentRatingId<>i.RatingId OR r.Outcome<>'rated'
                  OR i.CreatedAt<r.CompletedAt OR i.CreatedAt>=r.ExpiresAt OR i.CreatedAt<f.CreatedAt OR f.ScreeningState<>'accepted')
              THROW 51320,'Evidence requires the current rated revision and an earlier owned screened file.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId
                WHERE i.RequirementCode IN ('photocard-both-sides','driving-record') AND NOT EXISTS(
                  SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                  CROSS APPLY OPENJSON(s.value,'$.input.drivers') WITH(Id uniqueidentifier '$.id') driver WHERE driver.Id=i.RiskItemId))
              THROW 51321,'Driver proof requires an actual driver in a cumulative rated slice.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId
                WHERE i.RequirementCode='premises-security' AND NOT EXISTS(
                  SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                  CROSS APPLY OPENJSON(s.value,'$.input.pricing.cover.requestedSections') section
                  CROSS APPLY OPENJSON(section.value,'$.premisesIds') target
                  WHERE JSON_VALUE(section.value,'$.code')='premises' AND JSON_VALUE(section.value,'$.selected')='true'
                    AND TRY_CONVERT(uniqueidentifier,target.value)=i.RiskItemId))
              THROW 51322,'Premises proof requires selected premises in a cumulative rated slice.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingEvidenceEvent_AppendOnly ON ServicingEvidenceEvent AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51323,'Servicing evidence events are append-only.',1; END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingEvidenceEvent_Source ON ServicingEvidenceEvent AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingEvidenceAssociation a ON a.Id=i.AssociationId
                WHERE a.WithdrawnEventId IS NOT NULL OR i.CreatedAt<a.CreatedAt OR i.RecordedAt<a.CreatedAt
                  OR i.Sequence<>1+COALESCE((SELECT MAX(e.Sequence) FROM ServicingEvidenceEvent e WHERE e.AssociationId=i.AssociationId AND e.Sequence<i.Sequence),0)
                  OR EXISTS(SELECT 1 FROM ServicingEvidenceEvent e WHERE e.AssociationId=i.AssociationId AND e.Sequence<i.Sequence AND (e.RecordedAt>i.RecordedAt OR e.Kind='withdrawal')))
              THROW 51324,'Evidence events require ordered time and sequence and cannot follow withdrawal.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId JOIN AuthorityVersion v ON v.Id=i.AuthorityVersionId
                WHERE v.ProductVersionId<>c.ProductVersionId OR v.BinderVersionId<>c.BinderVersionId)
              THROW 51325,'Evidence review authority must belong to the same product and binder.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingEvidenceAssociation_History ON ServicingEvidenceAssociation AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT Id,DraftId,CycleId,RevisionId,RatingId,FileId,RequirementCode,RiskItemId,InputFingerprint,Reason,CreatedBy,CreatedAt FROM deleted
                EXCEPT SELECT Id,DraftId,CycleId,RevisionId,RatingId,FileId,RequirementCode,RiskItemId,InputFingerprint,Reason,CreatedBy,CreatedAt FROM inserted)
              THROW 51326,'Servicing evidence association provenance is immutable.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE d.WithdrawnEventId IS NOT NULL
                AND (i.WithdrawnEventId IS NULL OR i.WithdrawnEventId<>d.WithdrawnEventId OR
                    COALESCE(i.LatestReviewId,'00000000-0000-0000-0000-000000000000')<>COALESCE(d.LatestReviewId,'00000000-0000-0000-0000-000000000000')))
              THROW 51327,'Withdrawn evidence cannot be reactivated.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
                LEFT JOIN ServicingEvidenceEvent e ON e.Id=i.LatestReviewId
                WHERE COALESCE(i.LatestReviewId,'00000000-0000-0000-0000-000000000000')<>COALESCE(d.LatestReviewId,'00000000-0000-0000-0000-000000000000')
                  AND (e.Id IS NULL OR e.Kind<>'review' OR e.Sequence<>(SELECT MAX(x.Sequence) FROM ServicingEvidenceEvent x WHERE x.AssociationId=i.Id AND x.Kind='review')))
              THROW 51328,'The current review must be the latest owned review event.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingEvidenceEvent e ON e.Id=i.WithdrawnEventId WHERE e.Kind<>'withdrawal')
              THROW 51329,'Withdrawal pointer requires an owned withdrawal event.',1;
            END;
            """);
    }
}
