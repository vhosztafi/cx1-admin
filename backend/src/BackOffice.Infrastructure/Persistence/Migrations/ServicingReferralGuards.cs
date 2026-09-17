using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingReferrals
{
    private static void AddReferralGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingReferral_Source ON ServicingReferral AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId JOIN ServicingDraft d ON d.Id=i.DraftId
                JOIN ServicingRatingResult r ON r.Id=i.RatingId WHERE i.State<>'open' OR i.LatestDecisionId IS NOT NULL OR d.State<>'draft'
                OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>c.Id OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
                OR c.State<>'rated' OR c.CurrentRatingId IS NULL OR c.CurrentRatingId<>i.RatingId OR r.Outcome<>'rated'
                OR i.CreatedAt<r.CompletedAt OR i.CreatedAt>=r.ExpiresAt)
              THROW 51340,'Referral requires the current rated draft revision and starts open.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId CROSS APPLY OPENJSON(i.RequiredAuthorityJson,'$.triggers') t
                WHERE COALESCE(JSON_VALUE(t.value,'$.source'),'') NOT IN ('binder','authority','source')
                OR COALESCE(JSON_VALUE(t.value,'$.requirement.ruleCode'),'')<>i.RuleCode OR COALESCE(JSON_VALUE(t.value,'$.requirement.dimension'),'')<>i.Dimension
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(t.value,'$.requirement.targetId')),'00000000-0000-0000-0000-000000000000')<>i.TargetKey
                OR (JSON_VALUE(t.value,'$.requirement.targetId') IS NOT NULL AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(t.value,'$.requirement.targetId')) IS NULL)
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                    WHERE TRY_CONVERT(datetimeoffset,JSON_VALUE(s.value,'$.effectiveAt'))=TRY_CONVERT(datetimeoffset,JSON_VALUE(t.value,'$.effectiveAt'))))
              THROW 51341,'Referral triggers must retain the owned rated dates, rule, dimension and target.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId WHERE i.RiskItemId IS NOT NULL AND NOT EXISTS(
                SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s CROSS APPLY OPENJSON(s.value,'$.input.drivers') WITH(Id uniqueidentifier '$.id') driver WHERE driver.Id=i.RiskItemId))
              THROW 51342,'Referral item must exist in the cumulative rated driver risk.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingReferralDecision_AppendOnly ON ServicingReferralDecision AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51343,'Servicing referral decisions are append-only.',1; END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingReferralDecision_Source ON ServicingReferralDecision AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingReferral r ON r.Id=i.ReferralId JOIN ServicingCycle c ON c.Id=i.CycleId
                JOIN ServicingDraft d ON d.Id=i.DraftId JOIN ServicingRatingResult p ON p.Id=i.RatingId
                WHERE r.State='superseded' OR d.State<>'draft' OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>i.CycleId
                OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId OR c.State<>'rated' OR c.CurrentRatingId IS NULL OR c.CurrentRatingId<>i.RatingId
                OR p.Outcome<>'rated' OR i.DecidedAt>=p.ExpiresAt OR i.CreatedAt<r.CreatedAt OR i.DecidedAt<r.CreatedAt
                OR i.Sequence<>1+COALESCE((SELECT MAX(x.Sequence) FROM ServicingReferralDecision x WHERE x.ReferralId=i.ReferralId AND x.Sequence<i.Sequence),0)
                OR EXISTS(SELECT 1 FROM ServicingReferralDecision x WHERE x.ReferralId=i.ReferralId AND x.Sequence<i.Sequence AND x.DecidedAt>i.DecidedAt)
                OR (SELECT COUNT(*) FROM OPENJSON(i.ConditionsJson))>20)
              THROW 51344,'Decision requires current unexpired referral scope and ordered immutable history.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN UserAuthorityGrant g ON g.Id=i.GrantId JOIN AuthorityVersion v ON v.Id=i.AuthorityVersionId
                JOIN ServicingCycle c ON c.Id=i.CycleId WHERE g.RevokedAt IS NOT NULL OR i.DecidedAt<g.EffectiveFrom OR i.DecidedAt>=g.EffectiveTo
                OR v.ProductVersionId<>c.ProductVersionId OR v.BinderVersionId<>c.BinderVersionId OR v.State<>'published'
                OR i.DecidedAt<v.EffectiveFrom OR i.DecidedAt>=v.EffectiveTo)
              THROW 51345,'Decision requires an effective unrevoked same-product/binder grant for its actor.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingReferral_History ON ServicingReferral AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT Id,DraftId,CycleId,RevisionId,RatingId,Sequence,RuleCode,Dimension,RiskItemId,TargetKey,RequiredAuthorityJson,Reason,CreatedAt,CreatedBy FROM deleted
                EXCEPT SELECT Id,DraftId,CycleId,RevisionId,RatingId,Sequence,RuleCode,Dimension,RiskItemId,TargetKey,RequiredAuthorityJson,Reason,CreatedAt,CreatedBy FROM inserted)
              THROW 51346,'Servicing referral provenance is immutable.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE
                (d.State='superseded' AND i.State<>'superseded') OR (i.State='superseded' AND
                COALESCE(i.LatestDecisionId,'00000000-0000-0000-0000-000000000000')<>COALESCE(d.LatestDecisionId,'00000000-0000-0000-0000-000000000000')))
              THROW 51347,'Superseded referrals retain their historical decision and cannot reactivate.',1;
            IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN ServicingReferralDecision e ON e.Id=i.LatestDecisionId WHERE i.State<>'superseded' AND
                ((e.Id IS NULL AND (i.State<>'open' OR EXISTS(SELECT 1 FROM ServicingReferralDecision x WHERE x.ReferralId=i.Id))) OR
                 (e.Id IS NOT NULL AND (e.Sequence<>(SELECT MAX(x.Sequence) FROM ServicingReferralDecision x WHERE x.ReferralId=i.Id) OR
                    i.State<>CASE e.Outcome WHEN 'approve' THEN 'approved' WHEN 'approve-with-conditions' THEN 'conditional' WHEN 'query' THEN 'queried' WHEN 'decline' THEN 'declined' WHEN 'reopen' THEN 'open' END))))
              THROW 51348,'Referral state must match its latest owned immutable decision.',1;
            END;
            """);
    }
}
