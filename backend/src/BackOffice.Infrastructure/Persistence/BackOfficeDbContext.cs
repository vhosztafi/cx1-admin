using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext(DbContextOptions<BackOfficeDbContext> options) : DbContext(options)
{
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampUpdates();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,CancellationToken cancellationToken = default)
    {
        StampUpdates();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess,cancellationToken);
    }

    private void StampUpdates()
    {
        foreach (var entry in ChangeTracker.Entries<MutableRecord>().Where(e => e.State == EntityState.Modified))
            entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        var users = Record<StaffUser>(model, "User");
        Text(users, ("Email",254), ("NormalizedEmail",254), ("DisplayName",200), ("State",20), ("SecurityStamp",100));
        users.HasIndex(x => x.NormalizedEmail).IsUnique();
        users.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
        Check(users,"State","[State] IN ('invited','active','suspended')");
        var teams = Record<Team>(model, "Team");
        Text(teams,("Name",100)); teams.HasIndex(x => x.Name).IsUnique();
        var roles = Record<Role>(model,"Role");
        Text(roles,("Code",60),("Scope",20)); roles.HasIndex(x => x.Code).IsUnique();
        Check(roles,"Scope","[Scope] IN ('internal','agency')");
        var links = Record<UserRole>(model,"UserRole");
        links.HasIndex(x => new {x.UserId,x.RoleId}).IsUnique();
        links.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        links.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.NoAction);
        var credentials = Record<UserCredential>(model,"UserCredential");
        Text(credentials,("Provider",30),("ProviderSubject",300),("PasswordHash",1000));
        credentials.HasIndex(x => new {x.Provider,x.ProviderSubject}).IsUnique();
        credentials.HasIndex(x => new {x.UserId,x.Provider}).IsUnique();
        credentials.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        Check(credentials,"FailedAttempts","[FailedAttempts] BETWEEN 0 AND 5");
        var sessions = Record<UserSession>(model,"Session");
        Text(sessions,("DeviceLabel",200),("SecurityStamp",100)); Hash(sessions,"TokenHash");
        sessions.HasIndex(x => x.TokenHash).IsUnique(); sessions.HasIndex(x => new {x.UserId,x.ExpiresAt});
        sessions.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        Check(sessions,"Expiry","[ExpiresAt] > [CreatedAt]");

        var providers = Record<CapacityProvider>(model,"CapacityProvider");
        Text(providers,("Code",50),("Name",200),("State",20)); providers.HasIndex(x => x.Code).IsUnique();
        Check(providers,"State","[State] IN ('active','inactive')");
        var products = Record<Product>(model,"Product");
        Text(products,("Code",60),("Name",200)); products.HasIndex(x => x.Code).IsUnique();
        var versions = Record<ProductVersion>(model,"ProductVersion");
        versions.ToTable(t => t.UseSqlOutputClause(false)); // Published-range validation trigger.
        Text(versions,("State",20),("JsonSchemaVersion",30),("QuestionSetVersion",60)); Json(versions,"Definition");
        versions.HasIndex(x => new {x.ProductId,x.Version}).IsUnique();
        versions.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
        versions.HasOne<CapacityProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.NoAction);
        Check(versions,"Version","[Version] > 0");
        Check(versions,"State","[State] IN ('draft','published','retired')");
        Check(versions,"Validity","[EffectiveTo] IS NULL OR [EffectiveTo] > [EffectiveFrom]");
        var settings = Record<SettingVersion>(model,"SettingVersion");
        Text(settings,("Scope",100)); Json(settings,"Values");
        settings.HasIndex(x => new {x.Scope,x.Version}).IsUnique(); Check(settings,"Version","[Version] > 0");
        var clock = Record<DemoClock>(model,"DemoClock");
        Text(clock,("Name",20)); clock.HasIndex(x => x.Name).IsUnique(); Check(clock,"Singleton","[Name] = 'demo'");

        var audit = Record<AuditEvent>(model,"AuditEvent");
        audit.ToTable(t => t.UseSqlOutputClause(false));
        Text(audit,("EventType",100),("Reason",1000)); Json(audit,"Before",true); Json(audit,"After",true);
        audit.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        audit.HasIndex(x => new {x.SubjectRecordId,x.OccurredAt}); audit.HasIndex(x => new {x.ActorId,x.OccurredAt});
        var work = Record<OutboxWork>(model,"OutboxWork");
        Text(work,("ErrorCode",100));
        work.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        Check(work,"DiagnosticScenario","[Kind] <> 'diagnostic-probe' OR [ScenarioVersionId] IS NOT NULL");
        Text(work,("Kind",60),("OperationKey",200),("State",20)); Json(work,"Payload"); Json(work,"Result",true);
        work.Property(x => x.OperationKey).UseCollation("Latin1_General_100_BIN2");
        work.HasIndex(x => new {x.Kind,x.OperationKey}).IsUnique(); work.HasIndex(x => new {x.State,x.NextAttemptAt});
        Check(work,"State","[State] IN ('pending','leased','succeeded','failed')");
        Check(work,"Attempts","[Attempts] >= 0");
        work.Property(x => x.AttemptLimit).HasDefaultValue(6);
        Check(work,"AttemptLimit","[AttemptLimit] IN (6,12,18) AND [Attempts] <= [AttemptLimit]");
        var exceptions = Record<JobException>(model,"JobException");
        Text(exceptions,("Code",100)); exceptions.HasIndex(x => x.WorkId).IsUnique();
        exceptions.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        var attempts = Record<AdapterAttempt>(model,"AdapterAttempt");
        Text(attempts,("Outcome",30),("ErrorCode",100)); Json(attempts,"Request"); Json(attempts,"Response",true);
        attempts.HasIndex(x => new {x.WorkId,x.AttemptNumber}).IsUnique();
        attempts.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        Check(attempts,"Number","[AttemptNumber] > 0");
        Check(attempts,"Times","[EndedAt] IS NULL OR [EndedAt] >= [StartedAt]");
        var operations = Record<DemoProviderOperation>(model,"DemoProviderOperation");
        Text(operations,("Kind",60),("OperationKey",200),("State",30)); Hash(operations,"RequestHash"); Json(operations,"Result",true);
        operations.Property(x => x.OperationKey).UseCollation("Latin1_General_100_BIN2");
        operations.HasIndex(x => new {x.Kind,x.OperationKey}).IsUnique();
        operations.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        var inbox = Record<AdapterInbox>(model,"AdapterInbox");
        Text(inbox,("Provider",60),("EventId",200),("State",20),("QuarantineReason",1000)); Hash(inbox,"ContentHash");
        inbox.Property(x => x.EventId).UseCollation("Latin1_General_100_BIN2");
        inbox.HasIndex(x => new {x.Provider,x.EventId}).IsUnique();
        inbox.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        Check(inbox,"State","[State] IN ('received','applied','quarantined')");
        var quarantine = Record<AdapterQuarantine>(model,"AdapterQuarantine");
        Hash(quarantine,"ObservedHash"); Text(quarantine,("Reason",1000));
        quarantine.HasIndex(x => new {x.InboxId,x.ObservedHash}).IsUnique();
        quarantine.HasOne<AdapterInbox>().WithMany().HasForeignKey(x => x.InboxId).OnDelete(DeleteBehavior.NoAction);
        var receipts = Record<DiagnosticReceipt>(model,"DiagnosticReceipt");
        Text(receipts,("Reference",100)); receipts.HasIndex(x => x.WorkId).IsUnique(); receipts.HasIndex(x => x.ProviderOperationId).IsUnique();
        receipts.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        receipts.HasOne<DemoProviderOperation>().WithMany().HasForeignKey(x => x.ProviderOperationId).OnDelete(DeleteBehavior.NoAction);
        var idempotency = Record<IdempotencyRecord>(model,"IdempotencyRecord");
        idempotency.ToTable(t => t.UseSqlOutputClause(false));
        idempotency.Property(x => x.Key).UseCollation("Latin1_General_100_BIN2");
        Text(idempotency,("ActorScope",150),("Route",200),("Key",200)); Hash(idempotency,"RequestHash"); Json(idempotency,"ResultBody");
        idempotency.HasIndex(x => new {x.ActorScope,x.Route,x.Key}).IsUnique();
        Check(idempotency,"Status","[ResultStatus] BETWEEN 100 AND 599");

        ConfigureParties(model);
        ConfigureAgencies(model);
        ConfigureAgencyEvidence(model);
        ConfigureAgencyNotifications(model);
        ConfigureAgencyIdentity(model);
        ConfigureAgencyApprovals(model);
        ConfigureAgencyPermissions(model);
        ConfigureSupportFlags(model);
        ConfigureMatches(model);
        ConfigureQuotes(model);
        ConfigureQuoteLookups(model);
        ConfigureQuoteEvidence(model);
        ConfigureUnderwriting(model);
        ConfigureUnderwritingDecisions(model);
        ConfigureCapacity(model);
        ConfigureQuoteTerms(model);
        ConfigurePolicies(model);
        ConfigureIssueFinancials(model);
        // All instants are UTC; retain London intent separately in domain records.
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                model.Entity(entity.ClrType).ToTable(t => t.HasCheckConstraint($"CK_{entity.GetTableName()}_{property.Name}_Utc", $"DATEPART(TZOFFSET,[{property.Name}]) = 0"));
    }

    private static EntityTypeBuilder<T> Record<T>(ModelBuilder model, string table) where T : StoredRecord
    {
        var entity = model.Entity<T>();
        entity.HasBaseType((Type?)null);
        entity.ToTable(table);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        if (typeof(MutableRecord).IsAssignableFrom(typeof(T))) entity.Property<byte[]>("RowVersion").IsRowVersion();
        return entity;
    }
    private static void Text<T>(EntityTypeBuilder<T> entity, params (string Name,int Length)[] fields) where T : class
    {
        foreach (var (name,length) in fields) entity.Property(name).HasMaxLength(length);
    }
    private static void Hash<T>(EntityTypeBuilder<T> entity,string name) where T : class => entity.Property(name).HasColumnType("binary(32)");
    private static void Check<T>(EntityTypeBuilder<T> entity,string name,string sql) where T : class =>
        entity.ToTable(t => t.HasCheckConstraint($"CK_{entity.Metadata.GetTableName()}_{name}",sql));
    private static void Json<T>(EntityTypeBuilder<T> entity,string name,bool nullable = false) where T : class =>
        Check(entity,$"{name}_Json",(nullable ? $"[{name}] IS NULL OR " : "") + $"ISJSON([{name}]) = 1");
}
