using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class QuoteTermsDeliveryStorage
{
    private static void AddTermsGuards(MigrationBuilder migration)
    {
        foreach (var table in new[] { "QuoteTermsVersion", "QuoteAcceptance" })
            migration.Sql($"CREATE TRIGGER TR_{table}_AppendOnly ON [{table}] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51160, 'Quotation history is append-only.', 1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_TemplateVersion_Immutable ON TemplateVersion AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF NOT EXISTS(SELECT 1 FROM inserted) OR UPDATE(Id) OR UPDATE(Code) OR UPDATE(Version) OR UPDATE(ProductId) OR UPDATE(Kind) OR UPDATE(ContentJson) OR UPDATE(EffectiveFrom) OR UPDATE(EffectiveTo) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy)
              THROW 51161, 'Template version content is immutable.', 1;
            IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.State='retired' AND i.State<>'retired') THROW 51161, 'Template retirement is final.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_QuoteTermsVersion_Owner ON QuoteTermsVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN UnderwritingCycle c ON c.Id=i.CycleId JOIN TemplateVersion t ON t.Id=i.TemplateVersionId
                WHERE t.ProductId<>c.ProductId OR t.State<>'published' OR i.PreparedAt<t.EffectiveFrom OR i.PreparedAt>=t.EffectiveTo)
              THROW 51162, 'Prepared terms require the same product and applicable template.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_QuoteTermsDelivery_Immutable ON QuoteTermsDelivery AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF NOT EXISTS(SELECT 1 FROM inserted) OR UPDATE(Id) OR UPDATE(QuoteId) OR UPDATE(CycleId) OR UPDATE(TermsVersionId) OR UPDATE(WorkId) OR UPDATE(ScenarioVersionId) OR UPDATE(RecipientSnapshotJson) OR UPDATE(PayloadJson) OR UPDATE(PayloadHash) OR UPDATE(AssuranceHashAtSend) OR UPDATE(SentBy) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy)
              THROW 51163, 'Delivery request content is immutable.', 1;
            IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.State IN ('delivered','superseded') AND
                (i.State<>d.State OR i.CompletedAt<>d.CompletedAt OR ISNULL(i.OutcomeCode,'')<>ISNULL(d.OutcomeCode,'') OR ISNULL(CONVERT(varchar(36),i.ProviderOperationId),'')<>ISNULL(CONVERT(varchar(36),d.ProviderOperationId),'') OR ISNULL(CONVERT(varchar(36),i.AttemptId),'')<>ISNULL(CONVERT(varchar(36),d.AttemptId),'')))
              THROW 51163, 'Applied delivery outcomes are final.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_QuoteTermsDelivery_Work ON QuoteTermsDelivery AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN OutboxWork w ON w.Id=i.WorkId
              WHERE w.Kind<>'quote-delivery' OR w.SubjectRecordId<>i.Id OR w.ScenarioVersionId<>i.ScenarioVersionId OR w.OperationKey<>'quote-delivery/'+LOWER(REPLACE(CONVERT(varchar(36),i.Id),'-','')))
              THROW 51164, 'Delivery requires its exact durable work.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_QuoteAcceptance_Proof ON QuoteAcceptance AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i
              JOIN UnderwritingEvidenceAssociation a ON a.Id=i.EvidenceAssociationId
              JOIN UnderwritingEvidenceEvent e ON e.Id=i.EvidenceReviewId
              JOIN QuoteEvidenceFile f ON f.Id=a.FileId
              JOIN QuoteTermsVersion t ON t.Id=i.TermsVersionId
              JOIN QuoteTermsDelivery d ON d.Id=i.DeliveryId
              JOIN QuoteRatingResult r ON r.Id=i.RatingId
              JOIN UnderwritingCycle c ON c.Id=i.CycleId
              JOIN Quote q ON q.Id=i.QuoteId
              WHERE a.RequirementCode<>'acceptance-proof' OR a.TermsVersionId IS NULL OR a.TermsVersionId<>i.TermsVersionId OR a.LatestReviewId IS NULL OR a.LatestReviewId<>e.Id OR a.WithdrawnEventId IS NOT NULL
                OR e.Outcome<>'accepted' OR e.Kind<>'review' OR f.ScreeningState<>'accepted' OR t.TermsHash<>i.TermsHash
                OR d.State<>'delivered' OR d.CompletedAt>i.AcceptedAt OR i.RecordedAt>=r.ExpiresAt
                OR c.CurrentTermsVersionId IS NULL OR c.CurrentTermsVersionId<>i.TermsVersionId OR c.CurrentDeliveryId IS NULL OR c.CurrentDeliveryId<>d.Id
                OR q.CurrentUnderwritingCycleId IS NULL OR q.CurrentUnderwritingCycleId<>c.Id OR q.State IN ('bound','withdrawn','draft'))
              THROW 51165, 'Acceptance requires current delivered terms and the exact accepted proof.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_UnderwritingCycle_TermsPointers ON UnderwritingCycle AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted WHERE State IN ('superseded','bound')) AND (UPDATE(CurrentTermsVersionId) OR UPDATE(CurrentDeliveryId) OR UPDATE(CurrentAcceptanceId))
              THROW 51166, 'Completed cycle quotation pointers are immutable.', 1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN QuoteAcceptance a ON a.Id=i.CurrentAcceptanceId
                WHERE i.CurrentTermsVersionId IS NULL OR a.TermsVersionId<>i.CurrentTermsVersionId OR i.CurrentDeliveryId IS NULL OR a.DeliveryId<>i.CurrentDeliveryId OR i.CurrentRatingId IS NULL OR a.RatingId<>i.CurrentRatingId)
              THROW 51166, 'Current acceptance must match all quotation pointers.', 1;
            END;
            """);
    }
    private static void DropTermsGuards(MigrationBuilder migration)
    {
        foreach (var name in new[] { "QuoteTermsVersion_AppendOnly", "QuoteAcceptance_AppendOnly", "TemplateVersion_Immutable", "QuoteTermsVersion_Owner", "QuoteTermsDelivery_Immutable", "QuoteTermsDelivery_Work", "QuoteAcceptance_Proof", "UnderwritingCycle_TermsPointers" })
            migration.Sql($"DROP TRIGGER TR_{name};");
    }
}
