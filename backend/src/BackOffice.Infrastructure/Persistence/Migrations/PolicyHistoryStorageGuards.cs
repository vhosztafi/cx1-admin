using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class PolicyHistoryStorage
{
    private static void AddHistoryGuards(MigrationBuilder migration)
    {
        foreach(var table in new[]{"PolicyReconstructionRequest","PolicyQuoteClone"})
            migration.Sql($"CREATE TRIGGER TR_{table}_Immutable ON {table} INSTEAD OF UPDATE,DELETE AS BEGIN THROW 51860,'Policy history requests and clone lineage are immutable.',1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_PolicyReconstructionRequest_Source ON PolicyReconstructionRequest AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted r JOIN PolicyTerm term ON term.Id=r.TermId AND term.PolicyId=r.PolicyId
              JOIN OutboxWork w ON w.Id=r.WorkId
              OUTER APPLY(SELECT TOP(1) v.Id,v.ContentHash,t.Kind FROM PolicyVersion v WITH(HOLDLOCK)
                JOIN PolicyTransaction t ON t.Id=v.TransactionId AND t.TermId=v.TermId AND t.PolicyId=v.PolicyId
                WHERE v.TermId=r.TermId AND v.PolicyId=r.PolicyId AND v.ProcessedAt<=r.KnownAt AND t.ProcessedAt<=r.KnownAt
                  AND v.EffectiveAt<term.EndsAt AND (v.EffectiveAt<=r.EffectiveAt OR (r.EffectiveAt<term.StartsAt AND t.Kind IN ('new-business','renewal')))
                ORDER BY CASE WHEN v.EffectiveAt<=r.EffectiveAt THEN 0 ELSE 1 END,v.EffectiveAt DESC,t.Sequence DESC,v.SliceOrdinal DESC,v.Id DESC) selected
              WHERE ISNULL(r.VersionId,'00000000-0000-0000-0000-000000000000')<>ISNULL(selected.Id,'00000000-0000-0000-0000-000000000000')
                OR r.CoverageState<>CASE WHEN selected.Id IS NULL THEN 'not-covered' WHEN r.EffectiveAt<term.StartsAt THEN 'scheduled'
                   WHEN selected.Kind='cancellation' THEN 'cancelled' WHEN r.EffectiveAt>=term.EndsAt THEN 'expired' ELSE 'active' END
                OR w.Kind COLLATE Latin1_General_100_BIN2<>'policy-reconstruction'
                OR w.SubjectRecordId<>r.Id OR w.SubjectRecordId IS NULL
                OR w.OperationKey COLLATE Latin1_General_100_BIN2<>'policy-reconstruction/'+LOWER(REPLACE(CONVERT(varchar(36),r.Id),'-','')) COLLATE Latin1_General_100_BIN2
                OR CONVERT(varbinary(max),w.Payload)<>CONVERT(varbinary(max),r.ManifestJson)
                OR ISNULL(JSON_VALUE(r.ManifestJson,'$.format'),'')<>'policy-reconstruction-1'
                OR ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.ManifestJson,'$.requestId')),'00000000-0000-0000-0000-000000000000')<>r.Id
                OR ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.ManifestJson,'$.policyId')),'00000000-0000-0000-0000-000000000000')<>r.PolicyId
                OR ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.ManifestJson,'$.termId')),'00000000-0000-0000-0000-000000000000')<>r.TermId
                OR ISNULL(TRY_CONVERT(datetimeoffset,JSON_VALUE(r.ManifestJson,'$.effectiveAt')),'0001-01-01')<>r.EffectiveAt
                OR ISNULL(TRY_CONVERT(datetimeoffset,JSON_VALUE(r.ManifestJson,'$.knownAt')),'0001-01-01')<>r.KnownAt
                OR ISNULL(JSON_VALUE(r.ManifestJson,'$.coverageState'),'')<>r.CoverageState
                OR ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.ManifestJson,'$.versionId')),'00000000-0000-0000-0000-000000000000')<>ISNULL(r.VersionId,'00000000-0000-0000-0000-000000000000')
                OR ISNULL(TRY_CONVERT(binary(32),JSON_VALUE(r.ManifestJson,'$.contentHash'),2),0x00)<>ISNULL(r.VersionHash,0x00))
              THROW 51861,'Reconstruction must retain the exact owned cutoff selection and durable manifest.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyQuoteClone_Source ON PolicyQuoteClone AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted lineage JOIN Policy p ON p.Id=lineage.PolicyId
              JOIN Quote q ON q.Id=lineage.QuoteId JOIN QuoteRevision r ON r.Id=lineage.RevisionId AND r.QuoteId=q.Id
              WHERE q.ClientId<>p.ClientId OR q.AgencyId<>p.AgencyId OR q.ProductId<>p.ProductId
                OR q.State<>'draft' OR q.CurrentRevisionId<>r.Id OR q.CurrentRevisionId IS NULL OR q.CurrentUnderwritingCycleId IS NOT NULL
                OR q.BoundPolicyId IS NOT NULL OR q.CaptureClosedAt IS NOT NULL OR q.ClonedFromQuoteRevisionId IS NOT NULL
                OR r.Number<>1 OR q.CreatedBy IS NULL OR q.CreatedBy<>lineage.ActorId OR r.SavedBy<>lineage.ActorId
                OR JSON_PATH_EXISTS(r.ProposalJson,'$.termIntent')=1
                OR EXISTS(SELECT 1 FROM OPENJSON(lineage.ItemMapJson) m WHERE TRY_CONVERT(uniqueidentifier,m.[key]) IS NULL OR TRY_CONVERT(uniqueidentifier,m.value) IS NULL
                  OR TRY_CONVERT(uniqueidentifier,m.[key])=TRY_CONVERT(uniqueidentifier,m.value))
                OR (SELECT COUNT(*) FROM OPENJSON(lineage.ItemMapJson))<>(SELECT COUNT(DISTINCT value) FROM OPENJSON(lineage.ItemMapJson)))
              THROW 51862,'Policy clone lineage requires a fresh incomplete quote for the same client and agency with remapped identities.',1;
            END;
            """);
    }
    private static void RemoveHistoryGuards(MigrationBuilder migration)
    {
        migration.Sql("IF EXISTS(SELECT 1 FROM PolicyReconstructionRequest) OR EXISTS(SELECT 1 FROM PolicyQuoteClone) THROW 51863,'Retained policy history requests cannot be downgraded.',1;");
        foreach(var table in new[]{"PolicyReconstructionRequest","PolicyQuoteClone"})
        {migration.Sql($"DROP TRIGGER TR_{table}_Source;");migration.Sql($"DROP TRIGGER TR_{table}_Immutable;");}
    }
}
