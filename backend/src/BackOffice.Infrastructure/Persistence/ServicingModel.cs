using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureServicing(ModelBuilder model)
    {
        var draft = Record<ServicingDraft>(model, "ServicingDraft");
        draft.ToTable(t => t.UseSqlOutputClause(false)); Text(draft, ("Kind", 30), ("State", 30));
        draft.HasAlternateKey(x => new { x.Id, x.PolicyId, x.BaseTermId, x.BaseVersionId });
        draft.HasOne<PolicyVersion>().WithMany().HasForeignKey(x => new { x.BaseVersionId, x.BaseTermId, x.PolicyId })
            .HasPrincipalKey(x => new { x.Id, x.TermId, x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        draft.HasOne<ServicingRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, x.Id })
            .HasPrincipalKey(x => new { x.Id, x.DraftId }).OnDelete(DeleteBehavior.NoAction);
        draft.HasIndex(x => x.BaseTermId, "UX_ServicingDraft_LiveAdjustment").IsUnique().HasFilter("[Kind]='adjustment' AND [State]='draft'");
        draft.HasIndex(x => x.BaseTermId, "UX_ServicingDraft_LiveRenewal").IsUnique().HasFilter("[Kind]='renewal' AND [State]='draft'");
        Check(draft, "Kind", "[Kind] IN ('adjustment','renewal','cancellation')");
        Check(draft, "State", "[State] IN ('draft','abandoned')");
        Check(draft, "Creator", "[CreatedBy] IS NOT NULL");

        var revision = Record<ServicingRevision>(model, "ServicingRevision");
        revision.ToTable(t => t.UseSqlOutputClause(false)); Text(revision, ("SchemaVersion", 30));
        UnderwritingJson(revision, "ProposalJson"); Hash(revision, "ContentHash");
        revision.HasAlternateKey(x => new { x.Id, x.DraftId });
        revision.HasIndex(x => new { x.DraftId, x.Sequence }).IsUnique();
        revision.HasOne<ServicingDraft>().WithMany().HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.NoAction);
        Check(revision, "Sequence", "[Sequence]>0"); Check(revision, "Creator", "[CreatedBy] IS NOT NULL");
        Check(revision, "Schema", "[SchemaVersion]='1.0' AND COALESCE(JSON_VALUE([ProposalJson],'$.schemaVersion'),'')=[SchemaVersion]");

        var cycle = Record<ServicingCycle>(model, "ServicingCycle"); cycle.ToTable(t => t.UseSqlOutputClause(false));
        Text(cycle, ("State", 20), ("SupersededReason", 2000)); Hash(cycle, "InputHash");
        Check(cycle, "InputJson", "ISJSON([InputJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[InputJson] COLLATE Latin1_General_100_BIN2_UTF8))<=8388608");
        cycle.HasAlternateKey(x => new { x.Id, x.DraftId, x.PolicyId });
        cycle.HasAlternateKey(x => new { x.Id, x.DraftId, x.RevisionId });
        cycle.HasAlternateKey(x => new { x.Id, x.DraftId, x.RevisionId, x.WorkId, x.RatingRuleVersionId, x.InputHash });
        cycle.HasIndex(x => new { x.DraftId, x.Sequence }).IsUnique(); cycle.HasIndex(x => x.WorkId).IsUnique();
        cycle.HasOne<ServicingDraft>().WithMany().HasForeignKey(x => new { x.DraftId, x.PolicyId, x.BaseTermId, x.BaseVersionId })
            .HasPrincipalKey(x => new { x.Id, x.PolicyId, x.BaseTermId, x.BaseVersionId }).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<ServicingRevision>().WithMany().HasForeignKey(x => new { x.RevisionId, x.DraftId })
            .HasPrincipalKey(x => new { x.Id, x.DraftId }).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<Policy>().WithMany().HasForeignKey(x => new { x.PolicyId, x.ProductId })
            .HasPrincipalKey(x => new { x.Id, x.ProductId }).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<ProductVersion>().WithMany().HasForeignKey(x => new { x.ProductVersionId, x.ProductId })
            .HasPrincipalKey(x => new { x.Id, x.ProductId }).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<AgencyTermsVersion>().WithMany().HasForeignKey(x => x.AgencyTermsVersionId).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<RatingRuleVersion>().WithMany().HasForeignKey(x => new { x.RatingRuleVersionId, x.ProductId })
            .HasPrincipalKey(x => new { x.Id, x.ProductId }).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<AuthorityVersion>().WithMany().HasForeignKey(x => new { x.AuthorityVersionId, x.ProductVersionId, x.BinderVersionId })
            .HasPrincipalKey(x => new { x.Id, x.ProductVersionId, x.BinderVersionId }).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.RuntimeVersionId).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.RequestedBy).OnDelete(DeleteBehavior.NoAction);
        cycle.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        Check(cycle, "State", "[State] IN ('rating-pending','rated','failed','superseded')");
        Check(cycle, "Sequence", "[Sequence]>0");
        Check(cycle, "Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RequestedBy]");
        Check(cycle, "Superseded", "([State]='superseded' AND [SupersededAt] IS NOT NULL AND [SupersededAt]>=[CreatedAt] AND [SupersededReason] IS NOT NULL AND LEN(TRIM([SupersededReason]))>0) OR ([State]<>'superseded' AND [SupersededAt] IS NULL AND [SupersededReason] IS NULL)");
        draft.HasOne<ServicingCycle>().WithMany().HasForeignKey(x => new { x.CurrentCycleId, x.Id, x.PolicyId })
            .HasPrincipalKey(x => new { x.Id, x.DraftId, x.PolicyId }).OnDelete(DeleteBehavior.NoAction);

        var result = Record<ServicingRatingResult>(model, "ServicingRatingResult"); result.ToTable(t => t.UseSqlOutputClause(false));
        Text(result, ("Outcome", 20)); Hash(result, "InputHash"); Hash(result, "ResultHash"); UnderwritingJson(result, "ResultJson");
        result.HasAlternateKey(x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId });
        result.HasIndex(x => x.WorkId).IsUnique(); result.HasIndex(x => x.AttemptId).IsUnique(); result.HasIndex(x => x.ProviderOperationId).IsUnique();
        result.HasOne<ServicingCycle>().WithMany().HasForeignKey(x => new { x.CycleId, x.DraftId, x.RevisionId, x.WorkId, x.RuleVersionId, x.InputHash })
            .HasPrincipalKey(x => new { x.Id, x.DraftId, x.RevisionId, x.WorkId, x.RatingRuleVersionId, x.InputHash }).OnDelete(DeleteBehavior.NoAction);
        result.HasOne<AdapterAttempt>().WithMany().HasForeignKey(x => new { x.AttemptId, x.WorkId })
            .HasPrincipalKey(x => new { x.Id, x.WorkId }).OnDelete(DeleteBehavior.NoAction);
        result.HasOne<DemoProviderOperation>().WithMany().HasForeignKey(x => x.ProviderOperationId).OnDelete(DeleteBehavior.NoAction);
        Check(result, "Outcome", "[Outcome] IN ('rated','rejected')");
        Check(result, "Interval", "[CompletedAt]>=[CreatedAt] AND [ExpiresAt]>[CompletedAt]");
        foreach (var name in new[] { "BaseAnnualPremium", "Premium", "Tax", "Fee", "BrokerCommission", "GrossPayable", "NetDue" })
        { result.Property<decimal>(name).HasPrecision(19, 2); Check(result, name, $"[{name}] BETWEEN -9999999999999.99 AND 9999999999999.99"); }
        Check(result, "Totals", "[GrossPayable]=[Premium]+[Tax]+[Fee] AND [NetDue]=[GrossPayable]-[BrokerCommission] AND [Fee]>=0 AND (([Outcome]='rated' AND [BaseAnnualPremium]>0) OR ([Outcome]='rejected' AND [BaseAnnualPremium]=0 AND [Premium]=0 AND [Tax]=0 AND [Fee]=0 AND [BrokerCommission]=0 AND [GrossPayable]=0 AND [NetDue]=0))");
        cycle.HasOne<ServicingRatingResult>().WithMany().HasForeignKey(x => new { x.CurrentRatingId, x.Id, x.DraftId, x.RevisionId })
            .HasPrincipalKey(x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId }).OnDelete(DeleteBehavior.NoAction);
        Check(cycle, "RatedResult", "[State]<>'rated' OR [CurrentRatingId] IS NOT NULL");

        var lease = Record<ServicingLease>(model, "ServicingLease");
        lease.HasIndex(x => x.DraftId).IsUnique();
        lease.HasOne<ServicingDraft>().WithMany().HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.NoAction);
        lease.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.HolderId).OnDelete(DeleteBehavior.NoAction);
        Check(lease, "Fence", "[Generation]>0 AND [Token]<>'00000000-0000-0000-0000-000000000000'");
    }
}
