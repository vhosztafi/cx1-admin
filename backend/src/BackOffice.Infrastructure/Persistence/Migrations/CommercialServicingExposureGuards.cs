using Microsoft.EntityFrameworkCore.Migrations;
namespace BackOffice.Infrastructure.Persistence.Migrations;
public partial class CommercialServicingAtomicIssue
{
    private static void AddCommercialServicingExposureGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_CommercialExposureIssueDecision_Source ON CommercialExposureIssueDecision AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId WHERE v.TransactionKind NOT IN ('new-business','adjustment'))
             THROW 51945,'Unsupported commercial exposure decision transaction.',1;
            DECLARE @initial TABLE(Id uniqueidentifier,ExposureVersionId uniqueidentifier,AssessedAt datetimeoffset,DecisionJson nvarchar(max),DecisionHash binary(32),CreatedAt datetimeoffset,CreatedBy uniqueidentifier);
            INSERT @initial SELECT i.Id,i.ExposureVersionId,i.AssessedAt,i.DecisionJson,i.DecisionHash,i.CreatedAt,i.CreatedBy
             FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId WHERE v.TransactionKind='new-business';
            
            IF EXISTS(SELECT 1 FROM @initial i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
              WHERE i.DecisionHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.DecisionJson COLLATE Latin1_General_100_BIN2_UTF8))
                OR COALESCE(JSON_VALUE(i.DecisionJson,'$.format'),'')<>'commercial-exposure-decision-1'
                OR v.TransactionKind<>'new-business' OR i.AssessedAt<>v.ProcessedAt OR i.CreatedBy<>v.CreatedBy)
              THROW 51921,'Capacity decision requires its exact issued source and hash.',1;
            IF EXISTS(SELECT i.Id,v.BookId,v.PolicyId,v.VersionId,v.SourceHash,v.TermStartsAt,v.TermEndsAt,i.AssessedAt
              FROM @initial i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
              EXCEPT SELECT i.Id,j.bookId,j.policyId,j.versionId,TRY_CONVERT(binary(32),j.sourceHash,2),j.[from],j.[to],j.assessedAt
              FROM @initial i CROSS APPLY OPENJSON(i.DecisionJson) WITH(bookId uniqueidentifier,policyId uniqueidentifier,versionId uniqueidentifier,
                sourceHash varchar(64),[from] datetimeoffset,[to] datetimeoffset,assessedAt datetimeoffset) j)
              THROW 51921,'Capacity decision metadata must match the retained header.',1;
            IF EXISTS(SELECT 1 FROM @initial i WHERE ISJSON(JSON_QUERY(i.DecisionJson,'$.intervals'),ARRAY)<>1
              OR JSON_QUERY(i.DecisionJson,'$.intervals') IS NULL OR NOT EXISTS(SELECT 1 FROM OPENJSON(i.DecisionJson,'$.intervals')))
              THROW 51921,'Capacity decision needs a complete interval assessment.',1;
            IF EXISTS(SELECT 1 FROM @initial i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
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
              j.limitVersionId,TRY_CONVERT(binary(32),j.limitHash,2),j.limit,j.headroom,j.blocker FROM @initial i CROSS APPLY OPENJSON(i.DecisionJson,'$.intervals')
              WITH(district nvarchar(4),[from] datetimeoffset,[to] datetimeoffset,proposedPropertySum decimal(38,2),otherPropertySum decimal(38,2),
                resultingPropertySum decimal(38,2),policyCount int,limitVersionId uniqueidentifier,limitHash varchar(64),limit decimal(38,2),headroom decimal(38,2),blocker nvarchar(100)) j;
            IF EXISTS(SELECT 1 FROM @interval r JOIN @initial i ON i.Id=r.DecisionId JOIN CommercialExposureVersion v ON v.Id=r.ExposureId
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
            IF EXISTS(SELECT i.Id,COALESCE(c.District,'*') FROM @initial i LEFT JOIN CommercialExposureLocation c ON c.ExposureVersionId=i.ExposureVersionId
              EXCEPT SELECT DecisionId,District FROM @interval)
              OR EXISTS(SELECT DecisionId,District FROM @interval EXCEPT
                SELECT i.Id,COALESCE(c.District,'*') FROM @initial i LEFT JOIN CommercialExposureLocation c ON c.ExposureVersionId=i.ExposureVersionId)
              THROW 51921,'Capacity decision must include every own district.',1;
            IF EXISTS(SELECT 1 FROM (SELECT r.*,LAG(r.EndsAt,1,v.TermStartsAt) OVER(PARTITION BY r.DecisionId,r.District ORDER BY r.StartsAt) AS PreviousEnd
              FROM @interval r JOIN CommercialExposureVersion v ON v.Id=r.ExposureId) ordered WHERE StartsAt<>PreviousEnd)
              OR EXISTS(SELECT r.DecisionId,r.District FROM @interval r JOIN CommercialExposureVersion v ON v.Id=r.ExposureId
                GROUP BY r.DecisionId,r.District,v.TermEndsAt HAVING MAX(r.EndsAt)<>v.TermEndsAt)
              THROW 51921,'Capacity decision intervals must cover the complete issued term without gaps.',1;
            
            DECLARE @adjustment TABLE(Id uniqueidentifier,ExposureVersionId uniqueidentifier,AssessedAt datetimeoffset,DecisionJson nvarchar(max),DecisionHash binary(32),CreatedBy uniqueidentifier);
            INSERT @adjustment SELECT i.Id,i.ExposureVersionId,i.AssessedAt,i.DecisionJson,i.DecisionHash,i.CreatedBy
             FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId WHERE v.TransactionKind='adjustment';
            IF EXISTS(SELECT 1 FROM @adjustment i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
             JOIN PolicyTransaction t ON t.Id=v.TransactionId JOIN ServicingIssueDecision d ON d.Id=t.ServicingIssueDecisionId
             JOIN ServicingCycle c ON c.Id=d.CycleId JOIN UserAuthorityGrant g ON g.Id=d.GrantId
             JOIN AuthorityVersion a ON a.Id=d.AuthorityVersionId JOIN BinderVersion b ON b.Id=c.BinderVersionId
             WHERE i.DecisionHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.DecisionJson COLLATE Latin1_General_100_BIN2_UTF8))
             OR COALESCE(JSON_VALUE(i.DecisionJson,'$.format'),'')<>'commercial-servicing-exposure-decision-1'
             OR i.AssessedAt<>v.ProcessedAt OR i.CreatedBy<>d.ActorId OR i.CreatedBy<>v.CreatedBy OR g.UserId<>d.ActorId
             OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.bookId')),'00000000-0000-0000-0000-000000000000')<>v.BookId
             OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.policyId')),'00000000-0000-0000-0000-000000000000')<>v.PolicyId
             OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.versionId')),'00000000-0000-0000-0000-000000000000')<>v.VersionId
             OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.transactionId')),'00000000-0000-0000-0000-000000000000')<>v.TransactionId
             OR COALESCE(JSON_VALUE(i.DecisionJson,'$.sourceHash'),'')<>LOWER(CONVERT(varchar(64),v.SourceHash,2))
             OR COALESCE(TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DecisionJson,'$.assessedAt')),'0001-01-01')<>i.AssessedAt
             OR COALESCE(TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DecisionJson,'$.from')),'0001-01-01')<>d.EffectiveAt
             OR COALESCE(TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DecisionJson,'$.to')),'0001-01-01')<>v.TermEndsAt
             OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.authorityGrantId')),'00000000-0000-0000-0000-000000000000')<>g.Id
             OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.authorityVersionId')),'00000000-0000-0000-0000-000000000000')<>a.Id
             OR g.AuthorityVersionId<>a.Id OR a.BinderVersionId<>b.Id OR b.Id<>v.BinderVersionId
             OR g.EffectiveFrom>i.AssessedAt OR g.EffectiveTo<=i.AssessedAt OR g.EffectiveFrom>d.EffectiveAt OR g.EffectiveTo<v.TermEndsAt
             OR (g.RevokedAt IS NOT NULL AND g.RevokedAt<=i.AssessedAt)
             OR COALESCE(TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.actorDistrictLimit')),-1)<>TRY_CONVERT(decimal(38,2),JSON_VALUE(a.DefinitionJson,'$.limits.districtProperty'))
             OR COALESCE(TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.binderDistrictLimit')),-1)<>TRY_CONVERT(decimal(38,2),JSON_VALUE(b.DefinitionJson,'$.limits.districtProperty')))
             THROW 51945,'Commercial adjustment capacity decision must retain exact source, clock and authority.',1;
            IF EXISTS(SELECT 1 FROM @adjustment i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
             WHERE JSON_QUERY(i.DecisionJson,'$.versions') IS NULL OR (SELECT COUNT(*) FROM OPENJSON(i.DecisionJson,'$.versions'))<>
             (SELECT COUNT(*) FROM PolicyVersion p WHERE p.TransactionId=v.TransactionId)
             OR EXISTS(SELECT p.Id,p.ContentHash,p.EffectiveAt,p.SliceOrdinal FROM PolicyVersion p WHERE p.TransactionId=v.TransactionId
             EXCEPT SELECT j.versionId,TRY_CONVERT(binary(32),j.sourceHash,2),j.effectiveAt,j.sliceOrdinal FROM OPENJSON(i.DecisionJson,'$.versions')
             WITH(versionId uniqueidentifier,sourceHash varchar(64),effectiveAt datetimeoffset,sliceOrdinal int) j))
             THROW 51945,'Capacity decision must retain the complete immutable adjustment version manifest.',1;
            DECLARE @dated TABLE(DecisionId uniqueidentifier,ExposureId uniqueidentifier,District nvarchar(4) COLLATE Latin1_General_100_BIN2,StartsAt datetimeoffset,EndsAt datetimeoffset,
             OwnSum decimal(38,2),OtherSum decimal(38,2),Total decimal(38,2),PolicyCount int,LimitId uniqueidentifier,LimitHash binary(32),LimitAmount decimal(38,2),Headroom decimal(38,2),Blocker nvarchar(100));
            INSERT @dated SELECT i.Id,i.ExposureVersionId,j.district,j.[from],j.[to],j.proposedPropertySum,j.otherPropertySum,j.resultingPropertySum,j.policyCount,
             j.limitVersionId,TRY_CONVERT(binary(32),j.limitHash,2),j.limit,j.headroom,j.blocker FROM @adjustment i CROSS APPLY OPENJSON(i.DecisionJson,'$.intervals')
             WITH(district nvarchar(4),[from] datetimeoffset,[to] datetimeoffset,proposedPropertySum decimal(38,2),otherPropertySum decimal(38,2),resultingPropertySum decimal(38,2),
             policyCount int,limitVersionId uniqueidentifier,limitHash varchar(64),limit decimal(38,2),headroom decimal(38,2),blocker nvarchar(100)) j;
            IF EXISTS(SELECT 1 FROM @adjustment i WHERE NOT EXISTS(SELECT 1 FROM @dated d WHERE d.DecisionId=i.Id))
             THROW 51945,'A commercial adjustment requires a complete dated capacity assessment.',1;
            DECLARE @district TABLE(DecisionId uniqueidentifier,District nvarchar(4) COLLATE Latin1_General_100_BIN2);
            INSERT @district SELECT DISTINCT i.Id,loc.District FROM @adjustment i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
              JOIN CommercialExposureVersion own ON own.BookId=v.BookId AND own.PolicyId=v.PolicyId AND own.ProcessedAt<=i.AssessedAt
                AND own.EffectiveAt<v.TermEndsAt AND own.TermStartsAt<v.TermEndsAt
                AND TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DecisionJson,'$.from'))<own.TermEndsAt
              JOIN CommercialExposureLocation loc ON loc.ExposureVersionId=own.Id;
            INSERT @district SELECT i.Id,'*' FROM @adjustment i WHERE NOT EXISTS(SELECT 1 FROM @district d WHERE d.DecisionId=i.Id);
            IF EXISTS(SELECT DecisionId,District FROM @district EXCEPT SELECT DecisionId,District FROM @dated)
              OR EXISTS(SELECT DecisionId,District FROM @dated EXCEPT SELECT DecisionId,District FROM @district)
              THROW 51945,'Commercial adjustment assessment must include every previous and proposed own district.',1;
            IF EXISTS(SELECT 1 FROM @dated r JOIN @adjustment i ON i.Id=r.DecisionId JOIN CommercialExposureVersion v ON v.Id=r.ExposureId
              JOIN CommercialExposureVersion source ON source.BookId=v.BookId AND source.ProcessedAt<=i.AssessedAt
              CROSS APPLY(VALUES(source.TermStartsAt),(source.TermEndsAt),(source.EffectiveAt)) point(At)
              WHERE r.StartsAt<point.At AND point.At<r.EndsAt)
              OR EXISTS(SELECT 1 FROM @dated r JOIN @adjustment i ON i.Id=r.DecisionId JOIN CommercialExposureVersion v ON v.Id=r.ExposureId
                JOIN CommercialExposureLimitVersion limit ON limit.BookId=v.BookId AND limit.PublishedAt<=i.AssessedAt
                  AND (limit.District='*' OR EXISTS(SELECT 1 FROM @district d WHERE d.DecisionId=i.Id AND d.District=limit.District))
                CROSS APPLY(VALUES(limit.EffectiveFrom),(limit.EffectiveTo)) point(At)
                WHERE r.StartsAt<point.At AND point.At<r.EndsAt)
              THROW 51945,'Commercial adjustment intervals must include every known book and applicable limit boundary.',1;
            IF EXISTS(SELECT 1 FROM @dated r JOIN @adjustment i ON i.Id=r.DecisionId JOIN CommercialExposureVersion v ON v.Id=r.ExposureId
             LEFT JOIN CommercialExposureLimitVersion l ON l.Id=r.LimitId
             OUTER APPLY(SELECT SUM(loc.SumInsured) OwnSum,COUNT(DISTINCT winners.PolicyId) OwnCount
               FROM(SELECT x.Id,x.PolicyId,x.TransactionKind,ROW_NUMBER() OVER(PARTITION BY x.PolicyId ORDER BY x.TermStartsAt DESC,x.TermId,x.EffectiveAt DESC,x.TransactionSequence DESC,x.SliceOrdinal DESC,x.VersionId) rn
                 FROM CommercialExposureVersion x WHERE x.BookId=v.BookId AND x.PolicyId=v.PolicyId AND x.ProcessedAt<=i.AssessedAt
                  AND x.TermStartsAt<=r.StartsAt AND r.StartsAt<x.TermEndsAt AND x.EffectiveAt<=r.StartsAt) winners
               JOIN CommercialExposureLocation loc ON loc.ExposureVersionId=winners.Id AND loc.District=r.District AND loc.SumInsured>0
               WHERE winners.rn=1 AND winners.TransactionKind<>'cancellation') owned
            OUTER APPLY(SELECT SUM(loc.SumInsured) OtherSum,COUNT(DISTINCT winners.PolicyId) OtherCount
               FROM(SELECT x.Id,x.PolicyId,x.TransactionKind,ROW_NUMBER() OVER(PARTITION BY x.PolicyId ORDER BY x.TermStartsAt DESC,x.TermId,x.EffectiveAt DESC,x.TransactionSequence DESC,x.SliceOrdinal DESC,x.VersionId) rn
                 FROM CommercialExposureVersion x WHERE x.BookId=v.BookId AND x.PolicyId<>v.PolicyId AND x.ProcessedAt<=i.AssessedAt
                  AND x.TermStartsAt<=r.StartsAt AND r.StartsAt<x.TermEndsAt AND x.EffectiveAt<=r.StartsAt) winners
               JOIN CommercialExposureLocation loc ON loc.ExposureVersionId=winners.Id AND loc.District=r.District AND loc.SumInsured>0
               WHERE winners.rn=1 AND winners.TransactionKind<>'cancellation') others
            CROSS APPLY(SELECT owned.OwnSum,others.OtherSum,owned.OwnCount+others.OtherCount PolicyCount) actual
             WHERE r.District IS NULL OR r.StartsAt IS NULL OR r.EndsAt IS NULL OR r.StartsAt>=r.EndsAt
             OR r.StartsAt<TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DecisionJson,'$.from')) OR r.EndsAt>v.TermEndsAt
             OR r.OwnSum IS NULL OR r.OtherSum IS NULL OR r.Total IS NULL OR r.PolicyCount IS NULL OR r.LimitAmount IS NULL OR r.Headroom IS NULL OR r.LimitHash IS NULL
             OR r.OwnSum<>COALESCE(actual.OwnSum,0) OR r.OtherSum<>COALESCE(actual.OtherSum,0) OR r.PolicyCount<>actual.PolicyCount
             OR r.Total<>r.OwnSum+r.OtherSum OR r.Headroom<>r.LimitAmount-r.Total OR r.Headroom<0 OR r.Blocker IS NOT NULL
             OR l.Id IS NULL OR l.BookId<>v.BookId OR (l.District<>'*' AND l.District<>r.District) OR l.PublishedAt>i.AssessedAt
             OR r.StartsAt<l.EffectiveFrom OR r.EndsAt>l.EffectiveTo OR r.LimitHash<>l.ContentHash OR r.LimitAmount<>l.Amount
             OR r.Total>TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.actorDistrictLimit'))
             OR r.Total>TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.binderDistrictLimit')))
             THROW 51945,'Commercial adjustment intervals must match the whole issued book and pinned limits.',1;
            IF EXISTS(SELECT 1 FROM(SELECT r.*,LAG(r.EndsAt,1,TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DecisionJson,'$.from'))) OVER(PARTITION BY r.DecisionId,r.District ORDER BY r.StartsAt) PreviousEnd
             FROM @dated r JOIN @adjustment i ON i.Id=r.DecisionId) ordered WHERE StartsAt<>PreviousEnd)
             OR EXISTS(SELECT r.DecisionId,r.District FROM @dated r JOIN CommercialExposureVersion v ON v.Id=r.ExposureId GROUP BY r.DecisionId,r.District,v.TermEndsAt HAVING MAX(r.EndsAt)<>v.TermEndsAt)
             THROW 51945,'Commercial adjustment intervals must cover each assessed district without gaps.',1;
            END;
            """);
    }
    private static void RemoveCommercialServicingExposureGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE OR ALTER TRIGGER TR_CommercialExposureIssueDecision_Source ON CommercialExposureIssueDecision AFTER INSERT AS BEGIN
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
    }
}
