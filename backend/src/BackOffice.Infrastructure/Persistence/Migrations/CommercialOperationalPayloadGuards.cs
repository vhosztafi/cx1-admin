using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CommercialOperationalPayloads
{
    private static void AddDocumentGuards(MigrationBuilder migration)
    {
        var common = new[] { "format", "policyId", "versionId", "sourceContentHash", "kind", "effectiveAt", "insured", "term", "endorsements", "warranties" };
        var schedule = new[] { "sections", "locations", "business", "wages", "liability", "premium", "businessInterruption" };
        var statement = new[] { "declarations", "cover" };
        var certificate = new[] { "section", "employersReferenceNumber" };
        static string Keys(IEnumerable<string> keys) => string.Join(",", keys.Select(x => "N'" + x + "'"));
        // These are trusted schema field names. Values always come from the immutable version.
        static string EqualObject(string output, string input) => $"COALESCE(JSON_QUERY(i.PayloadJson,'$.commercial.{output}'),'') COLLATE Latin1_General_100_BIN2 <> COALESCE(JSON_QUERY(v.SnapshotJson,'$.{input}'),'') COLLATE Latin1_General_100_BIN2";
        var shared = string.Join(" OR ", new[] { ("insured", "insured"), ("term", "term"), ("endorsements", "cover.endorsements"), ("warranties", "cover.warranties") }.Select(x => EqualObject(x.Item1, x.Item2)));
        var scheduled = string.Join(" OR ", new[] { ("sections", "cover.sections"), ("locations", "risk.locations"), ("business", "risk.business"), ("wages", "risk.wages"), ("liability", "risk.liability"), ("premium", "premium"), ("businessInterruption", "risk.businessInterruption") }.Select(x => EqualObject(x.Item1, x.Item2)));
        migration.Sql($"""
            CREATE TRIGGER TR_PolicyDocumentRequest_CommercialPayload ON PolicyDocumentRequest AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyVersion v ON v.Id=i.VersionId JOIN Policy p ON p.Id=i.PolicyId
              JOIN Product product ON product.Id=p.ProductId JOIN TemplateVersion template ON template.Id=i.TemplateVersionId WHERE product.Code='commercial-combined' AND (
              COALESCE(JSON_VALUE(i.PayloadJson,'$.format'),'')<>'policy-document-1'
              OR COALESCE(JSON_VALUE(i.PayloadJson,'$.commercial.format'),'')<>'commercial-document-1'
              OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.requestId')),'00000000-0000-0000-0000-000000000000')<>i.Id
              OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.termId')),'00000000-0000-0000-0000-000000000000')<>i.TermId
              OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.transactionId')),'00000000-0000-0000-0000-000000000000')<>i.TransactionId
              OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.templateVersionId')),'00000000-0000-0000-0000-000000000000')<>i.TemplateVersionId
              OR COALESCE(JSON_VALUE(i.PayloadJson,'$.policyReference'),'')<>p.Reference
              OR COALESCE(JSON_QUERY(i.PayloadJson,'$.template'),'') COLLATE Latin1_General_100_BIN2<>template.ContentJson COLLATE Latin1_General_100_BIN2
              OR (SELECT COUNT(*) FROM OPENJSON(i.PayloadJson))<>13
              OR EXISTS(SELECT 1 FROM OPENJSON(i.PayloadJson) WHERE [key] NOT IN ('format','requestId','policyId','policyReference','termId','transactionId','versionId','contentHash','kind','templateVersionId','template','snapshot','commercial'))
              OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.policyId')),'00000000-0000-0000-0000-000000000000')<>v.PolicyId
              OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.versionId')),'00000000-0000-0000-0000-000000000000')<>v.Id
              OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.commercial.policyId')),'00000000-0000-0000-0000-000000000000')<>v.PolicyId
              OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.commercial.versionId')),'00000000-0000-0000-0000-000000000000')<>v.Id
              OR COALESCE(JSON_VALUE(i.PayloadJson,'$.contentHash'),'') COLLATE Latin1_General_100_BIN2<>LOWER(CONVERT(varchar(64),v.ContentHash,2))
              OR COALESCE(JSON_VALUE(i.PayloadJson,'$.commercial.sourceContentHash'),'') COLLATE Latin1_General_100_BIN2<>LOWER(CONVERT(varchar(64),v.ContentHash,2))
              OR COALESCE(JSON_VALUE(i.PayloadJson,'$.kind'),'')<>i.Kind OR COALESCE(JSON_VALUE(i.PayloadJson,'$.commercial.kind'),'')<>i.Kind
              OR TRY_CONVERT(datetimeoffset,JSON_VALUE(i.PayloadJson,'$.commercial.effectiveAt')) IS NULL
              OR TRY_CONVERT(datetimeoffset,JSON_VALUE(i.PayloadJson,'$.commercial.effectiveAt'))<>v.EffectiveAt
              OR COALESCE(JSON_QUERY(i.PayloadJson,'$.snapshot'),'') COLLATE Latin1_General_100_BIN2<>v.SnapshotJson COLLATE Latin1_General_100_BIN2
              OR EXISTS(SELECT 1 FROM OPENJSON(i.PayloadJson) GROUP BY [key] HAVING COUNT(*)>1)
              OR EXISTS(SELECT 1 FROM OPENJSON(i.PayloadJson,'$.commercial') GROUP BY [key] HAVING COUNT(*)>1)
              OR {shared}
              OR (i.Kind='policy-schedule' AND ({scheduled}
                OR (SELECT COUNT(*) FROM OPENJSON(i.PayloadJson,'$.commercial'))<>16+CASE WHEN JSON_QUERY(v.SnapshotJson,'$.risk.businessInterruption') IS NULL THEN 0 ELSE 1 END
                OR EXISTS(SELECT 1 FROM OPENJSON(i.PayloadJson,'$.commercial') WHERE [key] NOT IN ({Keys(common.Concat(schedule))}))))
              OR (i.Kind='policy-statement' AND ({EqualObject("declarations", "risk")} OR {EqualObject("cover", "cover")}
                OR (SELECT COUNT(*) FROM OPENJSON(i.PayloadJson,'$.commercial'))<>12
                OR EXISTS(SELECT 1 FROM OPENJSON(i.PayloadJson,'$.commercial') WHERE [key] NOT IN ({Keys(common.Concat(statement))}))))
              OR (i.Kind='policy-certificate' AND (
                NOT EXISTS(SELECT 1 FROM OPENJSON(v.SnapshotJson,'$.cover.sections') section WHERE JSON_VALUE(section.value,'$.code')='employers-liability'
                  AND section.value COLLATE Latin1_General_100_BIN2=JSON_QUERY(i.PayloadJson,'$.commercial.section') COLLATE Latin1_General_100_BIN2)
                OR COALESCE(JSON_VALUE(i.PayloadJson,'$.commercial.employersReferenceNumber'),'') COLLATE Latin1_General_100_BIN2<>COALESCE(JSON_VALUE(v.SnapshotJson,'$.risk.liability.employersReferenceNumber'),'') COLLATE Latin1_General_100_BIN2
                OR (SELECT COUNT(*) FROM OPENJSON(i.PayloadJson,'$.commercial'))<>11+CASE WHEN JSON_VALUE(v.SnapshotJson,'$.risk.liability.employersReferenceNumber') IS NULL THEN 0 ELSE 1 END
                OR EXISTS(SELECT 1 FROM OPENJSON(i.PayloadJson,'$.commercial') WHERE [key] NOT IN ({Keys(common.Concat(certificate))}))))
              )) THROW 51970,'Commercial document must retain its exact immutable source and applicable closed content.',1;
            END;
            """);
    }
}
