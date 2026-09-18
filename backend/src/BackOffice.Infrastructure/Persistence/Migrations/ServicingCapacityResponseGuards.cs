using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingCapacityResponses
{
    private static void AddResponseGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacityResponse_Source ON ServicingCapacityResponse AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(
                SELECT 1 FROM ServicingCapacitySubmission s JOIN ServicingCapacityCase k WITH(UPDLOCK,HOLDLOCK) ON k.Id=s.CaseId
                WHERE s.Id=i.SubmissionId AND k.ProviderId=i.ProviderId AND i.ReceivedAt>=s.SubmittedAt
                    AND i.Sequence=1+COALESCE((SELECT MAX(x.Sequence) FROM ServicingCapacityResponse x WHERE x.CaseId=i.CaseId AND x.Id<>i.Id),0)
                    AND NOT EXISTS(SELECT 1 FROM ServicingCapacityResponse x WHERE x.CaseId=i.CaseId AND x.Id<>i.Id AND x.RecordedAt>i.RecordedAt)
                    AND JSON_VALUE(i.DefinitionJson,'$.format')='servicing-capacity-response-1'
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DefinitionJson,'$.draftId'))=i.DraftId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DefinitionJson,'$.revisionId'))=i.RevisionId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DefinitionJson,'$.cycleId'))=i.CycleId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DefinitionJson,'$.ratingId'))=i.RatingId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DefinitionJson,'$.caseId'))=k.Id
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DefinitionJson,'$.referralId'))=k.ReferralId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DefinitionJson,'$.providerId'))=k.ProviderId
                    AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DefinitionJson,'$.submissionId'))=s.Id
                    AND JSON_VALUE(i.DefinitionJson,'$.submissionHash') COLLATE Latin1_General_100_BIN2=LOWER(CONVERT(varchar(64),s.ContextHash,2))
                    AND JSON_VALUE(i.DefinitionJson,'$.outcome')=i.Outcome
                    AND ISJSON(JSON_QUERY(i.DefinitionJson,'$.authorisedLimits'),ARRAY)=1
                    AND ISJSON(JSON_QUERY(i.DefinitionJson,'$.conditions'),ARRAY)=1
                    AND ((i.Outcome IN ('approve','approve-with-conditions')
                        AND TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DefinitionJson,'$.validFrom'))<TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DefinitionJson,'$.validTo'))
                        AND (SELECT COUNT(*) FROM OPENJSON(i.DefinitionJson,'$.authorisedLimits')) BETWEEN 1 AND 20)
                      OR (i.Outcome IN ('query','decline') AND JSON_VALUE(i.DefinitionJson,'$.validFrom') IS NULL
                        AND JSON_VALUE(i.DefinitionJson,'$.validTo') IS NULL AND (SELECT COUNT(*) FROM OPENJSON(i.DefinitionJson,'$.authorisedLimits'))=0))
                    AND ((i.Outcome='approve-with-conditions' AND (SELECT COUNT(*) FROM OPENJSON(i.DefinitionJson,'$.conditions')) BETWEEN 1 AND 20)
                      OR (i.Outcome<>'approve-with-conditions' AND (SELECT COUNT(*) FROM OPENJSON(i.DefinitionJson,'$.conditions'))=0))))
                THROW 51470,'Response requires exact owned submission context, extent and ordered immutable history.',1;
            IF EXISTS(SELECT 1 FROM inserted i WHERE i.ApplicationState='applied' AND NOT EXISTS(
                SELECT 1 FROM ServicingCapacityCase k JOIN ServicingDraft d WITH(UPDLOCK,HOLDLOCK) ON d.Id=k.DraftId
                JOIN ServicingCycle c ON c.Id=k.CycleId JOIN ServicingRatingResult r ON r.Id=k.RatingId
                JOIN ServicingReferral f ON f.Id=k.ReferralId
                JOIN CapacityProvider p ON p.Id=k.ProviderId
                WHERE k.Id=i.CaseId AND k.CurrentSubmissionId=i.SubmissionId AND k.State NOT IN ('draft','superseded')
                    AND d.State='draft' AND d.CurrentCycleId=c.Id AND d.CurrentRevisionId=i.RevisionId
                    AND c.State='rated' AND c.CurrentRatingId=r.Id AND r.Outcome='rated' AND p.State='active'
                    AND f.State NOT IN ('declined','superseded')
                    AND i.RecordedAt>=r.CompletedAt AND i.RecordedAt<r.ExpiresAt))
                THROW 51470,'Applied response requires a current active unexpired servicing case.',1;
            IF EXISTS(SELECT 1 FROM inserted i WHERE i.Provenance='supplied-response' AND NOT EXISTS(
                SELECT 1 FROM ServicingEvidenceAssociation a JOIN ServicingEvidenceEvent e ON e.Id=i.EvidenceReviewId
                JOIN ServicingEvidenceFile f ON f.Id=a.FileId
                WHERE a.Id=i.EvidenceAssociationId AND a.CapacitySubmissionId=i.SubmissionId AND a.RequirementCode='capacity-response'
                    AND a.LatestReviewId=e.Id AND a.WithdrawnEventId IS NULL AND e.Kind='review' AND e.Outcome='accepted'
                    AND f.ScreeningState='accepted' AND i.RecordedAt>=e.RecordedAt))
                THROW 51470,'Supplied response requires its exact current accepted response proof.',1;
            IF EXISTS(SELECT 1 FROM inserted i WHERE i.Provenance='demo-provider' AND NOT EXISTS(
                SELECT 1 FROM DemoProviderOperation o JOIN ServicingCapacitySubmission s ON s.Id=i.SubmissionId
                JOIN AdapterInbox n ON n.Id=i.InboxId
                CROSS APPLY OPENJSON(o.Result) WITH(DefinitionJson nvarchar(max) '$.definitionJson',Body nvarchar(max) '$.body',Outcome nvarchar(30) '$.outcome',ReceivedAt datetimeoffset '$.receivedAt') payload
                WHERE o.Id=i.ProviderOperationId AND o.Kind='servicing-capacity'
                    AND o.OperationKey='servicing-capacity/'+LOWER(REPLACE(CONVERT(varchar(36),s.Id),'-',''))
                    AND o.RequestHash=s.ContextHash AND o.ScenarioVersionId=s.ScenarioVersionId AND o.State='succeeded' AND o.Result IS NOT NULL
                    AND n.WorkId=s.WorkId AND n.Provider='demo-servicing-capacity'
                    AND n.EventId COLLATE Latin1_General_100_BIN2=i.ProviderEventId COLLATE Latin1_General_100_BIN2
                    AND n.ContentHash=HASHBYTES('SHA2_256',CONVERT(varchar(max),o.Result COLLATE Latin1_General_100_BIN2_UTF8))
                    AND i.ContentHash=HASHBYTES('SHA2_256',CONVERT(varchar(max),payload.DefinitionJson COLLATE Latin1_General_100_BIN2_UTF8))
                    AND HASHBYTES('SHA2_256',CONVERT(varbinary(max),i.Body))=HASHBYTES('SHA2_256',CONVERT(varbinary(max),payload.Body))
                    AND i.Outcome COLLATE Latin1_General_100_BIN2=payload.Outcome COLLATE Latin1_General_100_BIN2 AND i.ReceivedAt=payload.ReceivedAt
                    AND i.ProviderEventId='servicing-capacity-'+LOWER(REPLACE(CONVERT(varchar(36),o.Id),'-',''))))
                THROW 51470,'Demo response requires exact durable provider operation and inbox provenance.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacityResponse_Immutable ON ServicingCapacityResponse AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51471,'Carrier responses are immutable.',1;
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
