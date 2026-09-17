using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingRatingCycleStorage
{
    private static void AddCycleGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCycle_Immutable ON ServicingCycle AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted) AND (NOT EXISTS(SELECT 1 FROM inserted)
              OR UPDATE(Id) OR UPDATE(DraftId) OR UPDATE(PolicyId) OR UPDATE(BaseTermId) OR UPDATE(BaseVersionId)
              OR UPDATE(RevisionId) OR UPDATE(ProductId) OR UPDATE(ProductVersionId) OR UPDATE(AgencyTermsVersionId)
              OR UPDATE(RatingRuleVersionId) OR UPDATE(BinderVersionId) OR UPDATE(AuthorityVersionId)
              OR UPDATE(RuntimeVersionId) OR UPDATE(ScenarioVersionId) OR UPDATE(Sequence) OR UPDATE(WorkId)
              OR UPDATE(InputHash) OR UPDATE(InputJson) OR UPDATE(RequestedBy) OR UPDATE(CreatedBy) OR UPDATE(CreatedAt))
              THROW 51220,'Servicing cycle input and provenance are immutable.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE d.State='superseded'
              AND (i.State<>d.State OR i.SupersededAt<>d.SupersededAt OR i.SupersededReason<>d.SupersededReason))
              THROW 51221,'Superseded servicing cycles cannot regain authority.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId WHERE i.State='rated'
              AND (d.State<>'draft' OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>i.Id))
              THROW 51222,'Only the current active revision and cycle may become rated.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCycle_Source ON ServicingCycle AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId
              JOIN Policy p ON p.Id=i.PolicyId JOIN PolicyTerm t ON t.Id=i.BaseTermId
              JOIN AgencyTermsVersion a ON a.Id=i.AgencyTermsVersionId JOIN OutboxWork w ON w.Id=i.WorkId
              WHERE d.State<>'draft' OR d.Kind NOT IN ('adjustment','renewal') OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
                OR i.State<>'rating-pending' OR a.AgencyId<>p.AgencyId
                OR (d.Kind='adjustment' AND t.ProductVersionId<>i.ProductVersionId)
                OR w.Kind<>'servicing-rating' OR w.SubjectRecordId IS NULL OR w.SubjectRecordId<>i.Id
                OR w.ScenarioVersionId IS NULL OR w.ScenarioVersionId<>i.ScenarioVersionId OR w.OperationKey<>'servicing-rating/'+LOWER(REPLACE(CONVERT(varchar(36),i.Id),'-',''))
                OR i.InputHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.InputJson COLLATE Latin1_General_100_BIN2_UTF8))
                OR COALESCE(JSON_VALUE(i.InputJson,'$.format'),'')<>'servicing-rating-input-1')
              THROW 51223,'Cycle requires active owned revision, exact input and durable work provenance.',1;
            END;
            """);
        // Compare every persisted pin to the hashed envelope. FK validity alone
        // must not permit replacing a valid version with another valid version.
        var pins = new[] { "DraftId", "RevisionId", "BaseVersionId", "ProductVersionId", "AgencyTermsVersionId",
            "RatingRuleVersionId", "BinderVersionId", "AuthorityVersionId", "RuntimeVersionId", "ScenarioVersionId" };
        var mismatch = string.Join(" OR ", pins.Select(name =>
            $"COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.{char.ToLowerInvariant(name[0])}{name[1..]}')),'00000000-0000-0000-0000-000000000000')<>i.[{name}]"));
        migration.Sql($"CREATE TRIGGER TR_ServicingCycle_Pins ON ServicingCycle AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i WHERE {mismatch}) THROW 51224,'Cycle pins must equal the hashed input.',1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_ServicingDraft_Cycle ON ServicingDraft AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF UPDATE(CurrentCycleId) AND EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CurrentCycleId
              LEFT JOIN deleted d ON d.Id=i.Id LEFT JOIN ServicingCycle previous ON previous.Id=d.CurrentCycleId
              WHERE i.State<>'draft' OR i.CurrentRevisionId IS NULL OR c.RevisionId<>i.CurrentRevisionId OR c.State='superseded'
                OR (previous.Id IS NOT NULL AND c.Sequence<previous.Sequence))
              THROW 51225,'Current servicing cycle must be an eligible revision and cannot move backwards.',1;
            END;
            """);
    }
}
