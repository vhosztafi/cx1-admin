using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureRenewalLifecycle(ModelBuilder model)
    {
        var lapse=Record<RenewalLapseEvent>(model,"RenewalLapseEvent");lapse.ToTable(t=>t.UseSqlOutputClause(false));
        lapse.HasIndex(x=>x.TermId).IsUnique();lapse.HasIndex(x=>x.WorkId).IsUnique();
        lapse.HasAlternateKey(x=>new{x.Id,x.WorkId});
        lapse.HasOne<PolicyTerm>().WithMany().HasForeignKey(x=>new{x.TermId,x.PolicyId}).HasPrincipalKey(x=>new{x.Id,x.PolicyId}).OnDelete(DeleteBehavior.NoAction);
        lapse.HasOne<SettingVersion>().WithMany().HasForeignKey(x=>x.RuleSettingVersionId).OnDelete(DeleteBehavior.NoAction);
        lapse.HasOne<OutboxWork>().WithMany().HasForeignKey(x=>x.WorkId).OnDelete(DeleteBehavior.NoAction);
        Text(lapse,("Mode",20),("Reason",1000));
        Check(lapse,"Mode","([Mode]='manual' AND [CreatedBy] IS NOT NULL) OR ([Mode]='automatic' AND [CreatedBy] IS NULL AND [CreatedAt]>=[AutoLapseAt])");
        Check(lapse,"Reason","LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
        Check(lapse,"Recipients","ISJSON([RecipientSnapshotJson],ARRAY)=1");
        Check(lapse,"Dates","[AutoLapseAt]>=[EffectiveAt] AND DATEPART(TZOFFSET,[EffectiveAt])=0 AND DATEPART(TZOFFSET,[AutoLapseAt])=0");
        var receipt=Record<RenewalLapseNotificationReceipt>(model,"RenewalLapseNotificationReceipt");receipt.ToTable(t=>t.UseSqlOutputClause(false));
        receipt.HasIndex(x=>x.LapseEventId).IsUnique();receipt.HasIndex(x=>x.WorkId).IsUnique();
        receipt.HasOne<RenewalLapseEvent>().WithMany().HasForeignKey(x=>new{x.LapseEventId,x.WorkId}).HasPrincipalKey(x=>new{x.Id,x.WorkId}).OnDelete(DeleteBehavior.NoAction);
        Text(receipt,("Outcome",40));receipt.Property(x=>x.PayloadHash).HasMaxLength(32).IsFixedLength();
        Check(receipt,"Outcome","[Outcome] IN ('demo-delivered','demo-no-recipient') AND DATALENGTH([PayloadHash])=32");
    }
}
