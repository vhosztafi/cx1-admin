using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CommercialAtomicIssue
{
    private static void AddCommercialIssueGuards(MigrationBuilder migration)
    {
        foreach (var table in new[] { "CommercialExposureVersion", "CommercialExposureLimitVersion", "CommercialExposureIssueDecision" })
            migration.Sql($"CREATE TRIGGER TR_{table}_Fence ON [{table}] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF COALESCE(APPLOCK_MODE('public','CoverMGA.CommercialExposure','Transaction'),'')<>'Exclusive' THROW 51920,'Commercial exposure effects require the common transaction fence.',1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_CommercialExposureIssueDecision_AppendOnly ON CommercialExposureIssueDecision AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51921,'Commercial issue capacity decisions are immutable.',1; END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CommercialExposureIssueDecision_Source ON CommercialExposureIssueDecision AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
              WHERE i.DecisionHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.DecisionJson COLLATE Latin1_General_100_BIN2_UTF8))
                OR COALESCE(JSON_VALUE(i.DecisionJson,'$.format'),'')<>'commercial-exposure-decision-1'
                OR v.TransactionKind<>'new-business' OR i.AssessedAt<>v.ProcessedAt OR i.CreatedBy<>v.CreatedBy)
              THROW 51921,'Capacity decision requires its exact issued source and hash.',1;
            IF EXISTS(SELECT i.Id,v.BookId,v.PolicyId,v.VersionId,v.SourceHash,v.TermStartsAt,v.TermEndsAt,i.AssessedAt
              FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
              EXCEPT SELECT i.Id,j.bookId,j.policyId,j.versionId,TRY_CONVERT(binary(32),j.sourceHash,2),j.[from],j.[to],j.assessedAt
              FROM inserted i CROSS APPLY OPENJSON(i.DecisionJson) WITH(bookId uniqueidentifier,policyId uniqueidentifier,versionId uniqueidentifier,
                sourceHash varchar(64),[from] datetimeoffset,[to] datetimeoffset,assessedAt datetimeoffset) j)
              THROW 51921,'Capacity decision metadata must match the retained header.',1;
            IF EXISTS(SELECT 1 FROM inserted i WHERE ISJSON(JSON_QUERY(i.DecisionJson,'$.intervals'),ARRAY)<>1
              OR JSON_QUERY(i.DecisionJson,'$.intervals') IS NULL OR NOT EXISTS(SELECT 1 FROM OPENJSON(i.DecisionJson,'$.intervals')))
              THROW 51921,'Capacity decision needs a complete interval assessment.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
              JOIN PolicyVersion pv ON pv.Id=v.VersionId JOIN BinderVersion b ON b.Id=v.BinderVersionId
              LEFT JOIN UserAuthorityGrant g ON g.Id=TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.authorityGrantId'))
              LEFT JOIN AuthorityVersion a ON a.Id=g.AuthorityVersionId
              WHERE g.Id IS NULL OR g.UserId<>i.CreatedBy OR a.BinderVersionId<>v.BinderVersionId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.authorityVersionId')),'00000000-0000-0000-0000-000000000000')<>a.Id
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(pv.SnapshotJson,'$.provenance.authorityVersionId')),'00000000-0000-0000-0000-000000000000')<>a.Id
                OR g.EffectiveFrom>i.AssessedAt OR g.EffectiveTo<=i.AssessedAt OR g.EffectiveFrom>v.TermStartsAt OR g.EffectiveTo<v.TermEndsAt
                OR (g.RevokedAt IS NOT NULL AND g.RevokedAt<=i.AssessedAt)
                OR COALESCE(TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.actorDistrictLimit')),-1)<>TRY_CONVERT(decimal(38,2),JSON_VALUE(a.DefinitionJson,'$.limits.districtProperty'))
                OR COALESCE(TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.binderDistrictLimit')),-1)<>TRY_CONVERT(decimal(38,2),JSON_VALUE(b.DefinitionJson,'$.limits.districtProperty')))
              THROW 51921,'Capacity decision must retain the actual actor and binder district authority.',1;
            DECLARE @interval TABLE(DecisionId uniqueidentifier,ExposureId uniqueidentifier,District nvarchar(4) COLLATE Latin1_General_100_BIN2,StartsAt datetimeoffset,EndsAt datetimeoffset,
              Proposed decimal(38,2),OtherSum decimal(38,2),Resulting decimal(38,2),PolicyCount int,LimitId uniqueidentifier,LimitHash binary(32),
              LimitAmount decimal(38,2),Headroom decimal(38,2),Blocker nvarchar(100));
            INSERT @interval SELECT i.Id,i.ExposureVersionId,j.district,j.[from],j.[to],j.proposedPropertySum,j.otherPropertySum,j.resultingPropertySum,j.policyCount,
              j.limitVersionId,TRY_CONVERT(binary(32),j.limitHash,2),j.limit,j.headroom,j.blocker FROM inserted i CROSS APPLY OPENJSON(i.DecisionJson,'$.intervals')
              WITH(district nvarchar(4),[from] datetimeoffset,[to] datetimeoffset,proposedPropertySum decimal(38,2),otherPropertySum decimal(38,2),
                resultingPropertySum decimal(38,2),policyCount int,limitVersionId uniqueidentifier,limitHash varchar(64),limit decimal(38,2),headroom decimal(38,2),blocker nvarchar(100)) j;
            IF EXISTS(SELECT 1 FROM @interval r JOIN inserted i ON i.Id=r.DecisionId JOIN CommercialExposureVersion v ON v.Id=r.ExposureId
              LEFT JOIN CommercialExposureLimitVersion l ON l.Id=r.LimitId
              WHERE r.District IS NULL OR r.StartsAt IS NULL OR r.EndsAt IS NULL OR r.StartsAt>=r.EndsAt OR r.StartsAt<v.TermStartsAt OR r.EndsAt>v.TermEndsAt
                OR r.Proposed IS NULL OR r.OtherSum IS NULL OR r.Resulting IS NULL OR r.Headroom IS NULL OR r.LimitAmount IS NULL OR r.LimitHash IS NULL
                OR r.PolicyCount IS NULL OR r.PolicyCount<0 OR r.Proposed<0 OR r.OtherSum<0 OR r.Headroom<0 OR r.Resulting<>r.Proposed+r.OtherSum
                OR r.Headroom<>r.LimitAmount-r.Resulting OR r.Blocker IS NOT NULL OR l.Id IS NULL OR l.BookId<>v.BookId
                OR r.Resulting>TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.actorDistrictLimit'))
                OR r.Resulting>TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.binderDistrictLimit'))
                OR (l.District<>'*' AND l.District<>r.District) OR l.PublishedAt>i.AssessedAt OR r.StartsAt<l.EffectiveFrom OR r.EndsAt>l.EffectiveTo
                OR r.LimitHash<>l.ContentHash OR r.LimitAmount<>l.Amount
                OR r.Proposed<>COALESCE((SELECT SUM(c.SumInsured) FROM CommercialExposureLocation c WHERE c.ExposureVersionId=v.Id AND c.District=r.District),0))
              THROW 51921,'Capacity decision needs exact own exposure and applicable pinned limits.',1;
            IF EXISTS(SELECT i.Id,COALESCE(c.District,'*') FROM inserted i LEFT JOIN CommercialExposureLocation c ON c.ExposureVersionId=i.ExposureVersionId
              EXCEPT SELECT DecisionId,District FROM @interval)
              OR EXISTS(SELECT DecisionId,District FROM @interval EXCEPT
                SELECT i.Id,COALESCE(c.District,'*') FROM inserted i LEFT JOIN CommercialExposureLocation c ON c.ExposureVersionId=i.ExposureVersionId)
              THROW 51921,'Capacity decision must include every own district.',1;
            IF EXISTS(SELECT 1 FROM (SELECT r.*,LAG(r.EndsAt,1,v.TermStartsAt) OVER(PARTITION BY r.DecisionId,r.District ORDER BY r.StartsAt) AS PreviousEnd
              FROM @interval r JOIN CommercialExposureVersion v ON v.Id=r.ExposureId) ordered WHERE StartsAt<>PreviousEnd)
              OR EXISTS(SELECT r.DecisionId,r.District FROM @interval r JOIN CommercialExposureVersion v ON v.Id=r.ExposureId
                GROUP BY r.DecisionId,r.District,v.TermEndsAt HAVING MAX(r.EndsAt)<>v.TermEndsAt)
              THROW 51921,'Capacity decision intervals must cover the complete issued term without gaps.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_Policy_ReferencePrefix ON Policy AFTER INSERT,UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted) AND UPDATE(ReferencePrefix) THROW 51922,'Policy reference prefix is immutable.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN Product p ON p.Id=i.ProductId LEFT JOIN deleted d ON d.Id=i.Id
              WHERE d.Id IS NULL AND i.ReferencePrefix<>CASE WHEN p.Code='commercial-combined' THEN 'PL-CC-' ELSE 'PL-MT-' END)
              THROW 51922,'New policy reference must match its product.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyDocumentRequest_CommercialCover ON PolicyDocumentRequest AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyVersion v ON v.Id=i.VersionId JOIN Policy p ON p.Id=i.PolicyId JOIN Product product ON product.Id=p.ProductId
              WHERE product.Code='commercial-combined' AND i.Kind='policy-certificate' AND NOT EXISTS(
                SELECT 1 FROM OPENJSON(v.SnapshotJson,'$.cover.sections') WITH(code nvarchar(100)) section WHERE section.code='employers-liability'))
              THROW 51923,'Commercial EL certificate requires selected employers liability cover.',1;
            END;
            """);
        BoundCommercialQuote(migration, true);
    }

    private static void BoundCommercialQuote(MigrationBuilder migration, bool commercial)
    {
        var complete = commercial ? """
            AND ((product.Code<>'commercial-combined' AND (SELECT COUNT(DISTINCT d.Kind) FROM PolicyDocumentRequest d WHERE d.VersionId=v.Id)=3)
              OR (product.Code='commercial-combined'
                AND EXISTS(SELECT 1 FROM CommercialExposureVersion e JOIN CommercialExposureIssueDecision decision ON decision.ExposureVersionId=e.Id WHERE e.VersionId=v.Id)
                AND EXISTS(SELECT 1 FROM PolicyDocumentRequest d WHERE d.VersionId=v.Id AND d.Kind='policy-schedule')
                AND EXISTS(SELECT 1 FROM PolicyDocumentRequest d WHERE d.VersionId=v.Id AND d.Kind='policy-statement')
                AND (SELECT COUNT(*) FROM PolicyDocumentRequest d WHERE d.VersionId=v.Id AND d.Kind='policy-certificate')=
                  CASE WHEN EXISTS(SELECT 1 FROM OPENJSON(v.SnapshotJson,'$.cover.sections') WITH(code nvarchar(100)) section WHERE section.code='employers-liability') THEN 1 ELSE 0 END))
            """ : "AND (SELECT COUNT(DISTINCT d.Kind) FROM PolicyDocumentRequest d WHERE d.VersionId=v.Id)=3";
        migration.Sql($"""
            ALTER TRIGGER TR_Quote_BoundPolicy ON Quote AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.BoundPolicyId IS NOT NULL AND
              (i.BoundPolicyId IS NULL OR i.BoundPolicyId<>d.BoundPolicyId OR i.State<>'bound' OR i.ClientId<>d.ClientId OR i.RelationshipId<>d.RelationshipId))
              THROW 51187,'Bound quote ownership and policy pointer are final.',1;
            IF EXISTS(SELECT 1 FROM inserted q WHERE q.BoundPolicyId IS NOT NULL AND NOT EXISTS(
              SELECT 1 FROM Policy p JOIN Product product ON product.Id=p.ProductId JOIN PolicyTerm t ON t.PolicyId=p.Id AND t.Number=1
              JOIN PolicyVersion v ON v.TermId=t.Id AND v.Sequence=1 AND v.SliceOrdinal=1
              JOIN PolicyTransaction tr ON tr.Id=v.TransactionId JOIN IssueFinancialObligation o ON o.TransactionId=tr.Id
              JOIN Journal j ON j.ObligationId=o.Id AND j.PostedAt IS NOT NULL
              WHERE p.Id=q.BoundPolicyId AND p.SourceQuoteId=q.Id AND tr.SourceQuoteId=q.Id AND tr.Kind='new-business' {complete}))
              THROW 51187,'Bound quote requires complete policy, posting, exposure and selected documents.',1;
            END;
            """);
    }

    private static void DropCommercialIssueGuards(MigrationBuilder migration)
    {
        migration.Sql("IF EXISTS(SELECT 1 FROM CommercialExposureIssueDecision) OR EXISTS(SELECT 1 FROM Policy WHERE ReferencePrefix='PL-CC-') THROW 51924,'Commercial issue history cannot be downgraded.',1;");
        BoundCommercialQuote(migration, false);
        foreach (var table in new[] { "CommercialExposureVersion", "CommercialExposureLimitVersion", "CommercialExposureIssueDecision" })
            migration.Sql($"DROP TRIGGER IF EXISTS TR_{table}_Fence;");
        foreach (var name in new[] { "TR_CommercialExposureIssueDecision_AppendOnly", "TR_CommercialExposureIssueDecision_Source", "TR_Policy_ReferencePrefix", "TR_PolicyDocumentRequest_CommercialCover" })
            migration.Sql($"DROP TRIGGER IF EXISTS {name};");
    }
}
