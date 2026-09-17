using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static class ServicingResolutionGuards
{
    internal static void Up(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingConditionResolution_Immutable ON ServicingConditionResolution AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51370,'Servicing condition resolutions are append-only.',1; END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingConditionResolution_Source ON ServicingConditionResolution AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCondition c ON c.Id=i.ConditionId
              JOIN ServicingReferral r ON r.Id=i.ReferralId JOIN ServicingCycle y ON y.Id=i.CycleId
              JOIN ServicingDraft d ON d.Id=i.DraftId JOIN ServicingRatingResult p ON p.Id=i.RatingId
              JOIN ServicingEvidenceAssociation a ON a.Id=i.AssociationId JOIN ServicingEvidenceEvent e ON e.Id=i.ReviewId
              WHERE c.Kind='risk-change' OR r.State NOT IN ('conditional','queried') OR r.LatestDecisionId IS NULL OR r.LatestDecisionId<>c.DecisionId
                OR d.State<>'draft' OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>i.CycleId
                OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
                OR y.State<>'rated' OR y.CurrentRatingId IS NULL OR y.CurrentRatingId<>i.RatingId OR i.RecordedAt>=p.ExpiresAt
                OR a.WithdrawnEventId IS NOT NULL OR a.LatestReviewId IS NULL OR a.LatestReviewId<>i.ReviewId
                OR e.Kind<>'review' OR (i.Outcome='satisfied' AND e.Outcome<>'accepted')
                OR i.CreatedAt<c.CreatedAt OR i.RecordedAt<c.CreatedAt OR i.RecordedAt<e.RecordedAt
                OR i.Sequence<>1+COALESCE((SELECT MAX(x.Sequence) FROM ServicingConditionResolution x WHERE x.ConditionId=i.ConditionId AND x.Sequence<i.Sequence),0)
                OR EXISTS(SELECT 1 FROM ServicingConditionResolution x WHERE x.ConditionId=i.ConditionId AND x.Sequence<i.Sequence AND x.RecordedAt>i.RecordedAt))
              THROW 51371,'Resolution requires a current condition and exact current reviewed proof in ordered history.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCondition c ON c.Id=i.ConditionId JOIN ServicingEvidenceAssociation a ON a.Id=i.AssociationId
                WHERE a.RequirementCode<>CASE c.Code WHEN 'provide-driver-proof' THEN JSON_VALUE(c.DefinitionJson,'$.requirementCode')
                    WHEN 'provide-premises-security' THEN 'premises-security' WHEN 'provide-trading-history' THEN 'trading-history'
                    WHEN 'provide-signed-statement' THEN 'signed-statement' ELSE 'warranty-acknowledgement' END
                OR (c.Code='provide-driver-proof' AND (a.RiskItemId IS NULL OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(c.DefinitionJson,'$.driverId')) IS NULL
                    OR a.RiskItemId<>TRY_CONVERT(uniqueidentifier,JSON_VALUE(c.DefinitionJson,'$.driverId')) OR JSON_VALUE(c.DefinitionJson,'$.requirementCode') IS NULL))
                OR (c.Code='provide-premises-security' AND (a.RiskItemId IS NULL OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(c.DefinitionJson,'$.premisesId')) IS NULL
                    OR a.RiskItemId<>TRY_CONVERT(uniqueidentifier,JSON_VALUE(c.DefinitionJson,'$.premisesId'))))
                OR (c.Code IN ('provide-trading-history','provide-signed-statement') AND a.RiskItemId IS NOT NULL))
              THROW 51372,'Resolution evidence must match the condition purpose and stable target.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN UserAuthorityGrant g ON g.Id=i.GrantId JOIN AuthorityVersion v ON v.Id=i.AuthorityVersionId
              JOIN ServicingCycle c ON c.Id=i.CycleId WHERE g.RevokedAt IS NOT NULL OR i.RecordedAt<g.EffectiveFrom OR i.RecordedAt>=g.EffectiveTo
                OR v.State<>'published' OR v.ProductVersionId<>c.ProductVersionId OR v.BinderVersionId<>c.BinderVersionId
                OR i.RecordedAt<v.EffectiveFrom OR i.RecordedAt>=v.EffectiveTo)
              THROW 51373,'Resolution requires an effective unrevoked same-product/binder grant for its actor.',1;
            END;
            """);
    }
}
