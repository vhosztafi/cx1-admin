using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CapacityEscalationStorage
{
    private static void AddCapacityGuards(MigrationBuilder migration)
    {
        foreach (var table in new[] { "CapacitySubmission", "CapacitySubmissionEvidence", "CapacityMessage" })
            migration.Sql($"CREATE TRIGGER TR_{table}_AppendOnly ON [{table}] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51140, 'Capacity submission and correspondence are append-only.', 1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_CapacityEscalation_Immutable ON CapacityEscalation AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF NOT EXISTS(SELECT 1 FROM inserted) OR UPDATE(Id) OR UPDATE(QuoteId) OR UPDATE(CycleId) OR UPDATE(ReferralId) OR UPDATE(ProviderId) OR UPDATE(BinderVersionId) OR UPDATE(RaisedBy) OR UPDATE(Reason) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy)
              THROW 51141, 'Capacity escalation provenance is immutable.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CapacityEscalation_Owner ON CapacityEscalation AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN UnderwritingCycle c ON c.Id=i.CycleId JOIN BinderVersion b ON b.Id=i.BinderVersionId
              WHERE i.BinderVersionId<>c.BinderVersionId OR b.ProviderId<>i.ProviderId)
              THROW 51142, 'Capacity escalation requires the retained binder and provider.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CapacityEscalation_Pointers ON CapacityEscalation AFTER INSERT,UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CapacityMessage m ON m.Id=i.CurrentResponseId WHERE m.Direction<>'inbound' OR m.ApplicationState<>'applied')
              THROW 51143, 'Current response requires an applicable inbound message.', 1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id LEFT JOIN CapacitySubmission n ON n.Id=i.CurrentSubmissionId LEFT JOIN CapacitySubmission p ON p.Id=d.CurrentSubmissionId
              WHERE d.CurrentSubmissionId IS NOT NULL AND (i.CurrentSubmissionId IS NULL OR n.Sequence<p.Sequence))
              THROW 51144, 'Capacity submission cannot be rewound.', 1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id LEFT JOIN CapacityMessage n ON n.Id=i.CurrentResponseId LEFT JOIN CapacityMessage p ON p.Id=d.CurrentResponseId
              WHERE i.CurrentSubmissionId=d.CurrentSubmissionId AND d.CurrentResponseId IS NOT NULL AND (i.CurrentResponseId IS NULL OR n.Sequence<p.Sequence))
              THROW 51145, 'Capacity response cannot be rewound.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CapacityMessage_Context ON CapacityMessage AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CapacityEscalation e ON e.Id=i.EscalationId JOIN CapacitySubmission s ON s.Id=i.SubmissionId
              WHERE i.ProviderId<>e.ProviderId OR i.ReferralId<>e.ReferralId OR i.RecordedAt<s.SubmittedAt OR i.ReceivedAt<s.SubmittedAt)
              THROW 51146, 'Capacity correspondence requires its exact submitted context.', 1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN UnderwritingEvidenceAssociation a ON a.Id=i.EvidenceAssociationId JOIN UnderwritingEvidenceEvent r ON r.Id=i.EvidenceReviewId
              WHERE i.Provenance='supplied-response' AND (a.CapacitySubmissionId IS NULL OR a.CapacitySubmissionId<>i.SubmissionId OR a.WithdrawnEventId IS NOT NULL OR a.LatestReviewId IS NULL OR a.LatestReviewId<>r.Id OR r.Kind<>'review' OR r.Outcome<>'accepted'))
              THROW 51147, 'Supplied capacity response requires current reviewed submission proof.', 1;
            IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN QuoteReferralDecision d ON d.Id=i.DecisionId
              WHERE i.DecisionId IS NOT NULL AND (i.Outcome<>'approve-with-conditions' OR d.Outcome<>'approve-with-conditions'))
              THROW 51148, 'Carrier conditions require an explicit conditional decision.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_CapacitySubmissionEvidence_Context ON CapacitySubmissionEvidence AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CapacitySubmission s ON s.Id=i.SubmissionId JOIN UnderwritingEvidenceAssociation a ON a.Id=i.EvidenceAssociationId
              WHERE a.WithdrawnEventId IS NOT NULL OR NOT EXISTS(SELECT 1 FROM OPENJSON(s.ContextJson,'$.evidenceAssociationIds') p WHERE TRY_CONVERT(uniqueidentifier,p.value)=i.EvidenceAssociationId))
              THROW 51149, 'Submitted evidence must belong to the immutable request projection.', 1;
            END;
            """);
        migration.Sql("CREATE TRIGGER TR_UnderwritingEvidenceAssociation_CapacityImmutable ON UnderwritingEvidenceAssociation AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF UPDATE(CapacitySubmissionId) THROW 51150, 'Capacity proof submission is immutable.', 1; END;");
    }
    private static void RemoveCapacityGuards(MigrationBuilder migration)
    {
        foreach (var table in new[] { "CapacitySubmission", "CapacitySubmissionEvidence", "CapacityMessage" }) migration.Sql($"DROP TRIGGER TR_{table}_AppendOnly;");
        migration.Sql("DROP TRIGGER TR_CapacityEscalation_Immutable; DROP TRIGGER TR_CapacityEscalation_Owner; DROP TRIGGER TR_CapacityEscalation_Pointers; DROP TRIGGER TR_CapacityMessage_Context; DROP TRIGGER TR_CapacitySubmissionEvidence_Context; DROP TRIGGER TR_UnderwritingEvidenceAssociation_CapacityImmutable;");
    }
}
