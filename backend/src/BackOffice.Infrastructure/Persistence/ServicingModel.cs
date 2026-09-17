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

        var lease = Record<ServicingLease>(model, "ServicingLease");
        lease.HasIndex(x => x.DraftId).IsUnique();
        lease.HasOne<ServicingDraft>().WithMany().HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.NoAction);
        lease.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.HolderId).OnDelete(DeleteBehavior.NoAction);
        Check(lease, "Fence", "[Generation]>0 AND [Token]<>'00000000-0000-0000-0000-000000000000'");
    }
}
