using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureCapacity(ModelBuilder model)
    {
        var escalation = Record<CapacityEscalation>(model, "CapacityEscalation"); escalation.ToTable(t => t.UseSqlOutputClause(false));
        Text(escalation, ("Reason", 2000), ("State", 20));
        escalation.HasAlternateKey(x => new { x.Id, x.CycleId, x.QuoteId });
        escalation.HasIndex(x => x.ReferralId).IsUnique();
        escalation.HasOne<QuoteReferral>().WithMany().HasForeignKey(x => new { x.ReferralId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        escalation.HasOne<CapacityProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.NoAction);
        escalation.HasOne<BinderVersion>().WithMany().HasForeignKey(x => x.BinderVersionId).OnDelete(DeleteBehavior.NoAction);
        escalation.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.RaisedBy).OnDelete(DeleteBehavior.NoAction);
        Check(escalation, "State", "[State] IN ('draft','queued','sent','queried','approved','conditional','declined','superseded','failed')");
        Check(escalation, "Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[RaisedBy]");
        Check(escalation, "ResponseOwner", "[CurrentResponseId] IS NULL OR [CurrentSubmissionId] IS NOT NULL");

        var submission = Record<CapacitySubmission>(model, "CapacitySubmission"); submission.ToTable(t => t.UseSqlOutputClause(false));
        Text(submission, ("Body", 8000), ("ContextHash", 64)); UnderwritingJson(submission, "ContextJson");
        submission.Property(x => x.ContextHash).UseCollation("Latin1_General_100_BIN2");
        submission.HasAlternateKey(x => new { x.Id, x.CycleId, x.QuoteId });
        submission.HasAlternateKey(x => new { x.Id, x.EscalationId, x.CycleId, x.QuoteId });
        submission.HasIndex(x => new { x.EscalationId, x.Sequence }).IsUnique(); submission.HasIndex(x => x.WorkId).IsUnique();
        submission.HasOne<CapacityEscalation>().WithMany().HasForeignKey(x => new { x.EscalationId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.NoAction);
        Check(submission, "Sequence", "[Sequence]>0");
        Check(submission, "ContextHash", "LEN([ContextHash])=64 AND [ContextHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
        Check(submission, "Provenance", "LEN(TRIM([Body]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[SubmittedBy] AND [SubmittedAt]>=[CreatedAt] AND [ResponseDueAt]>[SubmittedAt]");
        escalation.HasOne<CapacitySubmission>().WithMany().HasForeignKey(x => new { x.CurrentSubmissionId, x.Id, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.EscalationId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);

        var proof = Record<CapacitySubmissionEvidence>(model, "CapacitySubmissionEvidence"); proof.ToTable(t => t.UseSqlOutputClause(false));
        proof.HasIndex(x => new { x.SubmissionId, x.EvidenceAssociationId }).IsUnique();
        proof.HasOne<CapacitySubmission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        proof.HasOne<UnderwritingEvidenceAssociation>().WithMany().HasForeignKey(x => new { x.EvidenceAssociationId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);

        var message = Record<CapacityMessage>(model, "CapacityMessage"); message.ToTable(t => t.UseSqlOutputClause(false));
        Text(message, ("Direction", 10), ("Provenance", 30), ("Outcome", 30), ("ProviderUnderwriter", 200), ("ProviderReference", 100), ("ProviderEventId", 100), ("Body", 8000), ("ApplicationState", 20));
        UnderwritingJson(message, "DefinitionJson"); message.Property(x => x.ContentHash).HasMaxLength(32);
        message.HasAlternateKey(x => new { x.Id, x.SubmissionId, x.EscalationId, x.CycleId, x.QuoteId });
        message.HasIndex(x => new { x.EscalationId, x.Sequence }).IsUnique();
        message.HasIndex(x => new { x.ProviderId, x.ProviderEventId }).IsUnique().HasFilter("[ProviderEventId] IS NOT NULL");
        message.HasOne<CapacitySubmission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.EscalationId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.EscalationId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        message.HasOne<QuoteReferral>().WithMany().HasForeignKey(x => new { x.ReferralId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        message.HasOne<CapacityProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.NoAction);
        message.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.RecordedBy).OnDelete(DeleteBehavior.NoAction);
        message.HasOne<AdapterInbox>().WithMany().HasForeignKey(x => x.InboxId).OnDelete(DeleteBehavior.NoAction);
        message.HasOne<UnderwritingEvidenceEvent>().WithMany().HasForeignKey(x => new { x.EvidenceReviewId, x.EvidenceAssociationId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.AssociationId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        message.HasOne<QuoteReferralDecision>().WithMany().HasForeignKey(x => new { x.DecisionId, x.ReferralId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.ReferralId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        Check(message, "Sequence", "[Sequence]>0"); Check(message, "ContentHash", "DATALENGTH([ContentHash])=32");
        Check(message, "Provenance", "LEN(TRIM([Body]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[RecordedBy] AND [RecordedAt]>=[CreatedAt]");
        Check(message, "Application", "[ApplicationState] IN ('applied','superseded')");
        Check(message, "Direction", "([Direction]='outbound' AND [Provenance]='staff-submission' AND [Outcome] IS NULL AND [ReceivedAt] IS NULL AND [EvidenceAssociationId] IS NULL AND [EvidenceReviewId] IS NULL AND [ProviderEventId] IS NULL AND [InboxId] IS NULL AND [DecisionId] IS NULL) OR ([Direction]='inbound' AND [Provenance] IN ('demo-provider','supplied-response') AND [Outcome] IN ('approve','approve-with-conditions','query','decline') AND [ReceivedAt] IS NOT NULL AND [ReceivedAt]<=[RecordedAt] AND [ProviderUnderwriter] IS NOT NULL AND LEN(TRIM([ProviderUnderwriter]))>0 AND [ProviderReference] IS NOT NULL AND LEN(TRIM([ProviderReference]))>0)");
        Check(message, "Evidence", "([Provenance]='supplied-response' AND [EvidenceAssociationId] IS NOT NULL AND [EvidenceReviewId] IS NOT NULL AND [ProviderEventId] IS NULL AND [InboxId] IS NULL) OR ([Provenance]<>'supplied-response' AND [EvidenceAssociationId] IS NULL AND [EvidenceReviewId] IS NULL)");
        Check(message, "ProviderEvent", "([Provenance]='demo-provider' AND [ProviderEventId] IS NOT NULL AND [InboxId] IS NOT NULL) OR ([Provenance]<>'demo-provider' AND [ProviderEventId] IS NULL AND [InboxId] IS NULL)");
        escalation.HasOne<CapacityMessage>().WithMany().HasForeignKey(x => new { x.CurrentResponseId, x.CurrentSubmissionId, x.Id, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.SubmissionId, x.EscalationId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        model.Entity<UnderwritingEvidenceAssociation>().HasOne<CapacitySubmission>().WithMany().HasForeignKey(x => new { x.CapacitySubmissionId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        Check(model.Entity<UnderwritingEvidenceAssociation>(), "CapacityPurpose", "([CapacitySubmissionId] IS NULL AND [RequirementCode]<>'capacity-response') OR ([CapacitySubmissionId] IS NOT NULL AND [RequirementCode]='capacity-response' AND [ConditionId] IS NULL AND [RiskItemId] IS NULL AND [TermsVersionId] IS NULL)");
    }
}
