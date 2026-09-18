using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingCapacitySubmissionStorage
{
    private static void AddSubmissionGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacitySubmission_Source ON ServicingCapacitySubmission AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(
                SELECT 1 FROM ServicingCapacityCase k WITH(UPDLOCK,HOLDLOCK)
                JOIN ServicingDraft d WITH(UPDLOCK,HOLDLOCK) ON d.Id=k.DraftId
                JOIN ServicingCycle c ON c.Id=k.CycleId
                JOIN ServicingRatingResult r ON r.Id=k.RatingId
                JOIN ServicingReferral f ON f.Id=k.ReferralId
                JOIN CapacityProvider p ON p.Id=k.ProviderId
                JOIN OutboxWork w ON w.Id=i.WorkId
                WHERE k.Id=i.CaseId AND k.State='draft' AND d.State='draft' AND d.CurrentCycleId=c.Id
                    AND d.CurrentRevisionId=i.RevisionId AND c.State='rated' AND c.CurrentRatingId=r.Id AND r.Outcome='rated'
                    AND f.State NOT IN ('declined','superseded') AND p.State='active'
                    AND i.SubmittedAt>=r.CompletedAt AND i.SubmittedAt<r.ExpiresAt AND i.SubmittedAt>=k.CreatedAt
                    AND w.Kind='servicing-capacity' AND w.SubjectRecordId=i.Id AND w.ScenarioVersionId=i.ScenarioVersionId
                    AND w.OperationKey='servicing-capacity/'+LOWER(REPLACE(CONVERT(varchar(36),i.Id),'-',''))
                    AND w.State='pending' AND w.CreatedBy=i.SubmittedBy
                    AND JSON_VALUE(i.ContextJson,'$.format')='servicing-capacity-submission-1'
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContextJson,'$.draftId'))=i.DraftId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContextJson,'$.revisionId'))=i.RevisionId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContextJson,'$.cycleId'))=i.CycleId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContextJson,'$.ratingId'))=i.RatingId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContextJson,'$.caseId'))=k.Id
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContextJson,'$.referralId'))=k.ReferralId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContextJson,'$.providerId'))=k.ProviderId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContextJson,'$.binderVersionId'))=k.BinderVersionId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ContextJson,'$.submissionId'))=i.Id
                    AND TRY_CONVERT(int,JSON_VALUE(i.ContextJson,'$.sequence'))=i.Sequence
                    AND i.Sequence=1+COALESCE((SELECT MAX(x.Sequence) FROM ServicingCapacitySubmission x WHERE x.CaseId=i.CaseId AND x.Id<>i.Id),0)))
                THROW 51430,'Submission requires current owned capacity, exact context and ordered durable work.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacitySubmission_Immutable ON ServicingCapacitySubmission AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51431,'Capacity submissions are immutable.',1;
            END;
            """);
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

    private static void RemoveSubmissionGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            DROP TRIGGER IF EXISTS TR_ServicingCapacitySubmission_Source;
            DROP TRIGGER IF EXISTS TR_ServicingCapacitySubmission_Immutable;
            DROP TRIGGER IF EXISTS TR_ServicingCapacityCase_History;
            """);
    }

    private static void RestoreCaseGuard(MigrationBuilder migration) => migration.Sql("""
        CREATE TRIGGER TR_ServicingCapacityCase_History ON ServicingCapacityCase AFTER UPDATE,DELETE AS BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT Id,DraftId,RevisionId,CycleId,RatingId,ReferralId,ProviderId,BinderVersionId,RaisedBy,Reason,CreatedAt,CreatedBy FROM deleted
            EXCEPT SELECT Id,DraftId,RevisionId,CycleId,RatingId,ReferralId,ProviderId,BinderVersionId,RaisedBy,Reason,CreatedAt,CreatedBy FROM inserted)
            THROW 51421,'Servicing capacity provenance is immutable.',1;
        IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
            WHERE i.State NOT IN ('draft','superseded') OR (d.State='superseded' AND i.State<>'superseded') OR i.UpdatedAt<d.UpdatedAt)
            THROW 51422,'Capacity requires retained monotonic state and exact response provenance.',1;
        END;
        """);
}
