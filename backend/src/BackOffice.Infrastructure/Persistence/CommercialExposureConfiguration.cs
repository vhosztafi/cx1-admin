using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureCommercialExposure(ModelBuilder model)
    {
        var book = Record<CommercialExposureBook>(model, "CommercialExposureBook");
        book.ToTable(t => t.UseSqlOutputClause(false)); Text(book, ("Code", 80));
        book.HasIndex(x => x.Code).IsUnique(); book.HasIndex(x => new { x.ProductId, x.ProviderId }).IsUnique();
        book.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
        book.HasOne<CapacityProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.NoAction);
        Check(book, "Publisher", "[CreatedBy] IS NOT NULL AND LEN(TRIM([Code]))>0");
        var mapping = Record<CommercialExposureBinder>(model, "CommercialExposureBinder");
        mapping.ToTable(t => t.UseSqlOutputClause(false));
        mapping.HasIndex(x => x.BinderVersionId).IsUnique(); mapping.HasAlternateKey(x => new { x.BookId, x.BinderVersionId });
        mapping.HasOne<CommercialExposureBook>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.NoAction);
        mapping.HasOne<BinderVersion>().WithMany().HasForeignKey(x => x.BinderVersionId).OnDelete(DeleteBehavior.NoAction);
        Check(mapping, "Publisher", "[CreatedBy] IS NOT NULL");
        var limit = Record<CommercialExposureLimitVersion>(model, "CommercialExposureLimitVersion");
        limit.ToTable(t => t.UseSqlOutputClause(false)); Text(limit, ("District", 4));
        limit.Property(x => x.District).UseCollation("Latin1_General_100_BIN2");
        limit.Property(x => x.Amount).HasPrecision(16, 2); Hash(limit, "ContentHash"); Json(limit, "PublicationJson");
        limit.HasIndex(x => new { x.BookId, x.District, x.Version }).IsUnique();
        limit.HasIndex(x => new { x.BookId, x.PublishedAt, x.EffectiveFrom });
        limit.HasOne<CommercialExposureBook>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.NoAction);
        limit.HasOne<CommercialExposureLimitVersion>().WithMany().HasForeignKey(x => x.SupersedesLimitId).OnDelete(DeleteBehavior.NoAction);
        Check(limit, "Publication", "[CreatedBy] IS NOT NULL AND [Version]>0 AND [EffectiveFrom]<[EffectiveTo] AND [Amount] BETWEEN 0 AND 9999999999999.99 AND [ContentHash]<>0x0000000000000000000000000000000000000000000000000000000000000000");
        Check(limit, "District", "([District]='*' AND DATALENGTH([District])=2) OR " + CommercialDistrictCheck("[District]"));
        var version = Record<CommercialExposureVersion>(model, "CommercialExposureVersion");
        version.ToTable(t => t.UseSqlOutputClause(false)); Text(version, ("TransactionKind", 30)); Hash(version, "SourceHash");
        version.HasIndex(x => x.VersionId).IsUnique(); version.HasIndex(x => new { x.BookId, x.ProcessedAt, x.PolicyId });
        version.HasOne<CommercialExposureBinder>().WithMany().HasForeignKey(x => new { x.BookId, x.BinderVersionId })
            .HasPrincipalKey(x => new { x.BookId, x.BinderVersionId }).OnDelete(DeleteBehavior.NoAction);
        version.HasOne<PolicyVersion>().WithMany().HasForeignKey(x => new { x.VersionId, x.TransactionId, x.TermId, x.PolicyId })
            .HasPrincipalKey(x => new { x.Id, x.TransactionId, x.TermId, x.PolicyId }).OnDelete(DeleteBehavior.NoAction);
        Check(version, "Timeline", "[CreatedBy] IS NOT NULL AND [TermStartsAt]<[TermEndsAt] AND [EffectiveAt]>=[TermStartsAt] AND [EffectiveAt]<[TermEndsAt] AND [TransactionSequence]>0 AND [SliceOrdinal]>0 AND [TransactionKind] IN ('new-business','adjustment','renewal','cancellation')");
        Check(version, "Locations", "ISJSON([LocationsJson], ARRAY)=1 AND ([TransactionKind]<>'cancellation' OR [LocationsJson]='[]')");
        var location = model.Entity<CommercialExposureLocationRecord>(); location.ToTable("CommercialExposureLocation", t => t.UseSqlOutputClause(false));
        location.HasKey(x => new { x.ExposureVersionId, x.RiskItemId }); location.Property(x => x.RiskItemId).ValueGeneratedNever();
        location.Property(x => x.District).HasMaxLength(4).UseCollation("Latin1_General_100_BIN2"); location.Property(x => x.SumInsured).HasPrecision(16, 2);
        location.HasIndex(x => new { x.District, x.ExposureVersionId });
        location.HasOne<CommercialExposureVersion>().WithMany().HasForeignKey(x => x.ExposureVersionId).OnDelete(DeleteBehavior.NoAction);
        Check(location, "District", CommercialDistrictCheck("[District]"));
        Check(location, "Property", "[RiskItemId]<>'00000000-0000-0000-0000-000000000000' AND [SumInsured] BETWEEN 0 AND 29999999999999.97");
        var decision = Record<CommercialExposureIssueDecision>(model, "CommercialExposureIssueDecision");
        decision.ToTable(t => t.UseSqlOutputClause(false)); Json(decision, "DecisionJson"); Hash(decision, "DecisionHash");
        decision.HasIndex(x => x.ExposureVersionId).IsUnique();
        decision.HasOne<CommercialExposureVersion>().WithMany().HasForeignKey(x => x.ExposureVersionId).OnDelete(DeleteBehavior.NoAction);
        Check(decision, "Provenance", "[CreatedBy] IS NOT NULL AND [CreatedAt]=[AssessedAt] AND [DecisionHash]<>0x0000000000000000000000000000000000000000000000000000000000000000");
    }

    private static string CommercialDistrictCheck(string value) => $"({value}='GIR' OR {value} LIKE '[A-PR-UWYZ][0-9]' OR {value} LIKE '[A-PR-UWYZ][0-9][0-9]' OR {value} LIKE '[A-PR-UWYZ][A-HK-Y][0-9]' OR {value} LIKE '[A-PR-UWYZ][A-HK-Y][0-9][0-9]' OR {value} LIKE '[A-PR-UWYZ][0-9][A-HJKPSTUW]' OR {value} LIKE '[A-PR-UWYZ][A-HK-Y][0-9][ABEHMNPRVWXY]') AND DATALENGTH({value})=2*LEN({value})";
}
