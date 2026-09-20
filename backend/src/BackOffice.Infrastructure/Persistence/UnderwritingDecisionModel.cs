using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureUnderwritingDecisions(ModelBuilder model)
    {
        var decision = Record<QuoteReferralDecision>(model, "QuoteReferralDecision"); decision.ToTable(t => t.UseSqlOutputClause(false));
        Text(decision, ("Outcome", 30), ("Reason", 2000), ("Question", 2000));
        decision.HasAlternateKey(x => new { x.Id, x.ReferralId, x.CycleId, x.QuoteId });
        decision.HasIndex(x => new { x.ReferralId, x.Sequence }).IsUnique();
        decision.HasOne<QuoteReferral>().WithMany().HasForeignKey(x => new { x.ReferralId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<AuthorityVersion>().WithMany().HasForeignKey(x => x.AuthorityVersionId).OnDelete(DeleteBehavior.NoAction);
        Check(decision, "Sequence", "[Sequence]>0");
        Check(decision, "Outcome", "[Outcome] IN ('approve','approve-with-conditions','query','decline','reopen')");
        Check(decision, "Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND [DecidedAt]>=[CreatedAt]");
        Check(decision, "Question", "([Outcome]='query' AND [Question] IS NOT NULL AND LEN(TRIM([Question]))>0) OR ([Outcome]<>'query' AND [Question] IS NULL)");
        Check(decision, "Conditions", "ISJSON([ConditionsJson],ARRAY)=1 AND DATALENGTH([ConditionsJson])<=131072 AND (([Outcome] IN ('approve-with-conditions','query') AND JSON_QUERY([ConditionsJson],'$[0]') IS NOT NULL) OR ([Outcome] NOT IN ('approve-with-conditions','query') AND [ConditionsJson]='[]'))");
        model.Entity<QuoteReferral>().HasOne<QuoteReferralDecision>().WithMany().HasForeignKey(x => new { x.LatestDecisionId, x.Id, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.ReferralId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);

        var condition = Record<QuoteCondition>(model, "QuoteCondition"); condition.ToTable(t => t.UseSqlOutputClause(false));
        Text(condition, ("Kind", 20), ("Code", 60), ("Wording", 8000), ("EndorsementCode", 60)); UnderwritingJson(condition, "DefinitionJson");
        condition.HasAlternateKey(x => new { x.Id, x.CycleId, x.QuoteId }); condition.HasIndex(x => new { x.DecisionId, x.Sequence }).IsUnique();
        condition.HasOne<QuoteReferralDecision>().WithMany().HasForeignKey(x => new { x.DecisionId, x.ReferralId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.ReferralId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        Check(condition, "Sequence", "[Sequence]>0");
        Check(condition, "Definition", "COALESCE(JSON_VALUE([DefinitionJson],'$.code'),'')=[Code] AND (([Kind]='documentary' AND [Code] IN ('provide-driver-proof','provide-premises-security','provide-signed-statement','provide-trading-history','provide-cc-property-proof','provide-cc-location-proof','provide-cc-liability-proof','provide-cc-wage-proof','provide-cc-bi-proof','provide-cc-business-proof','provide-cc-claims-experience-proof','provide-cc-health-safety-proof','provide-cc-electrical-proof','provide-cc-alarm-proof','provide-cc-structural-proof') AND [EndorsementCode] IS NULL) OR ([Kind]='warranty' AND [Code] IN ('overnight-security','named-drivers-only','any-driver-minimum-licence') AND [EndorsementCode] IS NOT NULL AND LEN(TRIM([Wording]))>0) OR ([Kind]='risk-change' AND [Code] IN ('revise-stock-limit','revise-vehicle-limit') AND [EndorsementCode] IS NULL))");
        Check(condition, "SecurityCode", "[Code]<>'overnight-security' OR [EndorsementCode]='W-07'");

        var evidence = Record<UnderwritingEvidenceAssociation>(model, "UnderwritingEvidenceAssociation"); evidence.ToTable(t => t.UseSqlOutputClause(false));
        Text(evidence, ("RequirementCode", 60), ("InputFingerprint", 64), ("Reason", 2000)); evidence.Property(x => x.InputFingerprint).UseCollation("Latin1_General_100_BIN2");
        evidence.HasAlternateKey(x => new { x.Id, x.CycleId, x.QuoteId }); evidence.HasIndex(x => new { x.CycleId, x.CreatedAt, x.Id });
        evidence.HasOne<UnderwritingCycle>().WithMany().HasForeignKey(x => new { x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        evidence.HasOne<QuoteEvidenceFile>().WithMany().HasForeignKey(x => new { x.FileId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        evidence.HasOne<QuoteCondition>().WithMany().HasForeignKey(x => new { x.ConditionId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        Check(evidence, "Fingerprint", "LEN([InputFingerprint])=64 AND [InputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
        Check(evidence, "Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL");
        Check(evidence, "Purpose", "[RequirementCode] IN ('motor-trader-proof','no-claims-proof','photocard-both-sides','driving-record','premises-security','trading-history','signed-statement','warranty-acknowledgement','acceptance-proof','capacity-response','cc-property-proof','cc-location-proof','cc-liability-proof','cc-wage-proof','cc-bi-proof','cc-business-proof','cc-claims-experience-proof','cc-health-safety-proof','cc-electrical-proof','cc-alarm-proof','cc-structural-proof')");

        var review = Record<UnderwritingEvidenceEvent>(model, "UnderwritingEvidenceEvent"); review.ToTable(t => t.UseSqlOutputClause(false));
        Text(review, ("Kind", 20), ("Outcome", 20), ("Reason", 2000), ("InputFingerprint", 64)); review.Property(x => x.InputFingerprint).UseCollation("Latin1_General_100_BIN2");
        review.HasAlternateKey(x => new { x.Id, x.AssociationId, x.CycleId, x.QuoteId }); review.HasIndex(x => new { x.AssociationId, x.Sequence }).IsUnique();
        review.HasOne<UnderwritingEvidenceAssociation>().WithMany().HasForeignKey(x => new { x.AssociationId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        review.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        review.HasOne<AuthorityVersion>().WithMany().HasForeignKey(x => x.AuthorityVersionId).OnDelete(DeleteBehavior.NoAction);
        Check(review, "Sequence", "[Sequence]>0");
        Check(review, "Outcome", "([Kind]='review' AND [Outcome] IS NOT NULL AND [Outcome] IN ('accepted','rejected') AND [AuthorityVersionId] IS NOT NULL) OR ([Kind]='withdrawal' AND [Outcome] IS NULL AND [AuthorityVersionId] IS NULL)");
        Check(review, "Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND [RecordedAt]>=[CreatedAt]");
        Check(review, "Fingerprint", "LEN([InputFingerprint])=64 AND [InputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
        evidence.HasOne<UnderwritingEvidenceEvent>().WithMany().HasForeignKey(x => new { x.LatestReviewId, x.Id, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.AssociationId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        evidence.HasOne<UnderwritingEvidenceEvent>().WithMany().HasForeignKey(x => new { x.WithdrawnEventId, x.Id, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.AssociationId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);

        var resolution = Record<QuoteConditionResolution>(model, "QuoteConditionResolution"); resolution.ToTable(t => t.UseSqlOutputClause(false));
        Text(resolution, ("Outcome", 20), ("Reason", 2000));
        resolution.HasAlternateKey(x => new { x.Id, x.ConditionId, x.CycleId, x.QuoteId }); resolution.HasIndex(x => new { x.ConditionId, x.Sequence }).IsUnique();
        resolution.HasOne<QuoteCondition>().WithMany().HasForeignKey(x => new { x.ConditionId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        resolution.HasOne<UnderwritingEvidenceEvent>().WithMany().HasForeignKey(x => new { x.EvidenceReviewId, x.EvidenceAssociationId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.AssociationId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        resolution.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        resolution.HasOne<AuthorityVersion>().WithMany().HasForeignKey(x => x.AuthorityVersionId).OnDelete(DeleteBehavior.NoAction);
        Check(resolution, "Sequence", "[Sequence]>0"); Check(resolution, "Outcome", "[Outcome] IN ('satisfied','rejected')");
        Check(resolution, "Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND [RecordedAt]>=[CreatedAt]");
        condition.HasOne<QuoteConditionResolution>().WithMany().HasForeignKey(x => new { x.LatestResolutionId, x.Id, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.ConditionId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
    }
}
