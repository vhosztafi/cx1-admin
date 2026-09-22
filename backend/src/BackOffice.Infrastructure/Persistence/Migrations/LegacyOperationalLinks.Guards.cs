using Microsoft.EntityFrameworkCore.Migrations;
namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class LegacyOperationalLinks
{
    private static void AddLegacyGuards(MigrationBuilder migration)
    {
        migration.Sql("CREATE TRIGGER TR_MatchCorrespondence_Retained ON MatchCorrespondence AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 52040,'Recorded correspondence association is immutable.',1; END;");
        migration.Sql("""
        CREATE TRIGGER TR_MatchCorrespondence_Source ON MatchCorrespondence AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted l
            JOIN MatchInformationRequest r ON r.Id=l.InformationRequestId
            JOIN MatchReview review ON review.Id=r.MatchId
            JOIN MatchSubmission intake ON intake.Id=review.SubmissionId
            JOIN OperationalMessageDraft m ON m.Id=l.MessageId
            JOIN OperationalThread t ON t.Id=m.ThreadId
            JOIN OperationalSubject s ON s.Id=t.SubjectId
            WHERE l.CreatedBy<>r.ActorId OR m.CreatedBy<>r.ActorId OR t.CreatedBy<>r.ActorId
              OR s.Kind<>'agency' OR s.AgencyId<>intake.AgencyId OR t.Visibility<>'internal'
              OR t.RelationshipId IS NOT NULL OR m.State<>'draft'
              OR EXISTS(SELECT 1 FROM MessageDraftRecipient recipient WHERE recipient.MessageId=m.Id)
              OR EXISTS(SELECT 1 FROM MessageDraftAttachment attachment WHERE attachment.MessageId=m.Id))
            THROW 52041,'Recorded request correspondence must retain its owned internal audience.',1;
        END;
        """);
    }
}
