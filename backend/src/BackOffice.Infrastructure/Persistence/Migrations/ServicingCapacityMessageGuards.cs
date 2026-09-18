using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingCapacityCorrespondence
{
    private static void AddMessageGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacityMessage_Source ON ServicingCapacityMessage AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(
                SELECT 1 FROM ServicingCapacitySubmission s
                JOIN ServicingCapacityCase k WITH(UPDLOCK,HOLDLOCK) ON k.Id=s.CaseId
                JOIN ServicingDraft d WITH(UPDLOCK,HOLDLOCK) ON d.Id=k.DraftId
                JOIN ServicingCycle c ON c.Id=k.CycleId
                JOIN ServicingRatingResult r ON r.Id=k.RatingId
                JOIN CapacityProvider p ON p.Id=k.ProviderId
                WHERE s.Id=i.SubmissionId AND d.State='draft' AND d.CurrentCycleId=c.Id
                    AND d.CurrentRevisionId=i.RevisionId AND c.State='rated' AND c.CurrentRatingId=r.Id
                    AND r.Outcome='rated' AND p.State='active' AND i.RecordedAt>=s.SubmittedAt AND i.RecordedAt<r.ExpiresAt
                    AND ((i.Kind='submission' AND k.State='draft' AND s.SubmittedBy=i.RecordedBy
                        AND s.SubmittedAt=i.RecordedAt AND s.Body=i.Body
                        AND s.Sequence=(SELECT MAX(x.Sequence) FROM ServicingCapacitySubmission x WHERE x.CaseId=k.Id))
                      OR (i.Kind='chase' AND k.CurrentSubmissionId=s.Id AND k.State IN ('queued','sent','queried'))
                      OR (i.Kind='query-reply' AND k.CurrentSubmissionId=s.Id AND k.State='queried'))
                    AND i.Sequence=1+COALESCE((SELECT MAX(x.Sequence) FROM ServicingCapacityMessage x WHERE x.CaseId=i.CaseId AND x.Id<>i.Id),0)
                    AND NOT EXISTS(SELECT 1 FROM ServicingCapacityMessage x WHERE x.CaseId=i.CaseId AND x.Id<>i.Id AND x.RecordedAt>i.RecordedAt)))
                THROW 51450,'Capacity correspondence requires the current owned submission, ordered history and eligible case state.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacityMessage_Immutable ON ServicingCapacityMessage AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51451,'Capacity correspondence is immutable.',1;
            END;
            """);
    }
}
