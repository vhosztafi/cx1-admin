using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingCarrierConditions
{
    private static void AddCarrierConditionGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacityCondition_Source ON ServicingCapacityCondition AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(
                SELECT 1 FROM ServicingCapacityResponse r JOIN ServicingCapacityCase k WITH(UPDLOCK,HOLDLOCK) ON k.Id=r.CaseId
                WHERE r.Id=i.ResponseId AND r.Outcome='approve-with-conditions' AND r.ApplicationState='applied'
                    AND k.CurrentSubmissionId=r.SubmissionId AND k.State NOT IN ('draft','superseded')
                    AND (k.CurrentResponseId IS NULL OR k.CurrentResponseId<>r.Id)
                    AND i.CreatedBy=r.RecordedBy AND i.CreatedAt=r.RecordedAt
                    AND r.Sequence=(SELECT MAX(x.Sequence) FROM ServicingCapacityResponse x WHERE x.CaseId=k.Id AND x.ApplicationState='applied')
                    AND JSON_QUERY(r.DefinitionJson,'$.conditions['+CONVERT(varchar(10),i.Sequence-1)+'].definition') COLLATE Latin1_General_100_BIN2=i.DefinitionJson COLLATE Latin1_General_100_BIN2
                    AND JSON_QUERY(r.DefinitionJson,'$.conditions['+CONVERT(varchar(10),i.Sequence-1)+'].effectiveDates') COLLATE Latin1_General_100_BIN2=i.EffectiveDatesJson COLLATE Latin1_General_100_BIN2
                    AND i.Kind=CASE WHEN i.Code IN ('overnight-security','named-drivers-only','any-driver-minimum-licence') THEN 'warranty'
                        WHEN i.Code IN ('revise-stock-limit','revise-vehicle-limit') THEN 'risk-change' ELSE 'documentary' END))
                THROW 51480,'Carrier condition requires the exact latest response manifest before activation.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId WHERE
                (SELECT COUNT(*) FROM OPENJSON(i.EffectiveDatesJson)) NOT BETWEEN 1 AND 100
                OR EXISTS(SELECT 1 FROM OPENJSON(i.EffectiveDatesJson) j WHERE j.type<>1
                    OR TRY_CONVERT(datetimeoffset,j.value) IS NULL OR DATEPART(TZOFFSET,TRY_CONVERT(datetimeoffset,j.value))<>0
                    OR NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                        WHERE TRY_CONVERT(datetimeoffset,JSON_VALUE(s.value,'$.effectiveAt'))=TRY_CONVERT(datetimeoffset,j.value)))
                OR EXISTS(SELECT 1 FROM OPENJSON(i.EffectiveDatesJson) a JOIN OPENJSON(i.EffectiveDatesJson) b ON TRY_CONVERT(int,b.[key])=TRY_CONVERT(int,a.[key])+1
                    WHERE TRY_CONVERT(datetimeoffset,a.value)>=TRY_CONVERT(datetimeoffset,b.value)))
                THROW 51480,'Carrier condition dates must be exact ordered UTC dates from the retained schedule.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacityCondition_Immutable ON ServicingCapacityCondition AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51481,'Carrier condition definitions are immutable.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCapacityCase_Conditions ON ServicingCapacityCase AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN ServicingCapacityResponse r ON r.Id=i.CurrentResponseId
                WHERE i.State='conditional' AND (r.Id IS NULL OR r.Outcome<>'approve-with-conditions'
                    OR (SELECT COUNT(*) FROM ServicingCapacityCondition c WHERE c.ResponseId=r.Id)
                       <>(SELECT COUNT(*) FROM OPENJSON(r.DefinitionJson,'$.conditions'))))
                THROW 51482,'Conditional capacity requires every immutable carrier condition before activation.',1;
            END;
            """);
        EnableConditionalCaseGuard(migration);
    }
}
