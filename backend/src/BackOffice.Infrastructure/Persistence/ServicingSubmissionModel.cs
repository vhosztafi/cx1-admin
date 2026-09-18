using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureServicingSubmissions(ModelBuilder model)
    {
        var submission=Record<ServicingUnderwritingSubmission>(model,"ServicingUnderwritingSubmission");
        submission.ToTable(t=>t.UseSqlOutputClause(false));
        Text(submission,("Reason",2000));Hash(submission,"InputHash");
        submission.HasIndex(x=>x.CycleId).IsUnique();
        submission.HasIndex(x=>new{x.DraftId,x.SubmittedAt,x.Id});
        submission.HasOne<ServicingCycle>().WithMany().HasForeignKey(x=>new{x.CycleId,x.DraftId,x.RevisionId})
            .HasPrincipalKey(x=>new{x.Id,x.DraftId,x.RevisionId}).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<ServicingRatingResult>().WithMany().HasForeignKey(x=>new{x.RatingId,x.CycleId,x.DraftId,x.RevisionId})
            .HasPrincipalKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId}).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.SubmittedBy).OnDelete(DeleteBehavior.NoAction);
        Check(submission,"Identity","[Id]<>'00000000-0000-0000-0000-000000000000'");
        Check(submission,"Actor","[CreatedBy] IS NOT NULL AND [CreatedBy]=[SubmittedBy]");
        Check(submission,"Reason","LEN(TRIM([Reason]))>=10");
        Check(submission,"Recorded","[SubmittedAt]=[CreatedAt]");
    }
}
