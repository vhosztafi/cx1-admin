using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureServicingIssue(ModelBuilder model)
    {
        var decision = Record<ServicingIssueDecision>(model, "ServicingIssueDecision"); decision.ToTable(t => t.UseSqlOutputClause(false));
        Text(decision, ("TermsHash",64), ("AssuranceHash",64), ("Reason",1000)); Hash(decision,"InputHash");
        decision.HasIndex(x => x.DraftId).IsUnique();
        decision.HasAlternateKey(x => new { x.Id,x.DraftId,x.PolicyId,x.CycleId,x.RevisionId,x.RatingId,x.AcceptanceId });
        decision.HasOne<ServicingDraft>().WithMany().HasForeignKey(x => new { x.DraftId,x.PolicyId,x.BaseTermId,x.BaseVersionId })
            .HasPrincipalKey(x => new { x.Id,x.PolicyId,x.BaseTermId,x.BaseVersionId }).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<ServicingCycle>().WithMany().HasForeignKey(x => new { x.CycleId,x.DraftId,x.PolicyId })
            .HasPrincipalKey(x => new { x.Id,x.DraftId,x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<ServicingTermsVersion>().WithMany().HasForeignKey(x => new { x.TermsVersionId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId })
            .HasPrincipalKey(x => new { x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId }).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<ServicingAcceptance>().WithMany().HasForeignKey(x => new { x.AcceptanceId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId })
            .HasPrincipalKey(x => new { x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId }).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<UserAuthorityGrant>().WithMany().HasForeignKey(x => new { x.GrantId,x.ActorId,x.AuthorityVersionId })
            .HasPrincipalKey(x => new { x.Id,x.UserId,x.AuthorityVersionId }).OnDelete(DeleteBehavior.NoAction);
        Check(decision,"Provenance","[CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
        foreach (var name in new[] { "TermsHash", "AssuranceHash" })
        { decision.Property<string>(name).UseCollation("Latin1_General_100_BIN2"); Check(decision,name,$"LEN([{name}])=64 AND [{name}] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2"); }
        var transaction = model.Entity<PolicyTransaction>();
        transaction.HasOne<ServicingIssueDecision>().WithMany()
            .HasForeignKey(x => new { x.ServicingIssueDecisionId,x.ServicingDraftId,x.PolicyId,x.ServicingCycleId,x.ServicingRevisionId,x.ServicingRatingId,x.ServicingAcceptanceId })
            .HasPrincipalKey(x => new { x.Id,x.DraftId,x.PolicyId,x.CycleId,x.RevisionId,x.RatingId,x.AcceptanceId }).OnDelete(DeleteBehavior.NoAction);
        transaction.HasIndex(x => x.ServicingIssueDecisionId).IsUnique().HasFilter("[ServicingIssueDecisionId] IS NOT NULL");
        Check(transaction,"IssueDecision","([Kind] IN ('new-business','cancellation') AND [ServicingIssueDecisionId] IS NULL) OR ([Kind] IN ('adjustment','renewal') AND [ServicingIssueDecisionId] IS NOT NULL)");
        transaction.HasAlternateKey(x => new { x.Id,x.PolicyId });
        var draft = model.Entity<ServicingDraft>();
        draft.HasOne<PolicyTransaction>().WithMany().HasForeignKey(x => new { x.IssuedTransactionId,x.PolicyId }).HasPrincipalKey(x => new { x.Id,x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        Check(draft,"IssuedTransaction","([State]='issued' AND [IssuedTransactionId] IS NOT NULL) OR ([State]<>'issued' AND [IssuedTransactionId] IS NULL)");

        var mid = Record<PolicyMidIntent>(model,"PolicyMidIntent"); mid.ToTable(t => t.UseSqlOutputClause(false));
        Text(mid,("Purpose",30)); UnderwritingJson(mid,"PayloadJson"); Hash(mid,"PayloadHash");
        mid.HasIndex(x => new { x.VersionId,x.Purpose }).IsUnique(); mid.HasIndex(x => x.WorkId).IsUnique();
        mid.HasOne<PolicyVersion>().WithMany().HasForeignKey(x => new { x.VersionId,x.TransactionId,x.TermId,x.PolicyId })
            .HasPrincipalKey(x => new { x.Id,x.TransactionId,x.TermId,x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        mid.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        Check(mid,"Purpose","[Purpose] IN ('new-business','adjustment','renewal')");
    }
}
