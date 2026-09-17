using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingRatingResultStorage
{
    private static void AddResultGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingRatingResult_AppendOnly ON ServicingRatingResult AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted) THROW 51230,'Servicing rating results are append-only.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingRatingResult_Source ON ServicingRatingResult AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId
              JOIN OutboxWork w ON w.Id=i.WorkId JOIN DemoProviderOperation p ON p.Id=i.ProviderOperationId
              WHERE i.CompletedAt<c.CreatedAt OR p.Kind<>'servicing-rating' OR p.Kind<>w.Kind
                OR p.OperationKey<>w.OperationKey OR p.RequestHash<>i.InputHash
                OR p.ScenarioVersionId<>c.ScenarioVersionId OR w.ScenarioVersionId IS NULL OR w.ScenarioVersionId<>c.ScenarioVersionId
                OR w.SubjectRecordId IS NULL OR w.SubjectRecordId<>c.Id
                OR p.CompletedAt IS NULL OR p.CompletedAt<>i.CompletedAt OR p.Result IS NULL
                OR (i.Outcome='rated' AND p.State<>'succeeded') OR (i.Outcome='rejected' AND p.State<>'rejected')
                OR i.ResultHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.ResultJson COLLATE Latin1_General_100_BIN2_UTF8))
                OR i.ResultHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),p.Result COLLATE Latin1_General_100_BIN2_UTF8))
                OR COALESCE(JSON_VALUE(i.ResultJson,'$.format'),'')<>'servicing-rating-result-1'
                OR COALESCE(JSON_VALUE(i.ResultJson,'$.outcome'),'')<>i.Outcome
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.ResultJson,'$.operationId')),'00000000-0000-0000-0000-000000000000')<>i.ProviderOperationId
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(i.ResultJson,'$.completedAt')) IS NULL
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(i.ResultJson,'$.completedAt'))<>i.CompletedAt
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(i.ResultJson,'$.expiresAt')) IS NULL
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(i.ResultJson,'$.expiresAt'))<>i.ExpiresAt)
              THROW 51231,'Result requires exact cycle input, provider outcome and output hash.',1;
            END;
            """);
        var amounts = new[] { "BaseAnnualPremium", "Premium", "Tax", "Fee", "BrokerCommission", "GrossPayable", "NetDue" };
        var mismatch = string.Join(" OR ", amounts.Select(name =>
        {
            var amount = $"TRY_CONVERT(decimal(19,2),JSON_VALUE(i.ResultJson,'$.rating.{char.ToLowerInvariant(name[0])}{name[1..]}'))";
            return $"{amount} IS NULL OR {amount}<>i.[{name}]";
        }));
        migration.Sql($"CREATE TRIGGER TR_ServicingRatingResult_Amounts ON ServicingRatingResult AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i WHERE i.Outcome='rated' AND ({mismatch})) THROW 51232,'Rated components must equal the retained provider outcome.',1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCycle_Rating ON ServicingCycle AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF UPDATE(CurrentRatingId) AND EXISTS(SELECT 1 FROM inserted i JOIN ServicingRatingResult r ON r.Id=i.CurrentRatingId
              JOIN ServicingDraft d ON d.Id=i.DraftId WHERE i.State<>'rated' OR r.Outcome<>'rated'
                OR d.State<>'draft' OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>i.Id
                OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId)
              THROW 51233,'A historical or rejected result cannot become current rating authority.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingRatingResult r ON r.Id=i.CurrentRatingId
              WHERE i.State='rated' AND r.Outcome<>'rated')
              THROW 51234,'A rejected result cannot grant rated authority.',1;
            END;
            """);
    }
}
