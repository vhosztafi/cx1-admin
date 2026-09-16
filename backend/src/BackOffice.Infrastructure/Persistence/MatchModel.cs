using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureMatches(ModelBuilder model)
    {
        model.Entity<ClientAgencyRelationship>().HasAlternateKey(x=>new {x.Id,x.ClientId,x.AgencyId});
        var intake=Record<MatchSubmission>(model,"MatchSubmission");intake.ToTable(t=>t.UseSqlOutputClause(false));
        Text(intake,("Reference",40));Json(intake,"IdentitySnapshot");intake.Property(x=>x.IdentitySnapshot).HasMaxLength(16000);
        intake.HasIndex(x=>x.Reference).IsUnique();intake.HasIndex(x=>new {x.AgencyId,x.CreatedAt,x.Id});
        intake.HasIndex(x=>x.QuoteId).IsUnique().HasFilter("[QuoteId] IS NOT NULL");
        intake.HasOne<Quote>().WithMany().HasForeignKey(x=>new {x.QuoteId,x.AgencyId})
            .HasPrincipalKey(x=>new {x.Id,x.AgencyId}).OnDelete(DeleteBehavior.NoAction);
        intake.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        intake.HasOne<ClientAccount>().WithMany().HasForeignKey(x=>x.SeparateClientId).OnDelete(DeleteBehavior.NoAction);
        intake.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x=>new {x.LinkedRelationshipId,x.LinkedClientId,x.AgencyId})
            .HasPrincipalKey(x=>new {x.Id,x.ClientId,x.AgencyId}).OnDelete(DeleteBehavior.NoAction);
        Check(intake,"Reference","LEN(TRIM([Reference]))>0");
        Check(intake,"Association","([LinkedClientId] IS NULL AND [LinkedRelationshipId] IS NULL) OR ([LinkedClientId] IS NOT NULL AND [LinkedRelationshipId] IS NOT NULL)");
        var review=Record<MatchReview>(model,"MatchReview");review.ToTable(t=>t.UseSqlOutputClause(false));
        Text(review,("Confidence",30),("State",30));Json(review,"RuleSnapshot");Json(review,"Signals");
        review.Property(x=>x.RuleSnapshot).HasMaxLength(8000);
        review.HasIndex(x=>x.SubmissionId).IsUnique();review.HasIndex(x=>new {x.State,x.CreatedAt,x.Id});
        review.HasOne<MatchSubmission>().WithMany().HasForeignKey(x=>x.SubmissionId).OnDelete(DeleteBehavior.NoAction);
        review.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x=>new {x.CandidateRelationshipId,x.CandidateClientId})
            .HasPrincipalKey(x=>new {x.Id,x.ClientId}).OnDelete(DeleteBehavior.NoAction);
        review.HasOne<SettingVersion>().WithMany().HasForeignKey(x=>x.RuleVersionId).OnDelete(DeleteBehavior.NoAction);
        Check(review,"State","[State] IN ('pending','linked','separate','declined','queried')");
        Check(review,"Confidence","[Confidence] IN ('low','medium','high')");
        Check(review,"SignalsShape","ISJSON([Signals],ARRAY)=1 AND DATALENGTH([Signals])<=2500000");
        Check(review,"RuleIdentity","COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE([RuleSnapshot],'$.id')),'00000000-0000-0000-0000-000000000000')=[RuleVersionId] AND COALESCE(TRY_CONVERT(int,JSON_VALUE([RuleSnapshot],'$.version')),0)>0");
        var request=Record<MatchInformationRequest>(model,"MatchInformationRequest");request.ToTable(t=>t.UseSqlOutputClause(false));
        Text(request,("Description",1000),("DeliveryState",20));request.HasAlternateKey(x=>new {x.Id,x.MatchId});
        request.HasOne<MatchReview>().WithMany().HasForeignKey(x=>x.MatchId).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.NoAction);
        request.HasIndex(x=>new {x.MatchId,x.RecordedAt,x.Id});
        Check(request,"Description","LEN(TRIM([Description]))>0");Check(request,"DeliveryState","[DeliveryState]='recorded'");
        var decision=Record<MatchDecision>(model,"MatchDecision");decision.ToTable(t=>t.UseSqlOutputClause(false));
        Text(decision,("Outcome",20),("Reason",1000));
        decision.HasOne<MatchReview>().WithMany().HasForeignKey(x=>x.MatchId).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x=>new {x.RelationshipId,x.ClientId})
            .HasPrincipalKey(x=>new {x.Id,x.ClientId}).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<MatchInformationRequest>().WithMany().HasForeignKey(x=>new {x.InformationRequestId,x.MatchId})
            .HasPrincipalKey(x=>new {x.Id,x.MatchId}).OnDelete(DeleteBehavior.NoAction);
        decision.HasIndex(x=>new {x.MatchId,x.OccurredAt,x.Id});decision.HasIndex(x=>x.InformationRequestId).IsUnique().HasFilter("[InformationRequestId] IS NOT NULL");
        Check(decision,"Outcome","[Outcome] IN ('link','separate','decline','query','reopen')");Check(decision,"Reason","LEN(TRIM([Reason]))>0");
        Check(decision,"Association","([ClientId] IS NULL AND [RelationshipId] IS NULL AND [Outcome] NOT IN ('link','separate')) OR ([ClientId] IS NOT NULL AND [RelationshipId] IS NOT NULL)");
        Check(decision,"InformationRequest","([Outcome]='query' AND [InformationRequestId] IS NOT NULL) OR ([Outcome]<>'query' AND [InformationRequestId] IS NULL)");
    }
}
