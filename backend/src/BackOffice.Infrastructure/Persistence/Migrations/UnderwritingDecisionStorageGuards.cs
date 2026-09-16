using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class UnderwritingDecisionStorage
{
    private static void AddDecisionGuards(MigrationBuilder migration)
    {
        foreach (var table in new[] { "QuoteReferralDecision", "UnderwritingEvidenceEvent", "QuoteConditionResolution" })
            migration.Sql($"CREATE TRIGGER TR_{table}_AuthorityOwner ON [{table}] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i JOIN UnderwritingCycle c ON c.Id=i.CycleId JOIN AuthorityVersion a ON a.Id=i.AuthorityVersionId WHERE a.ProductId<>c.ProductId OR a.ProductVersionId<>c.ProductVersionId OR a.BinderVersionId<>c.BinderVersionId) THROW 51129, 'Decision authority belongs to another product or binder.', 1; END;");
        foreach (var table in new[] { "QuoteReferralDecision", "QuoteConditionResolution", "UnderwritingEvidenceEvent" })
            migration.Sql($"CREATE TRIGGER TR_{table}_AppendOnly ON [{table}] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51120, 'Underwriting decisions and evidence events are append-only.', 1; END;");
        foreach (var (table, columns) in new[] {
            ("QuoteCondition", "DecisionId,ReferralId,CycleId,QuoteId,Sequence,Kind,Code,DefinitionJson,Wording,EndorsementCode"),
            ("UnderwritingEvidenceAssociation", "QuoteId,CycleId,FileId,RequirementCode,RiskItemId,ConditionId,TermsVersionId,InputFingerprint,Reason") })
        {
            var guard = string.Join(" OR ", ("Id,CreatedAt,CreatedBy," + columns).Split(',').Select(x => "UPDATE(" + x + ")"));
            migration.Sql($"CREATE TRIGGER TR_{table}_InputImmutable ON [{table}] AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF {guard} THROW 51121, 'Underwriting decision provenance is immutable.', 1; END;");
            migration.Sql($"CREATE TRIGGER TR_{table}_NoDelete ON [{table}] AFTER DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51122, 'Underwriting decision history cannot be deleted.', 1; END;");
        }
        migration.Sql("""
            CREATE TRIGGER TR_UnderwritingEvidenceAssociation_Pointers ON UnderwritingEvidenceAssociation AFTER INSERT,UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN UnderwritingEvidenceEvent r ON r.Id=i.LatestReviewId LEFT JOIN UnderwritingEvidenceEvent w ON w.Id=i.WithdrawnEventId
              WHERE (i.LatestReviewId IS NOT NULL AND r.Kind<>'review') OR (i.WithdrawnEventId IS NOT NULL AND w.Kind<>'withdrawal'))
              THROW 51123, 'Evidence pointers require the matching event kind.', 1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id LEFT JOIN UnderwritingEvidenceEvent n ON n.Id=i.LatestReviewId LEFT JOIN UnderwritingEvidenceEvent p ON p.Id=d.LatestReviewId
              WHERE (d.LatestReviewId IS NOT NULL AND (i.LatestReviewId IS NULL OR n.Sequence<p.Sequence)) OR
              (d.WithdrawnEventId IS NOT NULL AND (i.WithdrawnEventId IS NULL OR i.WithdrawnEventId<>d.WithdrawnEventId OR ISNULL(i.LatestReviewId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.LatestReviewId,'00000000-0000-0000-0000-000000000000'))))
              THROW 51124, 'Evidence history cannot be rewound or restored after withdrawal.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_QuoteReferral_DecisionPointer ON QuoteReferral AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id LEFT JOIN QuoteReferralDecision n ON n.Id=i.LatestDecisionId LEFT JOIN QuoteReferralDecision p ON p.Id=d.LatestDecisionId
              WHERE d.LatestDecisionId IS NOT NULL AND (i.LatestDecisionId IS NULL OR n.Sequence<p.Sequence))
              THROW 51125, 'Referral decision history cannot be rewound.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_QuoteCondition_ResolutionPointer ON QuoteCondition AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id LEFT JOIN QuoteConditionResolution n ON n.Id=i.LatestResolutionId LEFT JOIN QuoteConditionResolution p ON p.Id=d.LatestResolutionId
              WHERE d.LatestResolutionId IS NOT NULL AND (i.LatestResolutionId IS NULL OR n.Sequence<p.Sequence))
              THROW 51126, 'Condition resolution history cannot be rewound.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_UnderwritingEvidenceEvent_Context ON UnderwritingEvidenceEvent AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN UnderwritingEvidenceAssociation a ON a.Id=i.AssociationId
              WHERE i.InputFingerprint<>a.InputFingerprint COLLATE Latin1_General_100_BIN2 OR i.RecordedAt<a.CreatedAt OR a.WithdrawnEventId IS NOT NULL)
              THROW 51127, 'Evidence event requires current exact proof.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_QuoteConditionResolution_Proof ON QuoteConditionResolution AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN QuoteCondition c ON c.Id=i.ConditionId JOIN UnderwritingEvidenceEvent r ON r.Id=i.EvidenceReviewId JOIN UnderwritingEvidenceAssociation a ON a.Id=i.EvidenceAssociationId
              WHERE c.Kind='risk-change' OR r.Kind<>'review' OR a.WithdrawnEventId IS NOT NULL OR a.LatestReviewId IS NULL OR a.LatestReviewId<>r.Id OR (i.Outcome='satisfied' AND r.Outcome<>'accepted'))
              THROW 51128, 'Condition resolution requires current reviewed proof and cannot change risk.', 1;
            END;
            """);
    }

    private static void RemoveDecisionGuards(MigrationBuilder migration)
    {
        foreach (var table in new[] { "QuoteReferralDecision", "UnderwritingEvidenceEvent", "QuoteConditionResolution" }) migration.Sql($"DROP TRIGGER TR_{table}_AuthorityOwner;");
        foreach (var table in new[] { "QuoteReferralDecision", "QuoteConditionResolution", "UnderwritingEvidenceEvent" }) migration.Sql($"DROP TRIGGER TR_{table}_AppendOnly;");
        foreach (var table in new[] { "QuoteCondition", "UnderwritingEvidenceAssociation" }) migration.Sql($"DROP TRIGGER TR_{table}_InputImmutable; DROP TRIGGER TR_{table}_NoDelete;");
        migration.Sql("DROP TRIGGER TR_UnderwritingEvidenceAssociation_Pointers; DROP TRIGGER TR_QuoteReferral_DecisionPointer; DROP TRIGGER TR_QuoteCondition_ResolutionPointer; DROP TRIGGER TR_UnderwritingEvidenceEvent_Context; DROP TRIGGER TR_QuoteConditionResolution_Proof;");
    }
}
