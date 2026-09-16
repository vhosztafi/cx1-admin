using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureQuotes(ModelBuilder model)
    {
        model.HasSequence<long>("QuoteReferenceSequence").StartsAt(1).IncrementsBy(1).HasMax(9999999999);
        model.Entity<ClientAgencyRelationship>().HasAlternateKey(x => new { x.Id, x.ClientId, x.AgencyId });
        model.Entity<ProductVersion>().HasAlternateKey(x => new { x.Id, x.ProductId });
        model.Entity<AgencyTermsVersion>().HasAlternateKey(x => new { x.Id, x.AgencyId });
        model.Entity<AgencyProduct>().HasAlternateKey(x => new { x.AgencyTermsVersionId, x.ProductVersionId });

        var quote = Record<Quote>(model, "Quote");
        quote.ToTable(t => t.UseSqlOutputClause(false));
        Text(quote, ("Reference", 40), ("State", 20), ("CaptureClosedReason", 1000));
        quote.Property(x => x.Number).HasDefaultValueSql("NEXT VALUE FOR [QuoteReferenceSequence]");
        quote.Property(x => x.Reference).HasComputedColumnSql("'QT-MT-' + RIGHT('0000000000' + CONVERT(varchar(20), [Number]), 10)", stored: true);
        quote.HasIndex(x => x.Number).IsUnique(); quote.HasIndex(x => x.Reference).IsUnique();
        quote.HasIndex(x => new { x.AgencyId, x.State, x.UpdatedAt, x.Id });
        quote.HasIndex(x => new { x.ClientId, x.UpdatedAt, x.Id });
        quote.HasAlternateKey(x => new { x.Id, x.AgencyId, x.ProductId });
        quote.HasAlternateKey(x => new { x.Id, x.AgencyId });
        quote.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x => new { x.RelationshipId, x.ClientId, x.AgencyId })
            .HasPrincipalKey(x => new { x.Id, x.ClientId, x.AgencyId }).OnDelete(DeleteBehavior.NoAction);
        quote.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
        quote.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.NoAction);
        quote.HasOne<QuoteRevision>().WithMany().HasForeignKey(x => new { x.CurrentRevisionId, x.Id })
            .HasPrincipalKey(x => new { x.Id, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        quote.HasOne<QuoteRevision>().WithMany().HasForeignKey(x => x.ClonedFromQuoteRevisionId).OnDelete(DeleteBehavior.NoAction);
        Check(quote, "Number", "[Number] BETWEEN 1 AND 9999999999");
        Check(quote, "State", "[State] IN ('draft','withdrawn')");
        Check(quote, "Creator", "[CreatedBy] IS NOT NULL");
        Check(quote, "CaptureClosure", "([CaptureClosedAt] IS NULL AND [CaptureClosedReason] IS NULL) OR ([CaptureClosedAt] IS NOT NULL AND [CaptureClosedAt]>=[CreatedAt] AND [CaptureClosedReason] IS NOT NULL AND LEN(TRIM([CaptureClosedReason]))>0)");

        var revision = Record<QuoteRevision>(model, "QuoteRevision");
        revision.ToTable(t => t.UseSqlOutputClause(false));
        Text(revision, ("SchemaVersion", 30), ("QuestionSetVersion", 100), ("Reason", 1000));
        Hash(revision, "ContentHash");
        revision.HasAlternateKey(x => new { x.Id, x.QuoteId });
        revision.HasIndex(x => new { x.QuoteId, x.Number }).IsUnique();
        revision.HasOne<Quote>().WithMany().HasForeignKey(x => new { x.QuoteId, x.AgencyId, x.ProductId })
            .HasPrincipalKey(x => new { x.Id, x.AgencyId, x.ProductId }).OnDelete(DeleteBehavior.NoAction);
        revision.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x => new { x.RelationshipId, x.ClientId, x.AgencyId })
            .HasPrincipalKey(x => new { x.Id, x.ClientId, x.AgencyId }).OnDelete(DeleteBehavior.NoAction);
        revision.HasOne<ProductVersion>().WithMany().HasForeignKey(x => new { x.ProductVersionId, x.ProductId })
            .HasPrincipalKey(x => new { x.Id, x.ProductId }).OnDelete(DeleteBehavior.NoAction);
        revision.HasOne<AgencyTermsVersion>().WithMany().HasForeignKey(x => new { x.AgencyTermsVersionId, x.AgencyId })
            .HasPrincipalKey(x => new { x.Id, x.AgencyId }).OnDelete(DeleteBehavior.NoAction);
        revision.HasOne<AgencyProduct>().WithMany().HasForeignKey(x => new { x.AgencyTermsVersionId, x.ProductVersionId })
            .HasPrincipalKey(x => new { x.AgencyTermsVersionId, x.ProductVersionId }).OnDelete(DeleteBehavior.NoAction);
        revision.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.SavedBy).OnDelete(DeleteBehavior.NoAction);
        Check(revision, "Number", "[Number]>0");
        Check(revision, "Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[SavedBy] AND [SavedAt]>=[CreatedAt]");
        Check(revision, "Versions", "LEN(TRIM([SchemaVersion]))>0 AND LEN(TRIM([QuestionSetVersion]))>0 AND COALESCE(JSON_VALUE([ProposalJson],'$.schemaVersion'),'')=[SchemaVersion]");
        Check(revision, "Hash", "[ContentHash]<>0x0000000000000000000000000000000000000000000000000000000000000000");
        foreach (var field in new[] { "ProposalJson", "TermIntentJson", "ReferenceVersionsJson" })
            Check(revision, field, $"ISJSON([{field}], OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[{field}] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");

        var registration = model.Entity<QuoteRegistration>();
        registration.ToTable("QuoteRegistration"); registration.HasKey(x => new { x.QuoteId, x.VehicleId });
        registration.Property(x => x.NormalizedRegistration).HasMaxLength(12).UseCollation("Latin1_General_100_BIN2");
        registration.HasIndex(x => new { x.NormalizedRegistration, x.QuoteId });
        registration.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.NoAction);
        Check(registration, "Registration", "LEN([NormalizedRegistration]) BETWEEN 2 AND 12 AND [NormalizedRegistration] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_100_BIN2");
        Check(registration, "Vehicle", "[VehicleId]<>'00000000-0000-0000-0000-000000000000'");

        var activity = Record<QuoteActivity>(model, "QuoteActivity");
        activity.ToTable(t => t.UseSqlOutputClause(false)); Text(activity, ("EventType", 100));
        activity.HasOne<QuoteRevision>().WithMany().HasForeignKey(x => new { x.RevisionId, x.QuoteId })
            .HasPrincipalKey(x => new { x.Id, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        activity.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        activity.HasIndex(x => new { x.QuoteId, x.OccurredAt, x.Id });
        Check(activity, "Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId]");
        Check(activity, "EventType", "LEN(TRIM([EventType]))>0");
    }
}
