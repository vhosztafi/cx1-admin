using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigurePolicies(ModelBuilder model)
    {
        model.HasSequence<long>("PolicyReferenceSequence").StartsAt(1).IncrementsBy(1).HasMax(9999999999);
        var policy = Record<Policy>(model, "Policy"); policy.ToTable(t => t.UseSqlOutputClause(false)); Text(policy, ("Reference", 40));
        policy.Property(x => x.Number).HasDefaultValueSql("NEXT VALUE FOR [PolicyReferenceSequence]");
        policy.Property(x => x.Reference).HasComputedColumnSql("'PL-MT-' + RIGHT('0000000000' + CONVERT(varchar(20), [Number]), 10)", stored: true);
        policy.HasIndex(x => x.Number).IsUnique(); policy.HasIndex(x => x.Reference).IsUnique(); policy.HasIndex(x => x.SourceQuoteId).IsUnique();
        policy.HasIndex(x => new { x.AgencyId, x.CreatedAt, x.Id }); policy.HasIndex(x => new { x.ClientId, x.CreatedAt, x.Id });
        policy.HasAlternateKey(x => new { x.Id, x.SourceQuoteId }); policy.HasAlternateKey(x => new { x.Id, x.ProductId });
        policy.HasOne<Quote>().WithMany().HasForeignKey(x => new { x.SourceQuoteId, x.AgencyId, x.ProductId }).HasPrincipalKey(x => new { x.Id, x.AgencyId, x.ProductId }).OnDelete(DeleteBehavior.NoAction);
        policy.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x => new { x.RelationshipId, x.ClientId, x.AgencyId }).HasPrincipalKey(x => new { x.Id, x.ClientId, x.AgencyId }).OnDelete(DeleteBehavior.NoAction);
        policy.HasOne<PolicyTerm>().WithMany().HasForeignKey(x => new { x.CurrentTermId, x.Id }).HasPrincipalKey(x => new { x.Id, x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        Check(policy, "Number", "[Number] BETWEEN 1 AND 9999999999"); Check(policy, "Creator", "[CreatedBy] IS NOT NULL");
        model.Entity<Quote>().HasOne<Policy>().WithMany().HasForeignKey(x => new { x.BoundPolicyId, x.Id }).HasPrincipalKey(x => new { x.Id, x.SourceQuoteId }).OnDelete(DeleteBehavior.NoAction);
        Check(model.Entity<Quote>(), "BoundPolicy", "([State]='bound' AND [BoundPolicyId] IS NOT NULL) OR ([State]<>'bound' AND [BoundPolicyId] IS NULL)");

        var term = Record<PolicyTerm>(model, "PolicyTerm"); term.ToTable(t => t.UseSqlOutputClause(false)); UnderwritingJson(term, "LocalTermIntentJson");
        term.HasAlternateKey(x => new { x.Id, x.PolicyId }); term.HasIndex(x => new { x.PolicyId, x.Number }).IsUnique();
        term.HasOne<Policy>().WithMany().HasForeignKey(x => new { x.PolicyId, x.ProductId }).HasPrincipalKey(x => new { x.Id, x.ProductId }).OnDelete(DeleteBehavior.NoAction);
        term.HasOne<ProductVersion>().WithMany().HasForeignKey(x => new { x.ProductVersionId, x.ProductId }).HasPrincipalKey(x => new { x.Id, x.ProductId }).OnDelete(DeleteBehavior.NoAction);
        term.HasOne<PolicyVersion>().WithMany().HasForeignKey(x => new { x.CurrentVersionId, x.Id, x.PolicyId }).HasPrincipalKey(x => new { x.Id, x.TermId, x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        Check(term, "Number", "[Number]>0"); Check(term, "Interval", "[StartsAt]<[EndsAt]");

        var transaction = Record<PolicyTransaction>(model, "PolicyTransaction"); transaction.ToTable(t => t.UseSqlOutputClause(false));
        Text(transaction, ("Kind", 30), ("Reason", 2000), ("OperationKey", 200)); transaction.Property(x => x.OperationKey).UseCollation("Latin1_General_100_BIN2");
        transaction.HasAlternateKey(x => new { x.Id, x.TermId, x.PolicyId }); transaction.HasIndex(x => new { x.TermId, x.Sequence }).IsUnique();
        transaction.HasIndex(x => x.PolicyId).IsUnique().HasFilter("[Kind]='new-business'"); transaction.HasIndex(x => x.OperationKey).IsUnique();
        transaction.HasOne<Policy>().WithMany().HasForeignKey(x => new { x.PolicyId, x.SourceQuoteId }).HasPrincipalKey(x => new { x.Id, x.SourceQuoteId }).OnDelete(DeleteBehavior.NoAction);
        transaction.HasOne<PolicyTerm>().WithMany().HasForeignKey(x => new { x.TermId, x.PolicyId }).HasPrincipalKey(x => new { x.Id, x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        transaction.HasOne<QuoteRevision>().WithMany().HasForeignKey(x => new { x.QuoteRevisionId, x.SourceQuoteId }).HasPrincipalKey(x => new { x.Id, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        transaction.HasOne<QuoteRatingResult>().WithMany().HasForeignKey(x => new { x.RatingId, x.CycleId, x.SourceQuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        transaction.HasOne<QuoteAcceptance>().WithMany().HasForeignKey(x => new { x.AcceptanceId, x.CycleId, x.SourceQuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        transaction.HasOne<ServicingCycle>().WithMany().HasForeignKey(x => new { x.ServicingCycleId, x.ServicingDraftId, x.PolicyId })
            .HasPrincipalKey(x => new { x.Id, x.DraftId, x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        transaction.HasOne<ServicingRatingResult>().WithMany().HasForeignKey(x => new { x.ServicingRatingId, x.ServicingCycleId, x.ServicingDraftId, x.ServicingRevisionId })
            .HasPrincipalKey(x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId }).OnDelete(DeleteBehavior.NoAction);
        model.Entity<ServicingAcceptance>().HasAlternateKey(x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
        transaction.HasOne<ServicingAcceptance>().WithMany().HasForeignKey(x => new { x.ServicingAcceptanceId, x.ServicingCycleId, x.ServicingDraftId, x.ServicingRevisionId, x.ServicingRatingId })
            .HasPrincipalKey(x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId, x.RatingId }).OnDelete(DeleteBehavior.NoAction);
        transaction.HasIndex(x => x.ServicingDraftId).IsUnique().HasFilter("[ServicingDraftId] IS NOT NULL");
        Check(transaction, "Kind", "[Kind] IN ('new-business','adjustment','renewal','cancellation')"); Check(transaction, "Sequence", "[Sequence]>0");
        Check(transaction, "DecisionSource", "([Kind]='new-business' AND [CycleId] IS NOT NULL AND [QuoteRevisionId] IS NOT NULL AND [RatingId] IS NOT NULL AND [AcceptanceId] IS NOT NULL AND [ServicingDraftId] IS NULL AND [ServicingRevisionId] IS NULL AND [ServicingCycleId] IS NULL AND [ServicingRatingId] IS NULL AND [ServicingAcceptanceId] IS NULL) OR ([Kind] IN ('adjustment','renewal') AND [CycleId] IS NULL AND [QuoteRevisionId] IS NULL AND [RatingId] IS NULL AND [AcceptanceId] IS NULL AND [ServicingDraftId] IS NOT NULL AND [ServicingRevisionId] IS NOT NULL AND [ServicingCycleId] IS NOT NULL AND [ServicingRatingId] IS NOT NULL AND [ServicingAcceptanceId] IS NOT NULL) OR ([Kind]='cancellation' AND [CycleId] IS NULL AND [QuoteRevisionId] IS NULL AND [RatingId] IS NULL AND [AcceptanceId] IS NULL AND [ServicingDraftId] IS NOT NULL AND [ServicingRevisionId] IS NOT NULL AND [ServicingCycleId] IS NULL AND [ServicingRatingId] IS NULL AND [ServicingAcceptanceId] IS NULL)");
        Check(transaction, "Provenance", "[CreatedBy] IS NOT NULL AND [ProcessedAt]=[CreatedAt] AND LEN(TRIM([Reason]))>0 AND LEN(TRIM([OperationKey]))>0");

        var version = Record<PolicyVersion>(model, "PolicyVersion"); version.ToTable(t => t.UseSqlOutputClause(false)); Text(version, ("SchemaVersion", 30)); Hash(version, "ContentHash"); UnderwritingJson(version, "SnapshotJson");
        version.HasAlternateKey(x => new { x.Id, x.TermId, x.PolicyId }); version.HasAlternateKey(x => new { x.Id, x.TransactionId, x.TermId, x.PolicyId }); version.HasAlternateKey(x => new { x.Id, x.PolicyId });
        version.HasIndex(x => new { x.TermId, x.Sequence }).IsUnique(); version.HasIndex(x => new { x.TransactionId, x.SliceOrdinal }).IsUnique();
        version.HasOne<PolicyTransaction>().WithMany().HasForeignKey(x => new { x.TransactionId, x.TermId, x.PolicyId }).HasPrincipalKey(x => new { x.Id, x.TermId, x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        Check(version, "Sequence", "[Sequence]>0 AND [SliceOrdinal]>0");
        Check(version, "Schema", "[SchemaVersion]='1.0' AND COALESCE(JSON_VALUE([SnapshotJson],'$.schemaVersion'),'')=[SchemaVersion]");
        Check(version, "Hash", "[ContentHash]<>0x0000000000000000000000000000000000000000000000000000000000000000");

        var registration = model.Entity<PolicyRegistration>(); registration.ToTable("PolicyRegistration", t => t.UseSqlOutputClause(false)); registration.HasKey(x => new { x.VersionId, x.RiskItemId });
        registration.Property(x => x.NormalizedRegistration).HasMaxLength(12).UseCollation("Latin1_General_100_BIN2"); registration.HasIndex(x => new { x.NormalizedRegistration, x.PolicyId });
        registration.HasOne<PolicyVersion>().WithMany().HasForeignKey(x => new { x.VersionId, x.PolicyId }).HasPrincipalKey(x => new { x.Id, x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        Check(registration, "Registration", "LEN([NormalizedRegistration]) BETWEEN 2 AND 12 AND [NormalizedRegistration] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_100_BIN2");
        Check(registration, "RiskItem", "[RiskItemId]<>'00000000-0000-0000-0000-000000000000'");

        var document = Record<PolicyDocumentRequest>(model, "PolicyDocumentRequest"); document.ToTable(t => t.UseSqlOutputClause(false));
        Text(document, ("Kind", 30), ("Purpose", 30), ("State", 20)); UnderwritingJson(document, "PayloadJson"); Hash(document, "PayloadHash");
        document.HasIndex(x => new { x.VersionId, x.Kind, x.TemplateVersionId, x.Purpose }).IsUnique(); document.HasIndex(x => x.WorkId).IsUnique();
        document.HasOne<PolicyVersion>().WithMany().HasForeignKey(x => new { x.VersionId, x.TransactionId, x.TermId, x.PolicyId }).HasPrincipalKey(x => new { x.Id, x.TransactionId, x.TermId, x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        document.HasOne<TemplateVersion>().WithMany().HasForeignKey(x => x.TemplateVersionId).OnDelete(DeleteBehavior.NoAction);
        document.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        Check(document, "Kind", "[Kind] IN ('policy-schedule','policy-certificate','policy-statement')"); Check(document, "Purpose", "[Purpose] IN ('first-issue','adjustment','renewal')");
        Check(document, "State", "[State]='requested'"); Check(document, "Hash", "[PayloadHash]<>0x0000000000000000000000000000000000000000000000000000000000000000");
    }
}
