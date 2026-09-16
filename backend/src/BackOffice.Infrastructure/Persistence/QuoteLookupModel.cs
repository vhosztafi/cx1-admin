using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureQuoteLookups(ModelBuilder model)
    {
        var lookup = Record<QuoteLookup>(model, "QuoteLookup");
        lookup.ToTable(t => t.UseSqlOutputClause(false));
        Text(lookup, ("Kind", 20), ("TargetScope", 20), ("Query", 40), ("InputFingerprint", 64),
            ("ReferenceVersion", 100), ("Scenario", 40), ("State", 20));
        Hash(lookup, "RequestHash");
        lookup.Property(x => x.InputFingerprint).UseCollation("Latin1_General_100_BIN2");
        lookup.HasAlternateKey(x => new { x.Id, x.QuoteId, x.RevisionId, x.InputFingerprint });
        lookup.HasIndex(x => new { x.QuoteId, x.RequestHash }).IsUnique();
        lookup.HasIndex(x => x.WorkId).IsUnique();
        lookup.HasOne<QuoteRevision>().WithMany().HasForeignKey(x => new { x.RevisionId, x.QuoteId })
            .HasPrincipalKey(x => new { x.Id, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        lookup.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        lookup.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        Check(lookup, "Target", "([Kind]='address' AND [TargetScope] IN ('insured','driver','premises') OR [Kind]='vehicle' AND [TargetScope]='vehicle' OR [Kind]='licence' AND [TargetScope]='driver') AND (([TargetScope]='insured' AND [RiskItemId] IS NULL) OR ([TargetScope]<>'insured' AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000'))");
        Check(lookup, "Query", "LEN([Query]) BETWEEN 2 AND 40 AND [Query] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_100_BIN2");
        Check(lookup, "Fingerprint", "LEN([InputFingerprint])=64 AND [InputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
        Check(lookup, "Configuration", "LEN(TRIM([ReferenceVersion]))>0 AND [Scenario] IN ('success','no-match','multiple','reject','fail-once','timeout-after-success') AND [CreatedBy] IS NOT NULL");
        Check(lookup, "Outcome", "([State]='pending' AND [CompletedAt] IS NULL AND [ResultJson] IS NULL) OR ([State] IN ('succeeded','no-match','rejected','failed') AND [CompletedAt] IS NOT NULL AND [CompletedAt]>=[CreatedAt] AND [ResultJson] IS NOT NULL AND ISJSON([ResultJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[ResultJson] COLLATE Latin1_General_100_BIN2_UTF8))<=65536)");

        var selection = Record<QuoteLookupSelection>(model, "QuoteLookupSelection");
        selection.ToTable(t => t.UseSqlOutputClause(false));
        Text(selection, ("InputFingerprint", 64), ("ManualReason", 1000));
        selection.Property(x => x.InputFingerprint).UseCollation("Latin1_General_100_BIN2");
        selection.HasIndex(x => x.LookupId).IsUnique();
        selection.HasOne<QuoteLookup>().WithMany().HasForeignKey(x => new { x.LookupId, x.QuoteId, x.SourceRevisionId, x.InputFingerprint })
            .HasPrincipalKey(x => new { x.Id, x.QuoteId, x.RevisionId, x.InputFingerprint }).OnDelete(DeleteBehavior.NoAction);
        selection.HasOne<QuoteRevision>().WithMany().HasForeignKey(x => new { x.NewRevisionId, x.QuoteId })
            .HasPrincipalKey(x => new { x.Id, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        selection.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        Check(selection, "Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId]");
        Check(selection, "Decision", "[SourceRevisionId]<>[NewRevisionId] AND (([CandidateId] IS NOT NULL AND [CandidateId]<>'00000000-0000-0000-0000-000000000000' AND [ManualReason] IS NULL) OR ([CandidateId] IS NULL AND [ManualReason] IS NOT NULL AND LEN(TRIM([ManualReason]))>0))");
    }
}
