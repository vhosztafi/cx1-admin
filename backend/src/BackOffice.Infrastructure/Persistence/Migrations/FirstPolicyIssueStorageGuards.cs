using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class FirstPolicyIssueStorage
{
    private static void AddPolicyGuards(MigrationBuilder migration)
    {
        foreach (var table in new[] { "PolicyTransaction", "PolicyVersion", "PolicyRegistration", "IssueFinancialObligation", "IssueFinancialComponent", "JournalLine" })
            migration.Sql($"CREATE TRIGGER TR_{table}_AppendOnly ON [{table}] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51180, 'Issued history is append-only.', 1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_Policy_Owner ON Policy AFTER INSERT,UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted) AND (NOT EXISTS(SELECT 1 FROM inserted) OR UPDATE(SourceQuoteId) OR UPDATE(AgencyId) OR UPDATE(ClientId) OR UPDATE(RelationshipId) OR UPDATE(ProductId) OR UPDATE(Number) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy))
              THROW 51181,'Policy source and ownership are immutable.',1;
            IF EXISTS(SELECT 1 FROM inserted p JOIN Quote q ON q.Id=p.SourceQuoteId WHERE p.ClientId<>q.ClientId OR p.RelationshipId<>q.RelationshipId)
              THROW 51181,'Policy must retain its source quote ownership.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyTerm_Immutable ON PolicyTerm AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF NOT EXISTS(SELECT 1 FROM inserted) OR UPDATE(Id) OR UPDATE(PolicyId) OR UPDATE(Number) OR UPDATE(StartsAt) OR UPDATE(EndsAt) OR UPDATE(LocalTermIntentJson) OR UPDATE(ProductId) OR UPDATE(ProductVersionId) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy)
              THROW 51182,'Policy term provenance is immutable.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyTransaction_Source ON PolicyTransaction AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN QuoteAcceptance a ON a.Id=i.AcceptanceId JOIN UnderwritingCycle c ON c.Id=i.CycleId JOIN PolicyTerm t ON t.Id=i.TermId
              WHERE a.RatingId<>i.RatingId OR c.QuoteRevisionId<>i.QuoteRevisionId OR c.ProductVersionId<>t.ProductVersionId OR c.StartsAt<>t.StartsAt OR c.EndsAt<>t.EndsAt OR i.EffectiveAt<>t.StartsAt)
              THROW 51183,'First issue must retain exact accepted rating, revision and term.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyVersion_Source ON PolicyVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted v JOIN PolicyTransaction t ON t.Id=v.TransactionId JOIN Policy p ON p.Id=v.PolicyId JOIN PolicyTerm pt ON pt.Id=v.TermId JOIN Product product ON product.Id=p.ProductId
              WHERE v.EffectiveAt<>t.EffectiveAt OR v.ProcessedAt<>t.ProcessedAt OR v.ContentHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),v.SnapshotJson COLLATE Latin1_General_100_BIN2_UTF8))
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.productVersionId')),'00000000-0000-0000-0000-000000000000')<>pt.ProductVersionId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.insured.clientId')),'00000000-0000-0000-0000-000000000000')<>p.ClientId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.insured.clientAgencyRelationshipId')),'00000000-0000-0000-0000-000000000000')<>p.RelationshipId
                OR COALESCE(JSON_VALUE(v.SnapshotJson,'$.productCode'),'')<>product.Code
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.term.startsAt')) IS NULL OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.term.startsAt'))<>pt.StartsAt
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.term.endsAt')) IS NULL OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.term.endsAt'))<>pt.EndsAt)
              THROW 51184,'Issued snapshot must agree with source, coverage and content hash.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyRegistration_Source ON PolicyRegistration AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyVersion v ON v.Id=i.VersionId WHERE NOT EXISTS(
              SELECT 1 FROM OPENJSON(v.SnapshotJson,'$.risk.vehicles') WITH(id uniqueidentifier '$.id',registration nvarchar(100) '$.registration') vehicle
              WHERE vehicle.id=i.RiskItemId AND UPPER(REPLACE(REPLACE(vehicle.registration,' ',''),'-',''))=i.NormalizedRegistration))
              THROW 51185,'Registration must identify a vehicle in the retained snapshot.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyDocumentRequest_Source ON PolicyDocumentRequest AFTER INSERT,UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted) THROW 51186,'Requested policy document payload is immutable.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN Policy p ON p.Id=i.PolicyId JOIN TemplateVersion t ON t.Id=i.TemplateVersionId JOIN OutboxWork w ON w.Id=i.WorkId
              WHERE t.ProductId<>p.ProductId OR t.Kind<>i.Kind OR t.State<>'published' OR i.CreatedAt<t.EffectiveFrom OR i.CreatedAt>=t.EffectiveTo
                OR i.PayloadHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.PayloadJson COLLATE Latin1_General_100_BIN2_UTF8))
                OR CONVERT(varbinary(max),w.Payload)<>CONVERT(varbinary(max),i.PayloadJson) OR w.Kind<>'policy-document' OR w.SubjectRecordId<>i.Id OR w.OperationKey<>'policy-document/'+LOWER(REPLACE(CONVERT(varchar(36),i.Id),'-','')))
              THROW 51186,'Document request requires its exact product template, payload and durable work.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_Quote_BoundPolicy ON Quote AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.BoundPolicyId IS NOT NULL AND
              (i.BoundPolicyId IS NULL OR i.BoundPolicyId<>d.BoundPolicyId OR i.State<>'bound' OR i.ClientId<>d.ClientId OR i.RelationshipId<>d.RelationshipId))
              THROW 51187,'Bound quote ownership and policy pointer are final.',1;
            IF EXISTS(SELECT 1 FROM inserted q WHERE q.BoundPolicyId IS NOT NULL AND NOT EXISTS(
              SELECT 1 FROM Policy p JOIN PolicyTerm t ON t.Id=p.CurrentTermId JOIN PolicyVersion v ON v.Id=t.CurrentVersionId
              JOIN PolicyTransaction tr ON tr.Id=v.TransactionId JOIN IssueFinancialObligation o ON o.TransactionId=tr.Id
              JOIN Journal j ON j.ObligationId=o.Id AND j.PostedAt IS NOT NULL
              WHERE p.Id=q.BoundPolicyId AND p.SourceQuoteId=q.Id AND tr.SourceQuoteId=q.Id AND tr.Kind='new-business'
                AND (SELECT COUNT(DISTINCT d.Kind) FROM PolicyDocumentRequest d WHERE d.VersionId=v.Id)=3))
              THROW 51187,'Bound quote requires a complete policy, posted obligation and three document requests.',1;
            END;
            """);
        AddIssuePostingGuards(migration);
    }
    private static void DropPolicyGuards(MigrationBuilder migration)
    {
        foreach (var name in new[] { "TR_Quote_BoundPolicy", "TR_PolicyDocumentRequest_Source", "TR_PolicyRegistration_Source", "TR_PolicyVersion_Source", "TR_PolicyTransaction_Source", "TR_PolicyTerm_Immutable", "TR_Policy_Owner", "TR_IssueFinancialObligation_Source", "TR_IssueFinancialComponent_Source", "TR_JournalLine_Source", "TR_Journal_Posting" }) migration.Sql($"DROP TRIGGER IF EXISTS {name};");
        foreach (var table in new[] { "PolicyTransaction", "PolicyVersion", "PolicyRegistration", "IssueFinancialObligation", "IssueFinancialComponent", "JournalLine" }) migration.Sql($"DROP TRIGGER IF EXISTS TR_{table}_AppendOnly;");
    }
}
