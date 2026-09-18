using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingCapacityCaseStorage
{
    private static void AddGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacityCase_Source ON ServicingCapacityCase AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i WHERE i.State<>'draft' OR NOT EXISTS(
                SELECT 1 FROM ServicingDraft d WITH(UPDLOCK,HOLDLOCK)
                JOIN ServicingCycle c WITH(UPDLOCK,HOLDLOCK) ON c.Id=i.CycleId AND c.DraftId=d.Id
                JOIN ServicingReferral f WITH(UPDLOCK,HOLDLOCK) ON f.Id=i.ReferralId AND f.CycleId=c.Id
                JOIN ServicingRatingResult r ON r.Id=i.RatingId AND r.CycleId=c.Id
                JOIN BinderVersion b ON b.Id=c.BinderVersionId
                JOIN CapacityProvider p ON p.Id=b.ProviderId
                WHERE d.Id=i.DraftId AND d.State='draft' AND d.CurrentCycleId=c.Id
                    AND d.CurrentRevisionId=i.RevisionId AND c.RevisionId=i.RevisionId
                    AND c.State='rated' AND c.CurrentRatingId=r.Id AND r.Outcome='rated'
                    AND f.State NOT IN ('superseded','declined') AND f.RatingId=r.Id
                    AND i.BinderVersionId=b.Id AND i.ProviderId=p.Id AND p.State='active'
                    AND i.CreatedAt>=r.CompletedAt AND i.CreatedAt<r.ExpiresAt AND i.CreatedAt>=f.CreatedAt))
                THROW 51420,'Capacity requires a current owned referral, rating and pinned provider.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacityCase_History ON ServicingCapacityCase AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT Id,DraftId,RevisionId,CycleId,RatingId,ReferralId,ProviderId,BinderVersionId,RaisedBy,Reason,CreatedAt,CreatedBy FROM deleted
                EXCEPT SELECT Id,DraftId,RevisionId,CycleId,RatingId,ReferralId,ProviderId,BinderVersionId,RaisedBy,Reason,CreatedAt,CreatedBy FROM inserted)
                THROW 51421,'Servicing capacity provenance is immutable.',1;
            -- No carrier authority exists until the owned submission/response
            -- graph is introduced by a later additive migration in this plan.
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
                WHERE i.State NOT IN ('draft','superseded') OR (d.State='superseded' AND i.State<>'superseded') OR i.UpdatedAt<d.UpdatedAt)
                THROW 51422,'Capacity requires retained monotonic state and exact response provenance.',1;
            END;
            """);
    }

    private static void RemoveGuards(MigrationBuilder migration) => migration.Sql("""
        DROP TRIGGER IF EXISTS TR_ServicingCapacityCase_Source;
        DROP TRIGGER IF EXISTS TR_ServicingCapacityCase_History;
        """);
}
