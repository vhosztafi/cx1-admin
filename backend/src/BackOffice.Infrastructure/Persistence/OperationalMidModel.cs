using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Persistence;
public sealed partial class BackOfficeDbContext
{
    private static void ConfigureOperationalMid(ModelBuilder model)
    {
        var sub=Record<MidSubmission>(model,"MidSubmission");sub.ToTable(t=>t.UseSqlOutputClause(false));Text(sub,("RequestHash",64));sub.Property(x=>x.RequestJson).IsRequired();
        sub.HasOne<PolicyVersion>().WithMany().HasForeignKey(x=>new{x.VersionId,x.TransactionId,x.TermId,x.PolicyId}).HasPrincipalKey(x=>new{x.Id,x.TransactionId,x.TermId,x.PolicyId}).OnDelete(DeleteBehavior.NoAction);
        sub.HasOne<PolicyVersion>().WithMany().HasForeignKey(x=>x.BaseVersionId).OnDelete(DeleteBehavior.NoAction);
        sub.HasOne<PolicyMidIntent>().WithMany().HasForeignKey(x=>x.PolicyMidIntentId).OnDelete(DeleteBehavior.NoAction);
        sub.HasOne<CancellationConsequence>().WithMany().HasForeignKey(x=>x.CancellationConsequenceId).OnDelete(DeleteBehavior.NoAction);
        sub.HasOne<OutboxWork>().WithMany().HasForeignKey(x=>x.WorkId).OnDelete(DeleteBehavior.NoAction);
        sub.HasOne<SettingVersion>().WithMany().HasForeignKey(x=>x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        sub.HasIndex(x=>x.WorkId).IsUnique();sub.HasIndex(x=>x.VersionId).IsUnique();
        sub.HasIndex(x=>x.PolicyMidIntentId).IsUnique().HasFilter("[PolicyMidIntentId] IS NOT NULL");sub.HasIndex(x=>x.CancellationConsequenceId).IsUnique().HasFilter("[CancellationConsequenceId] IS NOT NULL");
        Check(sub,"Source","([PolicyMidIntentId] IS NOT NULL AND [CancellationConsequenceId] IS NULL) OR ([PolicyMidIntentId] IS NULL AND [CancellationConsequenceId] IS NOT NULL)");
        Check(sub,"Content","ISJSON([RequestJson])=1 AND LEN([RequestHash])=64 AND [CreatedBy] IS NOT NULL");
        var result=Record<MidResult>(model,"MidResult");result.ToTable(t=>t.UseSqlOutputClause(false));Text(result,("ProviderEventId",200),("ContentHash",64));result.Property(x=>x.ResultJson).IsRequired();
        result.HasOne<MidSubmission>().WithMany().HasForeignKey(x=>x.SubmissionId).OnDelete(DeleteBehavior.NoAction);
        result.HasOne<DemoProviderOperation>().WithMany().HasForeignKey(x=>x.ProviderOperationId).OnDelete(DeleteBehavior.NoAction);
        result.HasIndex(x=>x.SubmissionId).IsUnique();result.HasIndex(x=>x.ProviderEventId).IsUnique();
        Check(result,"Content","ISJSON([ResultJson])=1 AND LEN([ContentHash])=64");
    }
}
