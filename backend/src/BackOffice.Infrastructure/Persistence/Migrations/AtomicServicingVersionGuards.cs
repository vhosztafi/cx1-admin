using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static partial class AtomicServicingIssueGuards
{
    private static void AddVersionAndQuoteGuards(MigrationBuilder migration)
    {
        VersionSource(migration,true); BoundQuote(migration,true);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyVersion_Servicing ON PolicyVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted v JOIN PolicyTransaction t ON t.Id=v.TransactionId
              JOIN ServicingIssueDecision d ON d.Id=t.ServicingIssueDecisionId JOIN ServicingCycle c ON c.Id=d.CycleId
              WHERE COALESCE(JSON_VALUE(v.SnapshotJson,'$.snapshotFormat'),'')<>'issued-servicing-1'
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.servicingIssueDecisionId')),'00000000-0000-0000-0000-000000000000')<>d.Id
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.sourceQuoteId')),'00000000-0000-0000-0000-000000000000')<>t.SourceQuoteId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.baseVersionId')),'00000000-0000-0000-0000-000000000000')<>d.BaseVersionId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.revisionId')),'00000000-0000-0000-0000-000000000000')<>d.RevisionId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.transactionId')),'00000000-0000-0000-0000-000000000000')<>t.Id
                OR COALESCE(TRY_CONVERT(int,JSON_VALUE(v.SnapshotJson,'$.provenance.sliceOrdinal')),0)<>v.SliceOrdinal
                OR COALESCE(JSON_VALUE(v.SnapshotJson,'$.provenance.inputHash'),'')<>LOWER(CONVERT(varchar(64),d.InputHash,2))
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.effectiveAt')) IS NULL
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.effectiveAt'))<>v.EffectiveAt
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.processedAt')) IS NULL
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.processedAt'))<>v.ProcessedAt
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                  WHERE CONVERT(int,s.[key])+1=v.SliceOrdinal AND TRY_CONVERT(datetimeoffset,JSON_VALUE(s.value,'$.effectiveAt'))=v.EffectiveAt))
              THROW 51526,'Each servicing version must retain its exact approved slice and immutable decision provenance.',1;
            END;
            """);
    }

    private static void VersionSource(MigrationBuilder migration,bool servicing)
    {
        var dateCheck = servicing ? "(t.Kind='new-business' AND v.EffectiveAt<>t.EffectiveAt)" : "v.EffectiveAt<>t.EffectiveAt";
        migration.Sql($"""
            ALTER TRIGGER TR_PolicyVersion_Source ON PolicyVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted v JOIN PolicyTransaction t ON t.Id=v.TransactionId JOIN Policy p ON p.Id=v.PolicyId JOIN PolicyTerm pt ON pt.Id=v.TermId JOIN Product product ON product.Id=p.ProductId
              WHERE {dateCheck} OR v.ProcessedAt<>t.ProcessedAt OR v.ContentHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),v.SnapshotJson COLLATE Latin1_General_100_BIN2_UTF8))
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.productVersionId')),'00000000-0000-0000-0000-000000000000')<>pt.ProductVersionId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.insured.clientId')),'00000000-0000-0000-0000-000000000000')<>p.ClientId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.insured.clientAgencyRelationshipId')),'00000000-0000-0000-0000-000000000000')<>p.RelationshipId
                OR COALESCE(JSON_VALUE(v.SnapshotJson,'$.productCode'),'')<>product.Code
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.term.startsAt')) IS NULL OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.term.startsAt'))<>pt.StartsAt
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.term.endsAt')) IS NULL OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.term.endsAt'))<>pt.EndsAt)
              THROW 51184,'Issued snapshot must agree with source, coverage and content hash.',1;
            END;
            """);
    }

    private static void BoundQuote(MigrationBuilder migration,bool servicing)
    {
        var graph = servicing
            ? "Policy p JOIN PolicyTerm t ON t.PolicyId=p.Id AND t.Number=1 JOIN PolicyVersion v ON v.TermId=t.Id AND v.Sequence=1 AND v.SliceOrdinal=1"
            : "Policy p JOIN PolicyTerm t ON t.Id=p.CurrentTermId JOIN PolicyVersion v ON v.Id=t.CurrentVersionId";
        migration.Sql($"""
            ALTER TRIGGER TR_Quote_BoundPolicy ON Quote AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.BoundPolicyId IS NOT NULL AND
              (i.BoundPolicyId IS NULL OR i.BoundPolicyId<>d.BoundPolicyId OR i.State<>'bound' OR i.ClientId<>d.ClientId OR i.RelationshipId<>d.RelationshipId))
              THROW 51187,'Bound quote ownership and policy pointer are final.',1;
            IF EXISTS(SELECT 1 FROM inserted q WHERE q.BoundPolicyId IS NOT NULL AND NOT EXISTS(
              SELECT 1 FROM {graph}
              JOIN PolicyTransaction tr ON tr.Id=v.TransactionId JOIN IssueFinancialObligation o ON o.TransactionId=tr.Id
              JOIN Journal j ON j.ObligationId=o.Id AND j.PostedAt IS NOT NULL
              WHERE p.Id=q.BoundPolicyId AND p.SourceQuoteId=q.Id AND tr.SourceQuoteId=q.Id AND tr.Kind='new-business'
                AND (SELECT COUNT(DISTINCT d.Kind) FROM PolicyDocumentRequest d WHERE d.VersionId=v.Id)=3))
              THROW 51187,'Bound quote requires a complete policy, posted obligation and three document requests.',1;
            END;
            """);
    }

    internal static void Down(MigrationBuilder migration)
    {
        migration.Sql("IF EXISTS(SELECT 1 FROM ServicingIssueDecision) THROW 51527,'Issued servicing decisions cannot be downgraded.',1;");
        foreach(var name in new[]{"TR_ServicingIssueDecision_AppendOnly","TR_PolicyMidIntent_AppendOnly","TR_ServicingIssueDecision_Source",
            "TR_PolicyTransaction_IssueDecision","TR_PolicyDocumentRequest_Purpose","TR_PolicyMidIntent_Source","TR_ServicingDraft_Issued","TR_PolicyVersion_Servicing"})
            migration.Sql($"DROP TRIGGER IF EXISTS {name};");
        VersionSource(migration,false); BoundQuote(migration,false);
    }
}
