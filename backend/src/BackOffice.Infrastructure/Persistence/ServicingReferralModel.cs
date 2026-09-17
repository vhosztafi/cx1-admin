using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureServicingReferrals(ModelBuilder model)
    {
        var referral=Record<ServicingReferral>(model,"ServicingReferral");referral.ToTable(t=>t.UseSqlOutputClause(false));
        Text(referral,("RuleCode",60),("Dimension",60),("State",20),("Reason",2000));UnderwritingJson(referral,"RequiredAuthorityJson");
        referral.HasAlternateKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId});
        referral.HasIndex(x=>new{x.CycleId,x.RuleCode,x.Dimension,x.TargetKey}).IsUnique();
        referral.HasIndex(x=>new{x.CycleId,x.Sequence}).IsUnique();
        referral.HasIndex(x=>new{x.State,x.AssignedUserId,x.DraftId});
        referral.HasOne<ServicingRatingResult>().WithMany().HasForeignKey(x=>new{x.RatingId,x.CycleId,x.DraftId,x.RevisionId})
            .HasPrincipalKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId}).OnDelete(DeleteBehavior.NoAction);
        referral.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.AssignedUserId).OnDelete(DeleteBehavior.NoAction);
        Check(referral,"Sequence","[Sequence]>0");
        Check(referral,"Text","LEN(TRIM([RuleCode]))>0 AND LEN(TRIM([Dimension]))>0 AND LEN(TRIM([Reason]))>=10 AND [CreatedBy] IS NOT NULL");
        Check(referral,"State","[State] IN ('open','approved','conditional','queried','declined','superseded') AND ([State] IN ('open','superseded') OR [LatestDecisionId] IS NOT NULL)");
        Check(referral,"Target","([RiskItemId] IS NULL AND [TargetKey]='00000000-0000-0000-0000-000000000000') OR ([RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000' AND [TargetKey]=[RiskItemId])");
        Check(referral,"Triggers","JSON_QUERY([RequiredAuthorityJson],'$.triggers[0]') IS NOT NULL");

        var decision=Record<ServicingReferralDecision>(model,"ServicingReferralDecision");decision.ToTable(t=>t.UseSqlOutputClause(false));
        Text(decision,("Outcome",30),("Reason",2000),("Question",2000));
        decision.HasAlternateKey(x=>new{x.Id,x.ReferralId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId});
        decision.HasIndex(x=>new{x.ReferralId,x.Sequence}).IsUnique();
        decision.HasOne<ServicingReferral>().WithMany().HasForeignKey(x=>new{x.ReferralId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        model.Entity<UserAuthorityGrant>().HasAlternateKey(x=>new{x.Id,x.UserId,x.AuthorityVersionId});
        decision.HasOne<UserAuthorityGrant>().WithMany().HasForeignKey(x=>new{x.GrantId,x.ActorId,x.AuthorityVersionId})
            .HasPrincipalKey(x=>new{x.Id,x.UserId,x.AuthorityVersionId}).OnDelete(DeleteBehavior.NoAction);
        Check(decision,"Sequence","[Sequence]>0");
        Check(decision,"Outcome","[Outcome] IN ('approve','approve-with-conditions','query','decline','reopen')");
        Check(decision,"Reason","LEN(TRIM([Reason]))>=10 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND [DecidedAt]>=[CreatedAt]");
        Check(decision,"Question","([Outcome]='query' AND [Question] IS NOT NULL AND LEN(TRIM([Question]))>=10) OR ([Outcome]<>'query' AND [Question] IS NULL)");
        Check(decision,"Conditions","ISJSON([ConditionsJson],ARRAY)=1 AND DATALENGTH([ConditionsJson])<=131072 AND (([Outcome] IN ('approve-with-conditions','query') AND JSON_QUERY([ConditionsJson],'$[0]') IS NOT NULL) OR ([Outcome] NOT IN ('approve-with-conditions','query') AND [ConditionsJson]='[]'))");
        referral.HasOne<ServicingReferralDecision>().WithMany().HasForeignKey(x=>new{x.LatestDecisionId,x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.ReferralId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
    }
}
