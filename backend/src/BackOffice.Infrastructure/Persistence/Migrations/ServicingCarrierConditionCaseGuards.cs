using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingCarrierConditions
{
    private static void EnableConditionalCaseGuard(MigrationBuilder migration)
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
                LEFT JOIN ServicingCapacityResponse r ON r.Id=i.CurrentResponseId
                LEFT JOIN ServicingCapacityResponse prior ON prior.Id=d.CurrentResponseId
                WHERE i.UpdatedAt<d.UpdatedAt
                    OR (d.State='superseded' AND (i.State<>'superseded'
                        OR ISNULL(i.CurrentSubmissionId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentSubmissionId,'00000000-0000-0000-0000-000000000000')
                        OR ISNULL(i.CurrentResponseId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentResponseId,'00000000-0000-0000-0000-000000000000')))
                    OR (d.CurrentSubmissionId IS NOT NULL AND (s.Id IS NULL OR s.Sequence<old.Sequence))
                    OR (s.Id IS NOT NULL AND s.Sequence<>(SELECT MAX(x.Sequence) FROM ServicingCapacitySubmission x WHERE x.CaseId=i.Id))
                    OR (i.State NOT IN ('draft','superseded') AND s.Id IS NULL)
                    OR (d.State='draft' AND i.State NOT IN ('draft','superseded') AND (i.State<>'queued' OR s.Sequence<=COALESCE(old.Sequence,0)))
                    OR (ISNULL(i.CurrentSubmissionId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentSubmissionId,'00000000-0000-0000-0000-000000000000')
                        AND (d.State<>'draft' OR i.State<>'queued' OR i.CurrentResponseId IS NOT NULL))
                    OR (i.State IN ('queued','sent','failed') AND i.CurrentResponseId IS NOT NULL)
                    OR (d.CurrentResponseId IS NOT NULL AND i.CurrentSubmissionId=d.CurrentSubmissionId AND (r.Id IS NULL OR r.Sequence<prior.Sequence))
                    OR (r.Id IS NOT NULL AND (r.ApplicationState<>'applied' OR r.SubmissionId<>i.CurrentSubmissionId
                        OR r.Sequence<>(SELECT MAX(x.Sequence) FROM ServicingCapacityResponse x WHERE x.SubmissionId=i.CurrentSubmissionId AND x.ApplicationState='applied')))
                    OR (i.State IN ('approved','conditional','queried','declined') AND (r.Id IS NULL OR i.State<>CASE r.Outcome WHEN 'approve' THEN 'approved' WHEN 'query' THEN 'queried' WHEN 'decline' THEN 'declined' ELSE 'conditional' END)))
                THROW 51422,'Capacity requires retained monotonic state and exact current response provenance.',1;
            END;
            """);
    }
    private static void RestoreResponseCaseGuard(MigrationBuilder migration)
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
                LEFT JOIN ServicingCapacityResponse r ON r.Id=i.CurrentResponseId
                LEFT JOIN ServicingCapacityResponse prior ON prior.Id=d.CurrentResponseId
                WHERE i.State='conditional' OR i.UpdatedAt<d.UpdatedAt
                    OR (d.State='superseded' AND (i.State<>'superseded'
                        OR ISNULL(i.CurrentSubmissionId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentSubmissionId,'00000000-0000-0000-0000-000000000000')
                        OR ISNULL(i.CurrentResponseId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentResponseId,'00000000-0000-0000-0000-000000000000')))
                    OR (d.CurrentSubmissionId IS NOT NULL AND (s.Id IS NULL OR s.Sequence<old.Sequence))
                    OR (s.Id IS NOT NULL AND s.Sequence<>(SELECT MAX(x.Sequence) FROM ServicingCapacitySubmission x WHERE x.CaseId=i.Id))
                    OR (i.State NOT IN ('draft','superseded') AND s.Id IS NULL)
                    OR (d.State='draft' AND i.State NOT IN ('draft','superseded') AND (i.State<>'queued' OR s.Sequence<=COALESCE(old.Sequence,0)))
                    OR (ISNULL(i.CurrentSubmissionId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentSubmissionId,'00000000-0000-0000-0000-000000000000')
                        AND (d.State<>'draft' OR i.State<>'queued' OR i.CurrentResponseId IS NOT NULL))
                    OR (i.State IN ('queued','sent','failed') AND i.CurrentResponseId IS NOT NULL)
                    OR (d.CurrentResponseId IS NOT NULL AND i.CurrentSubmissionId=d.CurrentSubmissionId AND (r.Id IS NULL OR r.Sequence<prior.Sequence))
                    OR (r.Id IS NOT NULL AND (r.ApplicationState<>'applied' OR r.SubmissionId<>i.CurrentSubmissionId
                        OR r.Sequence<>(SELECT MAX(x.Sequence) FROM ServicingCapacityResponse x WHERE x.SubmissionId=i.CurrentSubmissionId AND x.ApplicationState='applied')))
                    OR (i.State IN ('approved','queried','declined') AND (r.Id IS NULL OR i.State<>CASE r.Outcome WHEN 'approve' THEN 'approved' WHEN 'query' THEN 'queried' WHEN 'decline' THEN 'declined' ELSE 'conditional' END)))
                THROW 51422,'Capacity requires retained monotonic state and exact current response provenance.',1;
            END;
            """);
    }
}
