using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class RenewalInvitationTemplates
{
    private static void AddInvitationGuards(MigrationBuilder migration)
    {
        migration.Sql(PreviousTermsSource.Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal)
            .Replace("t.Kind<>'servicing-terms'","t.Kind<>CASE WHEN d.Kind='renewal' THEN 'renewal-invitation' ELSE 'servicing-terms' END",StringComparison.Ordinal)
            .Replace("COALESCE(JSON_VALUE(i.TermsJson,'$.format'),'')<>'servicing-contract-1'","COALESCE(JSON_VALUE(i.TermsJson,'$.format'),'')<>CASE WHEN d.Kind='renewal' THEN 'renewal-contract-1' ELSE 'servicing-contract-1' END",StringComparison.Ordinal));
        migration.Sql("""
            CREATE TRIGGER TR_ServicingTermsVersion_Renewal ON ServicingTermsVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId JOIN ServicingDraft d ON d.Id=i.DraftId
              LEFT JOIN RenewalPreparationVersion p ON p.Id=c.RenewalPreparationVersionId
              LEFT JOIN RenewalExperienceVersion x ON x.Id=c.RenewalExperienceVersionId
              LEFT JOIN RenewalExperienceReview r ON r.Id=c.RenewalExperienceReviewId
              LEFT JOIN FairValueAssessmentVersion a ON a.Id=p.FairValueAssessmentId
              LEFT JOIN ProductEvidenceFileVersion f ON f.Id=a.EvidenceFileVersionId
              WHERE d.Kind='renewal' AND (p.Id IS NULL OR i.PreparedAt>p.StartsAt OR x.Id IS NULL OR x.EarnedPremium<=0
                OR r.Id IS NULL OR r.Outcome<>'accepted' OR r.ExperienceVersionId<>x.Id
                OR a.Id IS NULL OR a.Outcome<>'pass' OR f.ScreeningState<>'accepted'
                OR a.ValidFrom>p.StartsAt OR a.ValidTo<=p.StartsAt OR a.ApprovedAt>i.PreparedAt
                OR NOT EXISTS(SELECT 1 FROM UserAuthorityGrant g JOIN AuthorityVersion v ON v.Id=g.AuthorityVersionId JOIN [User] u ON u.Id=g.UserId
                  WHERE g.Id=r.AuthorityGrantId AND g.UserId=r.CreatedBy AND g.AuthorityVersionId=r.AuthorityVersionId AND g.RevokedAt IS NULL
                    AND g.EffectiveFrom<=i.PreparedAt AND g.EffectiveTo>i.PreparedAt AND v.State='published'
                    AND v.EffectiveFrom<=i.PreparedAt AND v.EffectiveTo>i.PreparedAt AND u.State='active')))
              THROW 51740,'Renewal invitations require reviewed experience, valid fair value and a supported inception.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingTermsDelivery_RenewalTime ON ServicingTermsDelivery AFTER INSERT,UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId JOIN ServicingDraft d ON d.Id=i.DraftId
              JOIN RenewalPreparationVersion p ON p.Id=c.RenewalPreparationVersionId
              WHERE d.Kind='renewal' AND (i.CreatedAt>p.StartsAt OR (i.State='delivered' AND i.CompletedAt>p.StartsAt)))
              THROW 51741,'Late renewal invitation delivery cannot establish uninterrupted cover.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingAcceptance_RenewalTime ON ServicingAcceptance AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId JOIN ServicingDraft d ON d.Id=i.DraftId
              JOIN RenewalPreparationVersion p ON p.Id=c.RenewalPreparationVersionId
              WHERE d.Kind='renewal' AND (i.RecordedAt>p.StartsAt OR i.AcceptedAt>p.StartsAt))
              THROW 51742,'Late renewal acceptance cannot establish uninterrupted cover.',1;
            END;
            """);
    }

    private static void RemoveInvitationGuards(MigrationBuilder migration)
    {
        migration.Sql("DROP TRIGGER IF EXISTS TR_ServicingTermsVersion_Renewal; DROP TRIGGER IF EXISTS TR_ServicingTermsDelivery_RenewalTime; DROP TRIGGER IF EXISTS TR_ServicingAcceptance_RenewalTime;");
        migration.Sql(PreviousTermsSource.Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
    }
}
