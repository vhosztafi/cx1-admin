using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static class ServicingAcceptanceStorageGuards
{
    internal static void Up(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingAcceptance_Source ON ServicingAcceptance AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId JOIN ServicingDraft d ON d.Id=i.DraftId
              JOIN ServicingRatingResult r ON r.Id=i.RatingId JOIN ServicingTermsVersion t ON t.Id=i.TermsVersionId
              JOIN ServicingTermsDelivery v ON v.Id=i.DeliveryId JOIN ServicingEvidenceAssociation a ON a.Id=i.EvidenceAssociationId
              JOIN ServicingEvidenceEvent e ON e.Id=i.EvidenceReviewId JOIN ServicingEvidenceFile f ON f.Id=a.FileId
              WHERE d.State<>'draft' OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>i.CycleId OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
                OR c.State<>'rated' OR c.CurrentRatingId IS NULL OR c.CurrentRatingId<>i.RatingId
                OR c.CurrentTermsVersionId IS NULL OR c.CurrentTermsVersionId<>i.TermsVersionId OR c.CurrentDeliveryId IS NULL OR c.CurrentDeliveryId<>i.DeliveryId
                OR i.TermsHash<>t.TermsHash OR i.RecordedAt>=r.ExpiresAt OR v.State<>'delivered' OR v.CompletedAt IS NULL OR v.CompletedAt>i.AcceptedAt
                OR a.TermsVersionId IS NULL OR a.TermsVersionId<>i.TermsVersionId OR a.RequirementCode<>'acceptance-proof'
                OR a.WithdrawnEventId IS NOT NULL OR a.LatestReviewId IS NULL OR a.LatestReviewId<>i.EvidenceReviewId
                OR e.Kind<>'review' OR e.Outcome IS NULL OR e.Outcome<>'accepted' OR f.ScreeningState<>'accepted' OR i.RecordedAt<e.RecordedAt)
              THROW 51520,'Acceptance requires exact current delivered terms and current reviewed acceptance proof.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingAcceptance_Immutable ON ServicingAcceptance AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51521,'Servicing acceptance is append-only.',1; END;
            """);
    }

    internal static void Down(MigrationBuilder migration)
    {
        migration.Sql("DROP TRIGGER IF EXISTS TR_ServicingAcceptance_Source;");
        migration.Sql("DROP TRIGGER IF EXISTS TR_ServicingAcceptance_Immutable;");
    }
}
