using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingCapacitySelectedEvidence
{
    private static void AddEvidenceGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacitySubmissionEvidence_Source ON ServicingCapacitySubmissionEvidence AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(
                SELECT 1 FROM ServicingCapacitySubmission s
                JOIN ServicingCapacityCase k WITH(UPDLOCK,HOLDLOCK) ON k.Id=s.CaseId
                JOIN ServicingEvidenceAssociation a WITH(HOLDLOCK) ON a.Id=i.AssociationId
                JOIN ServicingEvidenceEvent e ON e.Id=i.ReviewId AND e.AssociationId=a.Id
                JOIN ServicingEvidenceFile f ON f.Id=a.FileId
                WHERE s.Id=i.SubmissionId AND k.State='draft' AND (k.CurrentSubmissionId IS NULL OR k.CurrentSubmissionId<>s.Id)
                    AND s.Sequence=(SELECT MAX(x.Sequence) FROM ServicingCapacitySubmission x WHERE x.CaseId=k.Id)
                    AND i.CreatedBy=s.SubmittedBy AND i.CreatedAt>=s.SubmittedAt
                    AND a.WithdrawnEventId IS NULL AND a.LatestReviewId=e.Id AND e.Kind='review' AND e.Outcome='accepted' AND f.ScreeningState='accepted'
                    AND EXISTS(SELECT 1 FROM OPENJSON(s.ContextJson,'$.evidence') WITH(AssociationId uniqueidentifier '$.associationId',ReviewId uniqueidentifier '$.reviewId') x
                        WHERE x.AssociationId=i.AssociationId AND x.ReviewId=i.ReviewId)))
                THROW 51440,'Selected proof requires exact current reviewed evidence named in the unqueued request.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacitySubmissionEvidence_Immutable ON ServicingCapacitySubmissionEvidence AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51441,'Selected submission evidence is immutable.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacityCase_Evidence ON ServicingCapacityCase AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
                JOIN ServicingCapacitySubmission s ON s.Id=i.CurrentSubmissionId
                WHERE i.State='queued' AND (d.State<>'queued' OR d.CurrentSubmissionId IS NULL OR d.CurrentSubmissionId<>i.CurrentSubmissionId)
                AND (COALESCE(ISJSON(JSON_QUERY(s.ContextJson,'$.evidence'),ARRAY),0)<>1
                    OR (SELECT COUNT(*) FROM OPENJSON(s.ContextJson,'$.evidence'))>20
                    OR (SELECT COUNT(*) FROM OPENJSON(s.ContextJson,'$.evidence'))<>(SELECT COUNT(*) FROM ServicingCapacitySubmissionEvidence x WHERE x.SubmissionId=s.Id)
                    OR EXISTS(SELECT 1 FROM OPENJSON(s.ContextJson,'$.evidence') WITH(AssociationId uniqueidentifier '$.associationId',ReviewId uniqueidentifier '$.reviewId') manifest
                        WHERE NOT EXISTS(SELECT 1 FROM ServicingCapacitySubmissionEvidence selected
                            JOIN ServicingEvidenceAssociation a WITH(HOLDLOCK) ON a.Id=selected.AssociationId
                            JOIN ServicingEvidenceEvent e ON e.Id=selected.ReviewId
                            JOIN ServicingEvidenceFile f ON f.Id=a.FileId
                            WHERE selected.SubmissionId=s.Id AND selected.AssociationId=manifest.AssociationId AND selected.ReviewId=manifest.ReviewId
                                AND a.LatestReviewId=e.Id AND e.Kind='review' AND e.Outcome='accepted' AND a.WithdrawnEventId IS NULL AND f.ScreeningState='accepted'))))
                THROW 51442,'Queued capacity requires the complete exact current reviewed evidence manifest.',1;
            END;
            """);
    }

    private static void RemoveEvidenceGuards(MigrationBuilder migration) => migration.Sql("""
        DROP TRIGGER IF EXISTS TR_ServicingCapacityCase_Evidence;
        DROP TRIGGER IF EXISTS TR_ServicingCapacitySubmissionEvidence_Source;
        DROP TRIGGER IF EXISTS TR_ServicingCapacitySubmissionEvidence_Immutable;
        """);
}
