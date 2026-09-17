using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static class ServicingConditionGuards
{
    internal static void Up(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCondition_Immutable ON ServicingCondition AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51360,'Servicing condition definitions are immutable.',1; END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCondition_Source ON ServicingCondition AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingReferralDecision e ON e.Id=i.DecisionId
              JOIN ServicingReferral r ON r.Id=i.ReferralId JOIN ServicingCycle c ON c.Id=i.CycleId
              JOIN ServicingDraft d ON d.Id=i.DraftId JOIN ServicingRatingResult p ON p.Id=i.RatingId
              WHERE e.Outcome NOT IN ('approve-with-conditions','query') OR r.State='superseded'
                OR d.State<>'draft' OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>i.CycleId
                OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
                OR c.State<>'rated' OR c.CurrentRatingId IS NULL OR c.CurrentRatingId<>i.RatingId
                OR i.CreatedAt<e.DecidedAt OR i.CreatedAt>=p.ExpiresAt OR i.CreatedBy<>e.ActorId
                OR EXISTS(SELECT 1 FROM ServicingReferralDecision later WHERE later.ReferralId=i.ReferralId AND later.Sequence>e.Sequence)
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(e.ConditionsJson) j WHERE TRY_CONVERT(int,j.[key])=i.Sequence-1
                    AND j.type=5 AND j.value COLLATE Latin1_General_100_BIN2=i.DefinitionJson COLLATE Latin1_General_100_BIN2)
                OR i.Kind<>CASE WHEN i.Code IN ('overnight-security','named-drivers-only','any-driver-minimum-licence') THEN 'warranty'
                    WHEN i.Code IN ('revise-stock-limit','revise-vehicle-limit') THEN 'risk-change' ELSE 'documentary' END
                OR (e.Outcome='query' AND i.Kind<>'documentary'))
              THROW 51361,'Condition requires an exact current decision definition and owner.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId WHERE
                (SELECT COUNT(*) FROM OPENJSON(i.EffectiveDatesJson)) NOT BETWEEN 1 AND 100
                OR EXISTS(SELECT 1 FROM OPENJSON(i.EffectiveDatesJson) j WHERE j.type<>1
                    OR TRY_CONVERT(datetimeoffset,j.value) IS NULL OR DATEPART(TZOFFSET,TRY_CONVERT(datetimeoffset,j.value))<>0
                    OR NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                        WHERE TRY_CONVERT(datetimeoffset,JSON_VALUE(s.value,'$.effectiveAt'))=TRY_CONVERT(datetimeoffset,j.value)))
                OR EXISTS(SELECT 1 FROM OPENJSON(i.EffectiveDatesJson) j GROUP BY TRY_CONVERT(datetimeoffset,j.value) HAVING COUNT(*)>1))
              THROW 51362,'Condition dates must be distinct UTC dates from the owned cumulative rating.',1;
            END;
            """);
    }
}
