using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public static class DemoDatabase
{
    public const string DefaultConnection = "Server=.\\SQL2022;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true";

    public static void ValidateDemoTarget(string connectionString)
    {
        var connection = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(connection.InitialCatalog,"CoverMGA_Demo",StringComparison.Ordinal) || !string.IsNullOrEmpty(connection.AttachDBFilename))
            throw new InvalidOperationException("Demo initialization/reset is restricted to CoverMGA_Demo without attached files.");
    }

    public static async Task InitializeAsync(string connectionString,string password,bool reset,CancellationToken cancellationToken = default)
    {
        ValidateDemoTarget(connectionString);
        ValidatePassword(password); // Validate before any database mutation.
        await using var db = new BackOfficeDbContext(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(160)).Options);
        if (reset) await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await SeedAsync(db,password,cancellationToken,includeSupportFlags:true,includeMatches:true,includeQuoteCapture:true,includeUnderwriting:true,includeRenewalLifecycle:true,includeCommercialCapture:true,includeCommercialUnderwriting:true,includeOperationalWorkflows:true);
    }

    public static void ValidateHostedDevTarget(string connectionString)
    {
        var connection = new SqlConnectionStringBuilder(connectionString);
        if (connection.InitialCatalog != "Cx1_Dev" || !string.IsNullOrEmpty(connection.AttachDBFilename))
            throw new InvalidOperationException("Hosted dev initialization is restricted to Cx1_Dev without attached files.");
    }

    public static async Task InitializeHostedDevAsync(string connectionString, string password, CancellationToken cancellationToken = default)
    {
        ValidateHostedDevTarget(connectionString);
        ValidatePassword(password);
        await using var db = new BackOfficeDbContext(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(160)).Options);
        await db.Database.MigrateAsync(cancellationToken);
        await SeedAsync(db,password,cancellationToken,includeSupportFlags:true,includeMatches:true,includeQuoteCapture:true,includeUnderwriting:true,includeRenewalLifecycle:true,includeCommercialCapture:true,includeCommercialUnderwriting:true,includeOperationalWorkflows:true);
    }

    public static async Task SeedAsync(BackOfficeDbContext db,string password,CancellationToken cancellationToken = default,bool includeSupportFlags=false,bool includeMatches=false,bool includeQuoteCapture=false,bool includeUnderwriting=false,bool includeRenewalLifecycle=false,bool includeAccounting=true,bool includeCommercialCapture=false,bool includeCommercialUnderwriting=false,bool includeOperationalWorkflows=false)
    {
        ValidatePassword(password);
        if (includeCommercialUnderwriting && (!includeUnderwriting || !includeCommercialCapture || !includeQuoteCapture))
            throw new ArgumentException("Commercial underwriting initialization requires its capture and shared underwriting dependencies.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,cancellationToken);
        if (includeCommercialUnderwriting) await Policies.CommercialExposureLock.AcquireAsync(db, cancellationToken);
        // Serialize repeat startup seeds; failure aborts rather than partially applying.
        await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = N'CoverMGA.FoundationSeed', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 51000, 'Foundation seed lock unavailable.', 1;",cancellationToken);
        var team = await db.Set<Team>().SingleOrDefaultAsync(x => x.Name == "Demo operations",cancellationToken);
        if (team is null) { team = new Team {Name="Demo operations"}; db.Add(team); }
        var roleCodes = new[] {"servicing","underwriter","senior-underwriter","agency-admin","finance","system-admin"};
        foreach (var code in roleCodes)
        {
            var role = await db.Set<Role>().SingleOrDefaultAsync(x => x.Code == code,cancellationToken);
            if (role is null) { role = new Role {Code=code}; db.Add(role); }
            var email = $"{code}@cover.example";
            var normalized = email.ToUpperInvariant();
            if (!await db.Set<StaffUser>().AnyAsync(x => x.NormalizedEmail == normalized,cancellationToken))
            {
                var user = new StaffUser {Email=email,NormalizedEmail=normalized,DisplayName=$"Demo {code}",TeamId=team.Id};
                db.Add(user);
                db.Add(new UserRole {UserId=user.Id,RoleId=role.Id});
                db.Add(new UserCredential {UserId=user.Id,ProviderSubject=normalized,PasswordHash=new PasswordHasher<StaffUser>().HashPassword(user,password)});
            }
        }
        // Distinct local administrator for sensitive configuration/identity approvals.
        // Existing users and credential hashes are never overwritten on reseed.
        const string adminReviewerEmail="admin-reviewer@cover.example";
        var adminReviewerNormalized=adminReviewerEmail.ToUpperInvariant();
        if(!await db.Set<StaffUser>().AnyAsync(x=>x.NormalizedEmail==adminReviewerNormalized,cancellationToken))
        {
            var role=db.Set<Role>().Local.SingleOrDefault(x=>x.Code=="system-admin")??await db.Set<Role>().SingleAsync(x=>x.Code=="system-admin",cancellationToken);
            var adminReviewer=new StaffUser{Email=adminReviewerEmail,NormalizedEmail=adminReviewerNormalized,DisplayName="Demo administrator reviewer",TeamId=team.Id};
            db.Add(adminReviewer);db.Add(new UserRole{UserId=adminReviewer.Id,RoleId=role.Id});
            db.Add(new UserCredential{UserId=adminReviewer.Id,ProviderSubject=adminReviewer.NormalizedEmail,PasswordHash=new PasswordHasher<StaffUser>().HashPassword(adminReviewer,password)});
        }
        // Add a distinct countersigner without changing any existing identity or password.
        const string reviewerEmail="agency-reviewer@cover.example";
        var reviewerNormalized=reviewerEmail.ToUpperInvariant();
        if(!await db.Set<StaffUser>().AnyAsync(x=>x.NormalizedEmail==reviewerNormalized,cancellationToken))
        {
            var role=db.Set<Role>().Local.SingleOrDefault(x=>x.Code=="agency-admin")??await db.Set<Role>().SingleAsync(x=>x.Code=="agency-admin",cancellationToken);
            var reviewer=new StaffUser{Email=reviewerEmail,NormalizedEmail=reviewerNormalized,DisplayName="Demo agency reviewer",TeamId=team.Id};
            db.Add(reviewer);db.Add(new UserRole{UserId=reviewer.Id,RoleId=role.Id});
            db.Add(new UserCredential{UserId=reviewer.Id,ProviderSubject=reviewer.NormalizedEmail,PasswordHash=new PasswordHasher<StaffUser>().HashPassword(reviewer,password)});
        }
        // Refund decisions need a second current finance identity; keep the
        // original finance user's credentials and roles untouched on reseed.
        const string financeReviewerEmail="finance-reviewer@cover.example";
        var financeReviewerNormalized=financeReviewerEmail.ToUpperInvariant();
        if(!await db.Set<StaffUser>().AnyAsync(x=>x.NormalizedEmail==financeReviewerNormalized,cancellationToken))
        {
            var role=db.Set<Role>().Local.SingleOrDefault(x=>x.Code=="finance")??await db.Set<Role>().SingleAsync(x=>x.Code=="finance",cancellationToken);
            var financeReviewer=new StaffUser{Email=financeReviewerEmail,NormalizedEmail=financeReviewerNormalized,DisplayName="Demo finance reviewer",TeamId=team.Id};
            db.Add(financeReviewer);db.Add(new UserRole{UserId=financeReviewer.Id,RoleId=role.Id});
            db.Add(new UserCredential{UserId=financeReviewer.Id,ProviderSubject=financeReviewer.NormalizedEmail,PasswordHash=new PasswordHasher<StaffUser>().HashPassword(financeReviewer,password)});
        }
        var provider = await db.Set<CapacityProvider>().SingleOrDefaultAsync(x => x.Code == "demo-capacity",cancellationToken);
        if (provider is null) { provider = new CapacityProvider {Code="demo-capacity",Name="Fictional Demo Capacity"}; db.Add(provider); }
        var effective = new DateTimeOffset(2026,9,1,0,0,0,TimeSpan.Zero);
        foreach (var (code,name) in new[] {("motor-trade-road-risks","Motor Trade Road Risks"),("motor-trade-combined","Motor Trade Combined"),("commercial-combined","Commercial Combined")})
        {
            var product = await db.Set<Product>().SingleOrDefaultAsync(x => x.Code == code,cancellationToken);
            if (product is null) { product = new Product {Code=code,Name=name}; db.Add(product); }
            if (!await db.Set<ProductVersion>().AnyAsync(x => x.ProductId == product.Id && x.Version == 1,cancellationToken))
                db.Add(new ProductVersion {ProductId=product.Id,Version=1,ProviderId=provider.Id,EffectiveFrom=effective,JsonSchemaVersion="1.0",QuestionSetVersion="demo-1",
                    Definition=JsonSerializer.Serialize(new {demo=true,productCode=code,status="foundation-only",ratingAvailable=false})});
        }
        if (!await db.Set<SettingVersion>().AnyAsync(x => x.Scope == "demo-adapters" && x.Version == 1,cancellationToken))
            db.Add(new SettingVersion {Scope="demo-adapters",Version=1,EffectiveFrom=effective,Values="{\"demo\":true,\"scenario\":\"success\"}"});
        foreach (var scenario in new[] {"success","reject","fail-once","timeout-after-success"})
        {
            var scope="diagnostic-probe/"+scenario;
            if (!await db.Set<SettingVersion>().AnyAsync(x => x.Scope==scope && x.Version==1,cancellationToken))
                db.Add(new SettingVersion {Scope=scope,Version=1,EffectiveFrom=effective,
                    Values=JsonSerializer.Serialize(new {kind="diagnostic-probe",scenario})});
        }
        if (!await db.Set<DemoClock>().AnyAsync(cancellationToken)) db.Add(new DemoClock {FrozenAt=new DateTimeOffset(2026,9,13,12,0,0,TimeSpan.Zero)});
        await db.SaveChangesAsync(cancellationToken);
        await PartyDemoSeed.SeedAsync(db,cancellationToken);
        var processingClock = await db.Set<DemoClock>().SingleAsync(cancellationToken);
        if(includeAccounting)await Policies.AccountingPeriods.SeedAsync(db,processingClock.FrozenAt ?? DateTimeOffset.UtcNow,cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await ContactDemoSeed.SeedAsync(db,cancellationToken);
        await AgencyDemoSeed.SeedAsync(db,cancellationToken);
        if(includeQuoteCapture)
        {
            await QuoteCaptureDemoSeed.SeedAsync(db,cancellationToken);
            await QuoteLookupDemoSeed.SeedAsync(db,cancellationToken);
        }
        if(includeSupportFlags)await SupportFlagDemoSeed.SeedAsync(db,cancellationToken);
        if(includeMatches)await MatchDemoSeed.SeedAsync(db,cancellationToken);
        if(includeUnderwriting)
        {
            await Underwriting.UnderwritingSeed.SeedAsync(db,cancellationToken);
            await Underwriting.UnderwritingRuntimeSeed.SeedAsync(db,cancellationToken);
            await Underwriting.CapacitySeed.SeedAsync(db,cancellationToken);
            await Underwriting.QuoteTermsSeed.SeedAsync(db,cancellationToken);
            await Policies.PolicyTemplateSeed.SeedAsync(db,cancellationToken);
            await Policies.ServicingRatingSeed.SeedAsync(db,cancellationToken);
            await Policies.ServicingTermsSeed.SeedAsync(db,cancellationToken);
            await Policies.PolicyClientActivitySeed.SeedAsync(db,cancellationToken);
            await Policies.RenewalPreparationSeed.SeedAsync(db,cancellationToken);
        }
        if(includeRenewalLifecycle)await Policies.RenewalLifecycleSeed.SeedAsync(db,cancellationToken);
        if(includeOperationalWorkflows)
        {
            await Operations.WorkflowTaskSeed.SeedAsync(db,cancellationToken);
            await Operations.DocumentTemplateSeed.SeedAsync(db,cancellationToken);
            await Operations.OperationalDeliverySeed.Seed(db,cancellationToken);
            await Operations.OperationalClaimsSeed.Seed(db,cancellationToken);
            await Operations.OperationalMidSeed.Seed(db,cancellationToken);
        }
        if(includeCommercialCapture)await Quotes.CommercialCaptureSeed.SeedAsync(db,cancellationToken);
        if(includeCommercialUnderwriting)
        {
            await Underwriting.CommercialUnderwritingSeed.SeedAsync(db,cancellationToken);
            await Policies.CommercialExposureSeed.SeedAsync(db,cancellationToken);
            await Policies.RenewalPreparationSeed.SeedCommercialAsync(db,cancellationToken);
            await Policies.CommercialUnderwritingCancellationSeed.SeedAsync(db,cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 12 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || !password.Any(c => !char.IsLetterOrDigit(c)))
            throw new ArgumentException("Demo password needs 12 characters, upper/lower case, number and symbol.",nameof(password));
    }
}
