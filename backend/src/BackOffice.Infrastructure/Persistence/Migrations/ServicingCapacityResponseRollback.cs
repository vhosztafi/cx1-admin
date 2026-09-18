using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingCapacityResponses
{
    private static void RestorePriorCaseGuard(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_ServicingCapacityCase_History ON ServicingCapacityCase AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT Id,DraftId,RevisionId,CycleId,RatingId,ReferralId,ProviderId,BinderVersionId,RaisedBy,Reason,CreatedAt,CreatedBy FROM deleted
                EXCEPT SELECT Id,DraftId,RevisionId,CycleId,RatingId,ReferralId,ProviderId,BinderVersionId,RaisedBy,Reason,CreatedAt,CreatedBy FROM inserted)
                THROW 51421,'Servicing capacity provenance is immutable.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
                LEFT JOIN ServicingCapacitySubmission s ON s.Id=i.CurrentSubmissionId
                LEFT JOIN ServicingCapacitySubmission old ON old.Id=d.CurrentSubmissionId
                WHERE i.State NOT IN ('draft','queued','failed','superseded') OR i.UpdatedAt<d.UpdatedAt
                    OR (d.State='superseded' AND (i.State<>'superseded' OR ISNULL(i.CurrentSubmissionId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentSubmissionId,'00000000-0000-0000-0000-000000000000')))
                    OR (d.CurrentSubmissionId IS NOT NULL AND (s.Id IS NULL OR s.Sequence<old.Sequence))
                    OR (s.Id IS NOT NULL AND s.Sequence<>(SELECT MAX(x.Sequence) FROM ServicingCapacitySubmission x WHERE x.CaseId=i.Id))
                    OR (i.State IN ('queued','failed') AND s.Id IS NULL)
                    OR (d.State='draft' AND i.State='queued' AND (s.Id IS NULL OR s.Sequence<=COALESCE(old.Sequence,0)))
                    OR (ISNULL(i.CurrentSubmissionId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentSubmissionId,'00000000-0000-0000-0000-000000000000') AND (d.State<>'draft' OR i.State<>'queued')))
                THROW 51422,'Capacity requires retained monotonic state and exact submission provenance.',1;
            END;
            """);
    }
}
