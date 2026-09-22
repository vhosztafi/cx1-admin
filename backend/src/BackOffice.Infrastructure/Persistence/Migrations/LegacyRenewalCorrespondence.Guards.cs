using Microsoft.EntityFrameworkCore.Migrations;
namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class LegacyRenewalCorrespondence
{
    private static void AddLegacyRenewalGuards(MigrationBuilder migration)
    {
        migration.Sql("CREATE TRIGGER TR_RenewalLapseCorrespondence_Retained ON RenewalLapseCorrespondence AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 52045,'Lapse correspondence association is immutable.',1; END;");
        migration.Sql("""
        CREATE TRIGGER TR_RenewalLapseCorrespondence_Source ON RenewalLapseCorrespondence AFTER INSERT AS BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted l
            JOIN RenewalLapseEvent lapse ON lapse.Id=l.LapseEventId
            JOIN PolicyTerm term ON term.Id=lapse.TermId
            JOIN OperationalMessageDraft m ON m.Id=l.MessageId
            JOIN OperationalThread t ON t.Id=m.ThreadId
            JOIN OperationalSubject s ON s.Id=t.SubjectId
            WHERE m.CreatedBy<>l.CreatedBy OR t.CreatedBy<>l.CreatedBy OR term.PolicyId<>lapse.PolicyId OR term.EndsAt<>lapse.EffectiveAt
              OR s.Kind<>'policy' OR s.PolicyId<>lapse.PolicyId OR t.Visibility<>'internal' OR t.RelationshipId IS NOT NULL OR m.State<>'draft'
              OR EXISTS(SELECT 1 FROM MessageDraftRecipient recipient WHERE recipient.MessageId=m.Id)
              OR EXISTS(SELECT 1 FROM MessageDraftAttachment attachment WHERE attachment.MessageId=m.Id))
            THROW 52046,'Lapse correspondence must retain its owned internal policy audience.',1;
        END;
        """);
    }
}
