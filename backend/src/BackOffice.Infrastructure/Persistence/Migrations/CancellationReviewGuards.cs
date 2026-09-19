using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CancellationReviewStorage
{
    private static void AddReviewGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            IF NOT EXISTS(SELECT 1 FROM SettingVersion WHERE Scope='cancellation-review')
            INSERT SettingVersion(Id,Scope,Version,EffectiveFrom,[Values],CreatedAt,CreatedBy)
            VALUES(NEWID(),'cancellation-review',1,'2026-01-01T00:00:00+00:00',
            '{"demo":true,"kind":"cancellation-review","schemaVersion":"1","ruleVersion":"demo-servicing-1","authorityVersions":["demo-underwriter-1","demo-senior-1"],"seniorAuthorityVersions":["demo-senior-1"]}',SYSUTCDATETIME(),NULL);
            """);
        foreach (var table in new[] { "CancellationEvidence", "CancellationEvidenceReview", "CancellationPreview", "CancellationApproval" })
            migration.Sql($"CREATE TRIGGER TR_{table}_Immutable ON {table} AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; THROW 51800,'Cancellation review history is immutable.',1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_CancellationEvidence_Source ON CancellationEvidence AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d WITH(UPDLOCK,HOLDLOCK) ON d.Id=i.DraftId
                WHERE d.Kind<>'cancellation' OR d.State<>'draft' OR d.CurrentRevisionId<>i.RevisionId OR d.CurrentRevisionId IS NULL)
                THROW 51801,'Cancellation evidence requires the current cancellation revision.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CancellationEvidenceReview_Source ON CancellationEvidenceReview AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d WITH(UPDLOCK,HOLDLOCK) ON d.Id=i.DraftId
                JOIN UserAuthorityGrant g WITH(HOLDLOCK) ON g.Id=i.AuthorityGrantId
                JOIN AuthorityVersion a WITH(HOLDLOCK) ON a.Id=i.AuthorityVersionId
                JOIN [User] u WITH(HOLDLOCK) ON u.Id=i.CreatedBy
                JOIN PolicyTerm t ON t.Id=d.BaseTermId
                WHERE d.Kind<>'cancellation' OR d.State<>'draft' OR d.CurrentRevisionId<>i.RevisionId OR d.CurrentRevisionId IS NULL
                OR g.RevokedAt IS NOT NULL OR g.EffectiveFrom>i.CreatedAt OR g.EffectiveTo<=i.CreatedAt
                OR a.State<>'published' OR a.EffectiveFrom>i.CreatedAt OR a.EffectiveTo<=i.CreatedAt
                OR a.ProductVersionId<>t.ProductVersionId OR u.State<>'active'
                OR NOT EXISTS(SELECT 1 FROM UserRole ur JOIN Role r ON r.Id=ur.RoleId WHERE ur.UserId=u.Id AND r.Code IN ('underwriter','senior-underwriter')))
                THROW 51802,'Cancellation evidence review requires current owned authority.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CancellationPreview_Source ON CancellationPreview AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d WITH(UPDLOCK,HOLDLOCK) ON d.Id=i.DraftId
                JOIN PolicyTerm t WITH(UPDLOCK,HOLDLOCK) ON t.Id=i.BaseTermId
                JOIN SettingVersion s WITH(HOLDLOCK) ON s.Id=i.RuleSettingVersionId
                JOIN ServicingRevision r ON r.Id=i.RevisionId
                WHERE d.Kind<>'cancellation' OR d.State<>'draft' OR d.CurrentRevisionId<>i.RevisionId OR d.CurrentRevisionId IS NULL
                OR i.EffectiveAt<t.StartsAt OR i.EffectiveAt>=t.EndsAt OR s.Scope<>'cancellation-review' OR s.EffectiveFrom>i.CreatedAt
                OR ISNULL(JSON_VALUE(s.[Values],'$.ruleVersion'),'')<>i.RuleVersion
                OR ISNULL(JSON_VALUE(r.ProposalJson,'$.cancellationReasonCode'),'')<>i.ReasonCode
                OR ISNULL(JSON_VALUE(i.InputJson,'$.revisionId'),'')<>CONVERT(varchar(36),i.RevisionId)
                OR ISNULL(JSON_VALUE(i.InputJson,'$.settingId'),'')<>CONVERT(varchar(36),i.RuleSettingVersionId)
                OR EXISTS(SELECT 1 FROM PolicyVersion v WHERE v.TermId=t.Id AND v.EffectiveAt>i.EffectiveAt)
                OR EXISTS(SELECT 1 FROM PolicyTerm later WHERE later.PolicyId=t.PolicyId AND later.Number>t.Number)
                OR EXISTS(SELECT 1 FROM PolicyTransaction pt WHERE pt.TermId=t.Id AND pt.Kind='cancellation'))
                THROW 51803,'Cancellation preview requires current owned in-sequence inputs.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CancellationApproval_Source ON CancellationApproval AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CancellationPreview p ON p.Id=i.PreviewId
                JOIN ServicingDraft d WITH(UPDLOCK,HOLDLOCK) ON d.Id=i.DraftId
                JOIN PolicyTerm t ON t.Id=d.BaseTermId JOIN SettingVersion s WITH(HOLDLOCK) ON s.Id=p.RuleSettingVersionId
                JOIN UserAuthorityGrant g WITH(HOLDLOCK) ON g.Id=i.AuthorityGrantId
                JOIN AuthorityVersion a WITH(HOLDLOCK) ON a.Id=i.AuthorityVersionId
                JOIN [User] u WITH(HOLDLOCK) ON u.Id=i.CreatedBy
                WHERE d.Kind<>'cancellation' OR d.State<>'draft' OR d.CurrentRevisionId<>i.RevisionId OR d.CurrentRevisionId IS NULL
                OR g.RevokedAt IS NOT NULL OR g.EffectiveFrom>i.CreatedAt OR g.EffectiveTo<=i.CreatedAt
                OR a.State<>'published' OR a.EffectiveFrom>i.CreatedAt OR a.EffectiveTo<=i.CreatedAt
                OR a.ProductVersionId<>t.ProductVersionId OR u.State<>'active'
                OR NOT EXISTS(SELECT 1 FROM UserRole ur JOIN Role r ON r.Id=ur.RoleId WHERE ur.UserId=u.Id AND r.Code IN ('underwriter','senior-underwriter'))
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(s.[Values],'$.authorityVersions') v WHERE v.value=a.Version)
                OR (p.ReasonCode IN ('non-payment','non-disclosure','insurer-instruction') AND
                    (i.CreatedBy=d.CreatedBy OR NOT EXISTS(SELECT 1 FROM OPENJSON(s.[Values],'$.seniorAuthorityVersions') v WHERE v.value=a.Version)
                     OR NOT EXISTS(SELECT 1 FROM UserRole ur JOIN Role r ON r.Id=ur.RoleId WHERE ur.UserId=u.Id AND r.Code='senior-underwriter')))
                OR EXISTS(SELECT 1 FROM SettingVersion newer WHERE newer.Scope=s.Scope AND newer.Version>s.Version AND newer.EffectiveFrom<=i.CreatedAt))
                THROW 51804,'Cancellation approval requires independent current permitted authority and the current revision.',1;
            END;
            """);
    }
}
