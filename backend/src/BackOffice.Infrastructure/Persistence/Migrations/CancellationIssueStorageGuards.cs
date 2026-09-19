using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CancellationIssueStorage
{
    private static void AddIssueStorageGuards(MigrationBuilder migration)
    {
        foreach(var table in new[]{"CancellationIssueDecision","CancellationConsequence","CancellationNoticeReceipt"})
            migration.Sql($"CREATE TRIGGER TR_{table}_Immutable ON {table} AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51820,'Cancellation issue history is immutable.',1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_CancellationIssueDecision_Source ON CancellationIssueDecision AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d WITH(UPDLOCK,HOLDLOCK) ON d.Id=i.DraftId
              JOIN PolicyTerm t WITH(UPDLOCK,HOLDLOCK) ON t.Id=i.BaseTermId
              JOIN CancellationPreview p ON p.Id=i.PreviewId JOIN CancellationApproval ap ON ap.Id=i.ApprovalId
              JOIN SettingVersion s WITH(HOLDLOCK) ON s.Id=p.RuleSettingVersionId
              JOIN UserAuthorityGrant g WITH(HOLDLOCK) ON g.Id=i.AuthorityGrantId
              JOIN AuthorityVersion av WITH(HOLDLOCK) ON av.Id=i.AuthorityVersionId
              JOIN BinderVersion b WITH(HOLDLOCK) ON b.Id=av.BinderVersionId
              JOIN UserAuthorityGrant ag WITH(HOLDLOCK) ON ag.Id=ap.AuthorityGrantId
              JOIN AuthorityVersion aa WITH(HOLDLOCK) ON aa.Id=ap.AuthorityVersionId
              JOIN BinderVersion ab WITH(HOLDLOCK) ON ab.Id=aa.BinderVersionId
              WHERE d.Kind<>'cancellation' OR d.State<>'draft' OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
                OR t.CurrentVersionId IS NULL OR t.CurrentVersionId<>i.BaseVersionId
                OR p.PolicyId<>i.PolicyId OR p.BaseTermId<>i.BaseTermId OR p.BaseVersionId<>i.BaseVersionId OR p.EffectiveAt<>i.EffectiveAt
                OR i.EffectiveAt<t.StartsAt OR i.EffectiveAt>=t.EndsAt OR ap.CreatedAt>i.CreatedAt
                OR EXISTS(SELECT 1 FROM PolicyVersion v WHERE v.TermId=t.Id AND v.EffectiveAt>i.EffectiveAt)
                OR EXISTS(SELECT 1 FROM PolicyTerm later WITH(UPDLOCK,HOLDLOCK) WHERE later.PolicyId=i.PolicyId AND later.Number>t.Number)
                OR EXISTS(SELECT 1 FROM PolicyTransaction previous WHERE previous.TermId=t.Id AND previous.Kind='cancellation')
                OR g.RevokedAt IS NOT NULL OR ag.RevokedAt IS NOT NULL
                OR g.EffectiveFrom>i.CreatedAt OR g.EffectiveTo<=i.CreatedAt OR g.EffectiveFrom>t.StartsAt OR g.EffectiveTo<t.EndsAt
                OR ag.EffectiveFrom>i.CreatedAt OR ag.EffectiveTo<=i.CreatedAt OR ag.EffectiveFrom>t.StartsAt OR ag.EffectiveTo<t.EndsAt
                OR av.State<>'published' OR aa.State<>'published' OR b.State<>'published' OR ab.State<>'published'
                OR av.ProductVersionId<>t.ProductVersionId OR aa.ProductVersionId<>t.ProductVersionId
                OR av.EffectiveFrom>i.CreatedAt OR av.EffectiveTo<=i.CreatedAt OR av.EffectiveFrom>t.StartsAt OR av.EffectiveTo<t.EndsAt
                OR aa.EffectiveFrom>i.CreatedAt OR aa.EffectiveTo<=i.CreatedAt OR aa.EffectiveFrom>t.StartsAt OR aa.EffectiveTo<t.EndsAt
                OR b.EffectiveFrom>t.StartsAt OR b.EffectiveTo<t.EndsAt OR ab.EffectiveFrom>t.StartsAt OR ab.EffectiveTo<t.EndsAt
                OR NOT EXISTS(SELECT 1 FROM [User] u WITH(HOLDLOCK) WHERE u.Id=i.ActorId AND u.State='active' AND u.AgencyId IS NULL)
                OR NOT EXISTS(SELECT 1 FROM [User] u WITH(HOLDLOCK) WHERE u.Id=ap.CreatedBy AND u.State='active' AND u.AgencyId IS NULL)
                OR NOT EXISTS(SELECT 1 FROM UserRole ur WITH(HOLDLOCK) JOIN Role r ON r.Id=ur.RoleId WHERE ur.UserId=i.ActorId AND r.Code IN ('underwriter','senior-underwriter'))
                OR NOT EXISTS(SELECT 1 FROM UserRole ur WITH(HOLDLOCK) JOIN Role r ON r.Id=ur.RoleId WHERE ur.UserId=ap.CreatedBy AND r.Code IN ('underwriter','senior-underwriter'))
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(s.[Values],'$.authorityVersions') x WHERE x.value=av.Version)
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(s.[Values],'$.authorityVersions') x WHERE x.value=aa.Version)
                OR EXISTS(SELECT 1 FROM SettingVersion newer WITH(HOLDLOCK) WHERE newer.Scope=s.Scope AND newer.Version>s.Version AND newer.EffectiveFrom<=i.CreatedAt)
                OR (p.ReasonCode IN ('non-payment','non-disclosure','insurer-instruction') AND
                  (ap.CreatedBy=d.CreatedBy OR NOT EXISTS(SELECT 1 FROM UserRole ur WITH(HOLDLOCK) JOIN Role r ON r.Id=ur.RoleId WHERE ur.UserId=ap.CreatedBy AND r.Code='senior-underwriter')
                   OR NOT EXISTS(SELECT 1 FROM OPENJSON(s.[Values],'$.seniorAuthorityVersions') x WHERE x.value=aa.Version))))
              THROW 51821,'Cancellation issue decision requires the exact current owned approval, base and delegated authority.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CancellationConsequence_Source ON CancellationConsequence AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CancellationIssueDecision d ON d.Id=i.DecisionId
              JOIN PolicyTransaction t ON t.Id=i.TransactionId JOIN PolicyVersion v ON v.Id=i.VersionId JOIN OutboxWork w ON w.Id=i.WorkId
              WHERE t.Kind<>'cancellation' OR t.ServicingDraftId IS NULL OR t.ServicingDraftId<>d.DraftId
                OR t.ServicingRevisionId IS NULL OR t.ServicingRevisionId<>d.RevisionId OR t.EffectiveAt<>d.EffectiveAt
                OR i.CreatedAt<>t.ProcessedAt OR w.SubjectRecordId<>i.Id OR w.Kind<>('cancellation-'+i.Kind) COLLATE Latin1_General_100_BIN2
                OR w.OperationKey<>('cancellation-'+i.Kind+'/'+LOWER(REPLACE(CONVERT(varchar(36),t.Id),'-',''))) COLLATE Latin1_General_100_BIN2
                OR CONVERT(varbinary(max),w.Payload)<>CONVERT(varbinary(max),i.PayloadJson)
                OR ISNULL(JSON_VALUE(i.PayloadJson,'$.versionId'),'')<>CONVERT(varchar(36),v.Id)
                OR ISNULL(JSON_VALUE(i.PayloadJson,'$.contentHash'),'')<>LOWER(CONVERT(varchar(64),v.ContentHash,2)))
              THROW 51822,'Cancellation consequences require the exact issued version and durable operation.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CancellationNoticeReceipt_Source ON CancellationNoticeReceipt AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CancellationConsequence c ON c.Id=i.ConsequenceId
              WHERE c.Kind<>'notice' OR i.CreatedAt<c.CreatedAt)
              THROW 51823,'Cancellation notice receipt requires an issued notice intent.',1;
            END;
            """);
    }
}
