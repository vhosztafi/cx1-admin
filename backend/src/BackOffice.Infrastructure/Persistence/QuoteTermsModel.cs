using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureQuoteTerms(ModelBuilder model)
    {
        var template = Record<TemplateVersion>(model, "TemplateVersion"); template.ToTable(t => t.UseSqlOutputClause(false));
        Text(template, ("Code", 60), ("Kind", 30), ("State", 20)); UnderwritingJson(template, "ContentJson");
        template.HasIndex(x => new { x.Code, x.ProductId, x.Version }).IsUnique();
        template.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
        Check(template, "Version", "[Version]>0"); Check(template, "State", "[State] IN ('published','retired')");
        Check(template, "Kind", "[Kind] IN ('quote-terms','servicing-terms','policy-schedule','policy-certificate','policy-statement')"); Check(template, "Interval", "[EffectiveFrom]<[EffectiveTo]");

        var terms = Record<QuoteTermsVersion>(model, "QuoteTermsVersion"); terms.ToTable(t => t.UseSqlOutputClause(false));
        Text(terms, ("TermsHash", 64), ("AssuranceHashAtPreparation", 64)); UnderwritingJson(terms, "TermsJson");
        terms.HasAlternateKey(x => new { x.Id, x.CycleId, x.QuoteId });
        terms.HasAlternateKey(x => new { x.Id, x.RatingId, x.CycleId, x.QuoteId });
        terms.HasIndex(x => new { x.CycleId, x.Number }).IsUnique();
        terms.HasOne<QuoteRatingResult>().WithMany().HasForeignKey(x => new { x.RatingId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        terms.HasOne<TemplateVersion>().WithMany().HasForeignKey(x => x.TemplateVersionId).OnDelete(DeleteBehavior.NoAction);
        terms.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.PreparedBy).OnDelete(DeleteBehavior.NoAction);
        Check(terms, "Number", "[Number]>0");
        Check(terms, "Provenance", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[PreparedBy] AND [PreparedAt]=[CreatedAt]");
        foreach (var property in new[] { "TermsHash", "AssuranceHashAtPreparation" })
        { terms.Property<string>(property).UseCollation("Latin1_General_100_BIN2"); Check(terms, property, $"LEN([{property}])=64 AND [{property}] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2"); }

        var delivery = Record<QuoteTermsDelivery>(model, "QuoteTermsDelivery"); delivery.ToTable(t => t.UseSqlOutputClause(false));
        Text(delivery, ("State", 20), ("PayloadHash", 64), ("AssuranceHashAtSend", 64), ("OutcomeCode", 100));
        Check(delivery, "RecipientSnapshotJson", "ISJSON([RecipientSnapshotJson],ARRAY)=1 AND DATALENGTH(CONVERT(varchar(max),[RecipientSnapshotJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576"); UnderwritingJson(delivery, "PayloadJson");
        delivery.HasAlternateKey(x => new { x.Id, x.TermsVersionId, x.CycleId, x.QuoteId });
        delivery.HasIndex(x => x.WorkId).IsUnique(); delivery.HasIndex(x => new { x.QuoteId, x.CreatedAt, x.Id });
        delivery.HasOne<QuoteTermsVersion>().WithMany().HasForeignKey(x => new { x.TermsVersionId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.SentBy).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<DemoProviderOperation>().WithMany().HasForeignKey(x => x.ProviderOperationId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<AdapterAttempt>().WithMany().HasForeignKey(x => new { x.AttemptId, x.WorkId }).HasPrincipalKey(x => new { x.Id, x.WorkId }).OnDelete(DeleteBehavior.NoAction);
        Check(delivery, "State", "[State] IN ('queued','delivered','failed','superseded')");
        Check(delivery, "Completion", "([State]='queued' AND [CompletedAt] IS NULL) OR ([State]<>'queued' AND [CompletedAt] IS NOT NULL AND [CompletedAt]>=[CreatedAt])");
        Check(delivery, "Delivered", "[State]<>'delivered' OR ([ProviderOperationId] IS NOT NULL AND [AttemptId] IS NOT NULL)");
        Check(delivery, "Provenance", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[SentBy]");
        foreach (var property in new[] { "PayloadHash", "AssuranceHashAtSend" })
        { delivery.Property<string>(property).UseCollation("Latin1_General_100_BIN2"); Check(delivery, property, $"LEN([{property}])=64 AND [{property}] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2"); }

        var acceptance = Record<QuoteAcceptance>(model, "QuoteAcceptance"); acceptance.ToTable(t => t.UseSqlOutputClause(false));
        Text(acceptance, ("TermsHash", 64), ("AssuranceHash", 64), ("AccepterLabel", 200), ("Channel", 20));
        acceptance.HasAlternateKey(x => new { x.Id, x.CycleId, x.QuoteId });
        acceptance.HasIndex(x => new { x.QuoteId, x.RecordedAt, x.Id });
        acceptance.HasOne<QuoteTermsVersion>().WithMany().HasForeignKey(x => new { x.TermsVersionId, x.RatingId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.RatingId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        acceptance.HasOne<QuoteTermsDelivery>().WithMany().HasForeignKey(x => new { x.DeliveryId, x.TermsVersionId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.TermsVersionId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        acceptance.HasOne<UnderwritingEvidenceEvent>().WithMany().HasForeignKey(x => new { x.EvidenceReviewId, x.EvidenceAssociationId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.AssociationId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        acceptance.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.RecordedBy).OnDelete(DeleteBehavior.NoAction);
        Check(acceptance, "Channel", "[Channel] IN ('email','written','telephone')");
        Check(acceptance, "Provenance", "LEN(TRIM([AccepterLabel]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[RecordedBy] AND [RecordedAt]=[CreatedAt] AND [AcceptedAt]<=[RecordedAt]");
        foreach (var property in new[] { "TermsHash", "AssuranceHash" })
        { acceptance.Property<string>(property).UseCollation("Latin1_General_100_BIN2"); Check(acceptance, property, $"LEN([{property}])=64 AND [{property}] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2"); }

        model.Entity<UnderwritingCycle>().HasOne<QuoteTermsVersion>().WithMany().HasForeignKey(x => new { x.CurrentTermsVersionId, x.Id, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        model.Entity<UnderwritingCycle>().HasOne<QuoteTermsDelivery>().WithMany().HasForeignKey(x => new { x.CurrentDeliveryId, x.CurrentTermsVersionId, x.Id, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.TermsVersionId, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        model.Entity<UnderwritingCycle>().HasOne<QuoteAcceptance>().WithMany().HasForeignKey(x => new { x.CurrentAcceptanceId, x.Id, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        Check(model.Entity<UnderwritingCycle>(), "TermsPointers", "([CurrentDeliveryId] IS NULL AND [CurrentAcceptanceId] IS NULL) OR [CurrentTermsVersionId] IS NOT NULL");
        model.Entity<UnderwritingEvidenceAssociation>().HasOne<QuoteTermsVersion>().WithMany().HasForeignKey(x => new { x.TermsVersionId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        Check(model.Entity<UnderwritingEvidenceAssociation>(), "PreparedTermsOwner", "([TermsVersionId] IS NULL AND [RequirementCode] NOT IN ('signed-statement','acceptance-proof')) OR ([TermsVersionId] IS NOT NULL AND [RequirementCode] IN ('signed-statement','acceptance-proof'))");
    }
}
