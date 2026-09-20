using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureUnderwriting(ModelBuilder model)
    {
        var rules = Record<RatingRuleVersion>(model, "RatingRuleVersion");
        ConfigureUnderwritingDefinition(rules, "rating");
        rules.HasIndex(x => new { x.ProductId, x.Version }).IsUnique();
        rules.HasAlternateKey(x => new { x.Id, x.ProductId });
        rules.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
        foreach (var name in new[] { "basePremium", "minimumPremium", "maximumAnnualPremium" })
            Check(rules, name, $"TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.{name}')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.{name}'))>0");

        var binder = Record<BinderVersion>(model, "BinderVersion"); ConfigureUnderwritingDefinition(binder, "binder");
        binder.HasIndex(x => new { x.ProviderId, x.ProductId, x.Version }).IsUnique();
        binder.HasAlternateKey(x => new { x.Id, x.ProductId });
        binder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
        binder.HasOne<CapacityProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.NoAction);
        Check(binder, "Provider", "TRY_CONVERT(uniqueidentifier,JSON_VALUE([DefinitionJson],'$.providerId')) IS NOT NULL AND TRY_CONVERT(uniqueidentifier,JSON_VALUE([DefinitionJson],'$.providerId'))=[ProviderId]");

        var authority = Record<AuthorityVersion>(model, "AuthorityVersion"); ConfigureUnderwritingDefinition(authority, "authority");
        authority.HasIndex(x => new { x.ProductVersionId, x.BinderVersionId, x.Version }).IsUnique();
        authority.HasAlternateKey(x => new { x.Id, x.ProductVersionId, x.BinderVersionId });
        authority.HasOne<ProductVersion>().WithMany().HasForeignKey(x => new { x.ProductVersionId, x.ProductId }).HasPrincipalKey(x => new { x.Id, x.ProductId }).OnDelete(DeleteBehavior.NoAction);
        authority.HasOne<BinderVersion>().WithMany().HasForeignKey(x => new { x.BinderVersionId, x.ProductId }).HasPrincipalKey(x => new { x.Id, x.ProductId }).OnDelete(DeleteBehavior.NoAction);

        var grant = Record<UserAuthorityGrant>(model, "UserAuthorityGrant"); grant.ToTable(t => t.UseSqlOutputClause(false));
        Text(grant, ("Reason", 2000), ("RevocationReason", 2000));
        grant.HasIndex(x => new { x.UserId, x.AuthorityVersionId, x.EffectiveFrom }).IsUnique();
        grant.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        grant.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.GrantedBy).OnDelete(DeleteBehavior.NoAction);
        grant.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.RevokedBy).OnDelete(DeleteBehavior.NoAction);
        grant.HasOne<AuthorityVersion>().WithMany().HasForeignKey(x => x.AuthorityVersionId).OnDelete(DeleteBehavior.NoAction);
        Check(grant, "Interval", "[EffectiveFrom]<[EffectiveTo]");
        Check(grant, "Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[GrantedBy]");
        Check(grant, "Revocation", "([RevokedAt] IS NULL AND [RevokedBy] IS NULL AND [RevocationReason] IS NULL) OR ([RevokedAt] IS NOT NULL AND [RevokedAt]>=[CreatedAt] AND [RevokedBy] IS NOT NULL AND [RevocationReason] IS NOT NULL AND LEN(TRIM([RevocationReason]))>0)");

        model.Entity<QuoteRevision>().HasAlternateKey(x => new { x.Id, x.QuoteId, x.AgencyId, x.ClientId, x.RelationshipId, x.ProductId, x.ProductVersionId, x.AgencyTermsVersionId });
        var cycle = Record<UnderwritingCycle>(model, "UnderwritingCycle"); cycle.ToTable(t => t.UseSqlOutputClause(false));
        Text(cycle, ("State", 20), ("SupersededReason", 2000)); Hash(cycle, "PricingInputHash"); UnderwritingJson(cycle, "InputJson");
        cycle.HasIndex(x => new { x.QuoteId, x.Sequence }).IsUnique(); cycle.HasIndex(x => x.WorkId).IsUnique();
        cycle.HasAlternateKey(x => new { x.Id, x.QuoteId });
        cycle.HasAlternateKey(x => new { x.Id, x.QuoteId, x.WorkId, x.RatingRuleVersionId, x.PricingInputHash });
        cycle.HasOne<QuoteRevision>().WithMany().HasForeignKey(x => new { x.QuoteRevisionId, x.QuoteId, x.AgencyId, x.ClientId, x.RelationshipId, x.ProductId, x.ProductVersionId, x.AgencyTermsVersionId })
            .HasPrincipalKey(x => new { x.Id, x.QuoteId, x.AgencyId, x.ClientId, x.RelationshipId, x.ProductId, x.ProductVersionId, x.AgencyTermsVersionId }).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<RatingRuleVersion>().WithMany().HasForeignKey(x => new { x.RatingRuleVersionId, x.ProductId }).HasPrincipalKey(x => new { x.Id, x.ProductId }).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<AuthorityVersion>().WithMany().HasForeignKey(x => new { x.AuthorityVersionId, x.ProductVersionId, x.BinderVersionId }).HasPrincipalKey(x => new { x.Id, x.ProductVersionId, x.BinderVersionId }).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.RequestedBy).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        Check(cycle, "State", "[State] IN ('rating-pending','rated','failed','superseded','bound')");
        Check(cycle, "Sequence", "[Sequence]>0"); Check(cycle, "Interval", "[StartsAt]<[EndsAt]");
        Check(cycle, "Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RequestedBy]");
        Check(cycle, "Hash", "[PricingInputHash]<>0x0000000000000000000000000000000000000000000000000000000000000000");
        Check(cycle, "Superseded", "([State]='superseded' AND [SupersededAt] IS NOT NULL AND [SupersededAt]>=[CreatedAt] AND [SupersededReason] IS NOT NULL AND LEN(TRIM([SupersededReason]))>0) OR ([State]<>'superseded' AND [SupersededAt] IS NULL AND [SupersededReason] IS NULL)");

        var result = Record<QuoteRatingResult>(model, "QuoteRatingResult"); result.ToTable(t => t.UseSqlOutputClause(false));
        Text(result, ("Outcome", 20)); Hash(result, "InputHash"); UnderwritingJson(result, "ResultJson");
        result.HasAlternateKey(x => new { x.Id, x.CycleId, x.QuoteId });
        result.HasIndex(x => x.WorkId).IsUnique(); result.HasIndex(x => x.AttemptId).IsUnique();
        result.HasOne<UnderwritingCycle>().WithMany().HasForeignKey(x => new { x.CycleId, x.QuoteId, x.WorkId, x.RuleVersionId, x.InputHash })
            .HasPrincipalKey(x => new { x.Id, x.QuoteId, x.WorkId, x.RatingRuleVersionId, x.PricingInputHash }).OnDelete(DeleteBehavior.NoAction);
        model.Entity<AdapterAttempt>().HasAlternateKey(x => new { x.Id, x.WorkId });
        result.HasOne<AdapterAttempt>().WithMany().HasForeignKey(x => new { x.AttemptId, x.WorkId }).HasPrincipalKey(x => new { x.Id, x.WorkId }).OnDelete(DeleteBehavior.NoAction);
        Check(result, "Outcome", "[Outcome] IN ('rated','rejected')");
        Check(result, "Interval", "[CompletedAt]>=[CreatedAt] AND [ExpiresAt]>[CompletedAt]");
        foreach (var name in new[] { "AnnualPremium", "TermPremium", "Tax", "Fee", "GrossPayable", "BrokerCommission" })
        { result.Property<decimal>(name).HasPrecision(19, 2); Check(result, name, $"[{name}] BETWEEN 0 AND 9999999999999.99"); }
        Check(result, "Totals", "[GrossPayable]=[TermPremium]+[Tax]+[Fee] AND [BrokerCommission]<=[TermPremium] AND ([Outcome]<>'rated' OR ([AnnualPremium]>0 AND [TermPremium]>0))");
        cycle.HasOne<QuoteRatingResult>().WithMany().HasForeignKey(x => new { x.CurrentRatingId, x.Id, x.QuoteId })
            .HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Quote>().HasOne<UnderwritingCycle>().WithMany().HasForeignKey(x => new { x.CurrentUnderwritingCycleId, x.Id })
            .HasPrincipalKey(x => new { x.Id, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);

        var submission = Record<QuoteSubmission>(model, "QuoteSubmission"); submission.ToTable(t => t.UseSqlOutputClause(false));
        Text(submission, ("OperationKey", 200), ("Reason", 2000)); submission.Property(x => x.OperationKey).UseCollation("Latin1_General_100_BIN2");
        submission.HasIndex(x => new { x.CycleId, x.Sequence }).IsUnique(); submission.HasIndex(x => new { x.QuoteId, x.OperationKey }).IsUnique();
        submission.HasOne<UnderwritingCycle>().WithMany().HasForeignKey(x => new { x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<Team>().WithMany().HasForeignKey(x => x.AssignedTeamId).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.RoutingVersionId).OnDelete(DeleteBehavior.NoAction);
        Check(submission, "Sequence", "[Sequence]>0");
        Check(submission, "Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[SubmittedBy]");
        Check(submission, "Assignment", "[AssignedUserId] IS NOT NULL OR [AssignedTeamId] IS NOT NULL");
        Check(submission, "Reason", "LEN(TRIM([Reason]))>0 AND LEN(TRIM([OperationKey]))>0");

        var referral = Record<QuoteReferral>(model, "QuoteReferral"); referral.ToTable(t => t.UseSqlOutputClause(false));
        Text(referral, ("RuleCode", 60), ("Dimension", 60), ("State", 20), ("Reason", 2000)); UnderwritingJson(referral, "RequiredAuthorityJson");
        referral.HasAlternateKey(x => new { x.Id, x.CycleId, x.QuoteId });
        referral.HasIndex(x => new { x.CycleId, x.RuleCode, x.TargetKey }).IsUnique(); referral.HasIndex(x => new { x.CycleId, x.Sequence }).IsUnique();
        referral.HasIndex(x => new { x.State, x.AssignedUserId, x.QuoteId });
        referral.HasOne<QuoteRatingResult>().WithMany().HasForeignKey(x => new { x.RatingId, x.CycleId, x.QuoteId }).HasPrincipalKey(x => new { x.Id, x.CycleId, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        referral.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.NoAction);
        Check(referral, "Sequence", "[Sequence]>0");
        Check(referral, "Text", "LEN(TRIM([Reason]))>0 AND LEN(TRIM([RuleCode]))>0 AND LEN(TRIM([Dimension]))>0");
        Check(referral, "State", "[State] IN ('open','approved','conditional','queried','declined','superseded')");
        Check(referral, "Target", "([RiskItemId] IS NULL AND [TargetKey]='00000000-0000-0000-0000-000000000000') OR ([RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000' AND [TargetKey]=[RiskItemId])");
    }

    private static void UnderwritingJson<T>(EntityTypeBuilder<T> entity, string field) where T : class =>
        Check(entity, field, $"ISJSON([{field}],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[{field}] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
    private static void ConfigureUnderwritingDefinition<T>(EntityTypeBuilder<T> entity, string kind) where T : class
    {
        entity.ToTable(t => t.UseSqlOutputClause(false)); Text(entity, ("Version", 60), ("State", 20)); UnderwritingJson(entity, "DefinitionJson");
        Check(entity, "Interval", "[EffectiveFrom]<[EffectiveTo]");
        Check(entity, "State", "[State] IN ('published','retired')");
        const string commercial = "(COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='commercial-underwriting-1' AND COALESCE(JSON_VALUE([DefinitionJson],'$.productCode'),'')='commercial-combined')";
        Check(entity, "Definition", $"(COALESCE(JSON_VALUE([DefinitionJson],'$.schemaVersion'),'')='1' OR {commercial}) AND COALESCE(JSON_VALUE([DefinitionJson],'$.kind'),'')='{kind}' AND COALESCE(JSON_VALUE([DefinitionJson],'$.version'),'')=[Version] AND LEN(TRIM([Version]))>0 AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveFrom'))=[EffectiveFrom] AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo')) IS NOT NULL AND TRY_CONVERT(datetimeoffset,JSON_VALUE([DefinitionJson],'$.effectiveTo'))=[EffectiveTo]");
        if (kind is "binder" or "authority")
        {
            foreach (var name in new[] { "annualPremiumLimit", "stockLimit", "vehicleLimit" })
                Check(entity, name, $"{commercial} OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.{name}')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.{name}'))>=0)");
            foreach (var name in new[] { "annualPremium", "singleLocation", "maximumEstimatedLoss", "districtProperty", "employersLiability", "publicLiability", "productsLiability", "businessInterruption", "contractWorks" })
                Check(entity, "Commercial_" + name, $"NOT {commercial} OR (TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.{name}')) IS NOT NULL AND TRY_CONVERT(decimal(19,2),JSON_VALUE([DefinitionJson],'$.limits.{name}'))>0)");
        }
    }
}
