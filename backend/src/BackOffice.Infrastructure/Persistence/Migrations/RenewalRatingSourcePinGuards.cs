using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class RenewalRatingSourcePins
{
    private static void AddRenewalRatingGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingReferralDecision_Renewal ON ServicingReferralDecision AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingReferral r ON r.Id=i.ReferralId
              WHERE i.Outcome IN ('approve','approve-with-conditions') AND (r.RuleCode='UW-31-information'
                OR (r.RuleCode='UW-31' AND NOT EXISTS(SELECT 1 FROM UserRole u JOIN Role role ON role.Id=u.RoleId
                  WHERE u.UserId=i.ActorId AND role.Code='senior-underwriter' AND role.Scope='internal'))))
              THROW 51732,'Incomplete renewal experience cannot be approved and UW-31 needs a current senior underwriter.',1;
            END;
            """);
        migration.Sql(PreviousCycleSource.Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER", StringComparison.Ordinal)
            .Replace("COALESCE(JSON_VALUE(i.InputJson,'$.format'),'')<>'servicing-rating-input-1'",
                "COALESCE(JSON_VALUE(i.InputJson,'$.format'),'')<>CASE WHEN d.Kind='renewal' THEN 'servicing-rating-input-2' ELSE 'servicing-rating-input-1' END", StringComparison.Ordinal));
        var fee = "CASE WHEN i.RenewalPreparationVersionId IS NULL THEN JSON_VALUE(s.[Values],'$.adjustmentFee') ELSE JSON_VALUE(s.[Values],'$.renewalFee') END";
        migration.Sql(PreviousCycleSetting.Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER", StringComparison.Ordinal)
            .Replace("s.Scope<>'servicing-rating'", "s.Scope<>CASE WHEN i.RenewalPreparationVersionId IS NULL THEN 'servicing-rating' ELSE 'renewal-preparation' END", StringComparison.Ordinal)
            .Replace("COALESCE(JSON_VALUE(s.[Values],'$.kind'),'')<>'servicing-rating'", "COALESCE(JSON_VALUE(s.[Values],'$.kind'),'')<>s.Scope", StringComparison.Ordinal)
            .Replace("COALESCE(JSON_VALUE(s.[Values],'$.earningBasis'),'')<>'london-calendar-days'", "(i.RenewalPreparationVersionId IS NULL AND COALESCE(JSON_VALUE(s.[Values],'$.earningBasis'),'')<>'london-calendar-days')", StringComparison.Ordinal)
            .Replace("JSON_VALUE(s.[Values],'$.adjustmentFee')", fee, StringComparison.Ordinal));
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCycle_RenewalImmutable ON ServicingCycle AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF UPDATE(RenewalPreparationVersionId) OR UPDATE(RenewalExperienceVersionId) OR UPDATE(RenewalExperienceReviewId)
              THROW 51730,'Renewal rating source identities are immutable.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingCycle_RenewalSource ON ServicingCycle AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId
              LEFT JOIN RenewalPreparationVersion p ON p.Id=i.RenewalPreparationVersionId AND p.DraftId=i.DraftId
              LEFT JOIN RenewalExperienceVersion e ON e.Id=i.RenewalExperienceVersionId AND e.DraftId=i.DraftId
              LEFT JOIN RenewalExperienceReview r ON r.Id=i.RenewalExperienceReviewId AND r.DraftId=i.DraftId AND r.ExperienceVersionId=e.Id
              JOIN SettingVersion s ON s.Id=i.ServicingSettingVersionId
              WHERE (d.Kind<>'renewal' AND (i.RenewalPreparationVersionId IS NOT NULL OR i.RenewalExperienceVersionId IS NOT NULL OR i.RenewalExperienceReviewId IS NOT NULL OR JSON_QUERY(i.InputJson,'$.renewal') IS NOT NULL))
              OR (d.Kind='renewal' AND (p.Id IS NULL OR p.CreatedAt>i.CreatedAt OR p.BaseVersionId<>i.BaseVersionId
                OR p.ProductVersionId<>i.ProductVersionId OR p.BinderVersionId<>i.BinderVersionId OR p.AgencyTermsVersionId<>i.AgencyTermsVersionId
                OR p.RuleSettingVersionId<>i.ServicingSettingVersionId
                OR EXISTS(SELECT 1 FROM PolicyTerm other WHERE other.PolicyId=i.PolicyId AND other.Id<>i.BaseTermId AND other.StartsAt<p.EndsAt AND p.StartsAt<other.EndsAt)
                OR NOT EXISTS(SELECT 1 FROM PolicyVersion v JOIN PolicyTransaction pt ON pt.Id=v.TransactionId WHERE v.Id=i.BaseVersionId AND pt.Kind IN ('new-business','adjustment','renewal'))
                OR i.BaseVersionId<>(SELECT TOP(1) v.Id FROM PolicyVersion v JOIN PolicyTransaction pt ON pt.Id=v.TransactionId
                  WHERE v.TermId=i.BaseTermId AND v.PolicyId=i.PolicyId AND v.EffectiveAt<p.StartsAt AND v.ProcessedAt<=i.CreatedAt AND pt.ProcessedAt<=i.CreatedAt
                  AND pt.Kind IN ('new-business','adjustment','renewal','cancellation') ORDER BY v.EffectiveAt DESC,pt.Sequence DESC,v.SliceOrdinal DESC,v.Id)
                OR EXISTS(SELECT 1 FROM RenewalPreparationVersion newer WHERE newer.DraftId=i.DraftId AND newer.Sequence>p.Sequence)
                OR EXISTS(SELECT 1 FROM RenewalExperienceVersion newer WHERE newer.DraftId=i.DraftId AND (e.Id IS NULL OR newer.Sequence>e.Sequence))
                OR EXISTS(SELECT 1 FROM RenewalExperienceReview newer WHERE newer.DraftId=i.DraftId AND newer.ExperienceVersionId=e.Id AND (r.Id IS NULL OR newer.Sequence>r.Sequence))
                OR e.CreatedAt>i.CreatedAt OR r.CreatedAt>i.CreatedAt
                OR (r.Outcome='accepted' AND NOT EXISTS(SELECT 1 FROM UserAuthorityGrant g JOIN AuthorityVersion a ON a.Id=g.AuthorityVersionId JOIN [User] u ON u.Id=g.UserId
                  WHERE g.Id=r.AuthorityGrantId AND g.UserId=r.CreatedBy AND a.Id=r.AuthorityVersionId AND g.RevokedAt IS NULL
                    AND g.EffectiveFrom<=i.CreatedAt AND i.CreatedAt<g.EffectiveTo AND a.State='published' AND a.EffectiveFrom<=i.CreatedAt AND i.CreatedAt<a.EffectiveTo AND u.State='active'))
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.renewal.preparationVersionId')),'00000000-0000-0000-0000-000000000000')<>p.Id
                OR COALESCE(JSON_VALUE(i.InputJson,'$.renewal.experienceVersionId'),'')<>COALESCE(LOWER(CONVERT(varchar(36),e.Id)),'')
                OR COALESCE(JSON_VALUE(i.InputJson,'$.renewal.experienceReviewId'),'')<>COALESCE(LOWER(CONVERT(varchar(36),r.Id)),'')
                OR COALESCE(JSON_VALUE(i.InputJson,'$.renewal.fairValueAssessmentId'),'')<>COALESCE(LOWER(CONVERT(varchar(36),p.FairValueAssessmentId)),'')
                OR COALESCE(JSON_VALUE(i.InputJson,'$.renewal.evidenceAccepted'),'')<>CASE WHEN r.Outcome='accepted' THEN 'true' ELSE 'false' END
                OR COALESCE(JSON_VALUE(i.InputJson,'$.renewal.ruleVersion'),'')<>COALESCE(JSON_VALUE(s.[Values],'$.ruleVersion'),'')
                OR COALESCE(TRY_CONVERT(int,JSON_VALUE(i.InputJson,'$.renewal.thresholdBasisPoints')),-1)<>COALESCE(TRY_CONVERT(int,JSON_VALUE(s.[Values],'$.lossRatioThresholdBasisPoints')),-2)
                OR COALESCE(TRY_CONVERT(int,JSON_VALUE(i.InputJson,'$.renewal.loadingBasisPoints')),-1)<>COALESCE(TRY_CONVERT(int,JSON_VALUE(s.[Values],'$.experienceLoadingBasisPoints')),-2)
                OR COALESCE(TRY_CONVERT(datetimeoffset,JSON_VALUE(i.InputJson,'$.term.startsAt')),'0001-01-01')<>p.StartsAt
                OR COALESCE(TRY_CONVERT(datetimeoffset,JSON_VALUE(i.InputJson,'$.term.endsAt')),'0001-01-01')<>p.EndsAt
                OR COALESCE(JSON_VALUE(i.InputJson,'$.term.kind'),'')<>CASE WHEN p.TermMonths=12 THEN 'annual' ELSE 'short-period' END
                OR COALESCE(JSON_VALUE(i.InputJson,'$.term.timeZone'),'')<>'Europe/London'
                OR (SELECT COUNT(*) FROM OPENJSON(i.InputJson,'$.slices'))<>1
                OR COALESCE(TRY_CONVERT(datetimeoffset,JSON_VALUE(i.InputJson,'$.slices[0].effectiveAt')),'0001-01-01')<>p.StartsAt
                OR (e.Id IS NULL AND JSON_QUERY(i.InputJson,'$.renewal.experience') IS NOT NULL)
                OR (e.Id IS NOT NULL AND (JSON_QUERY(i.InputJson,'$.renewal.experience') IS NULL
                  OR COALESCE(JSON_VALUE(i.InputJson,'$.renewal.experience.observationStartsOn'),'')<>CONVERT(char(10),e.ObservationStartsOn,23)
                  OR COALESCE(JSON_VALUE(i.InputJson,'$.renewal.experience.observationEndsOn'),'')<>CONVERT(char(10),e.ObservationEndsOn,23)
                  OR COALESCE(TRY_CONVERT(int,JSON_VALUE(i.InputJson,'$.renewal.experience.claimCount')),-1)<>e.ClaimCount
                  OR COALESCE(TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.renewal.experience.paid')),-1)<>e.Paid
                  OR COALESCE(TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.renewal.experience.outstanding')),-1)<>e.Outstanding
                  OR COALESCE(TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.renewal.experience.earnedPremium')),-1)<>e.EarnedPremium
                  OR COALESCE(JSON_VALUE(i.InputJson,'$.renewal.experience.sourceCode'),'')<>e.SourceCode
                  OR COALESCE(JSON_VALUE(i.InputJson,'$.renewal.experience.sourceReference'),'')<>e.SourceReference
                  OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.renewal.experience.evidenceAssociationId')),'00000000-0000-0000-0000-000000000000')<>e.EvidenceAssociationId)))))
              THROW 51731,'Renewal rating requires the exact latest owned preparation, experience, review and configured full term.',1;
            END;
            """);
    }

    private static void RemoveRenewalRatingGuards(MigrationBuilder migration)
    {
        migration.Sql("DROP TRIGGER TR_ServicingCycle_RenewalSource; DROP TRIGGER TR_ServicingCycle_RenewalImmutable; DROP TRIGGER TR_ServicingReferralDecision_Renewal;");
        migration.Sql(PreviousCycleSource.Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER", StringComparison.Ordinal));
        migration.Sql(PreviousCycleSetting.Replace("CREATE TRIGGER", "CREATE OR ALTER TRIGGER", StringComparison.Ordinal));
    }
}
