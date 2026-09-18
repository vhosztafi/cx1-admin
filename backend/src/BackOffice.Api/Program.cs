using BackOffice.Infrastructure.Persistence;
using BackOffice.Api;

if (args.Contains("--initialize-demo", StringComparer.Ordinal))
{
    var password = Environment.GetEnvironmentVariable("COVER_DEMO_PASSWORD")
        ?? throw new InvalidOperationException("Set COVER_DEMO_PASSWORD locally before initialization.");
    var connection = Environment.GetEnvironmentVariable("COVER_SQL_CONNECTION") ?? DemoDatabase.DefaultConnection;
    await DemoDatabase.InitializeAsync(connection,password,args.Contains("--reset-demo",StringComparer.Ordinal));
    Console.WriteLine("CoverMGA_Demo migrated and fictional foundation data seeded.");
    return;
}
if (args.Contains("--reset-demo",StringComparer.Ordinal)) throw new InvalidOperationException("Reset requires --initialize-demo --reset-demo.");

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.AddLocalIdentity();
builder.Services.AddSingleton<PartyPaging>();
builder.Services.AddScoped<BackOffice.Infrastructure.Quotes.QuoteService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Quotes.QuoteEvidenceService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Quotes.QuoteLifecycleService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Quotes.QuoteProducts>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencyDraftService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencyEvidenceService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencyNotificationRetry>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.InvitationAcceptance>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.InvitationDemoReveal>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.InvitationService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencyUserService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencyUserLifecycle>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencyInvitationCommands>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencyActivationService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencyActivationDecisions>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencySuspensionService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencyReactivationAssessment>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencyReactivationService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencyTermsService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Agencies.AgencyPermissionService>();
DiagnosticDispatcher.Register(builder);
AgencyNotificationDispatcher.Register(builder);
QuoteLookupDispatcher.Register(builder);
QuoteRatingDispatcher.Register(builder);
ServicingRatingDispatcher.Register(builder);
CapacityDispatcher.Register(builder);
ServicingCapacityDispatcher.Register(builder);
ServicingDeliveryDispatcher.Register(builder);
QuoteDeliveryDispatcher.Register(builder);
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.QuoteIssueService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.ServicingIssueService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.PolicyReadService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.ServicingDraftService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.RenewalPreparationService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.ServicingEvidenceService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.ServicingSubmissionService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.ServicingReferralService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.PolicyDiscoveryService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Underwriting.UnderwritingEvidenceService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Underwriting.QuoteReferralService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Underwriting.QuoteReferralReadModel>();
var app = builder.Build();
if(args.Contains("--seed-accounting-periods-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Accounting fixtures require local Development.");
    var factory=app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>();
    await using var db=await factory.CreateDbContextAsync();
    DemoDatabase.ValidateDemoTarget(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)!);
    await using var transaction=await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.BeginTransactionAsync(db.Database,System.Data.IsolationLevel.Serializable);
    var clock=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Set<DemoClock>());
    await BackOffice.Infrastructure.Policies.AccountingPeriods.SeedAsync(db,clock.FrozenAt??DateTimeOffset.UtcNow);await transaction.CommitAsync();
    Console.WriteLine("Missing fictional accounting periods added; existing periods preserved.");return;
}
if(args.Contains("--seed-renewal-lifecycle-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Renewal fixtures require local Development.");
    var factory=app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>();
    await using var db=await factory.CreateDbContextAsync();
    DemoDatabase.ValidateDemoTarget(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)!);
    await using var transaction=await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.BeginTransactionAsync(db.Database,System.Data.IsolationLevel.Serializable);
    await BackOffice.Infrastructure.Policies.RenewalLifecycleSeed.SeedAsync(db);await transaction.CommitAsync();
    Console.WriteLine("Missing fictional renewal invitation templates added; existing records preserved.");return;
}
if(args.Contains("--seed-renewal-preparation-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Renewal fixtures require local Development.");
    var factory=app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>();
    await using var db=await factory.CreateDbContextAsync();
    DemoDatabase.ValidateDemoTarget(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)!);
    await using var transaction=await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.BeginTransactionAsync(db.Database,System.Data.IsolationLevel.Serializable);
    await BackOffice.Infrastructure.Policies.RenewalPreparationSeed.SeedAsync(db);await transaction.CommitAsync();
    Console.WriteLine("Missing fictional renewal settings and fair value evidence added; existing records preserved.");return;
}
if(args.Contains("--seed-servicing-terms-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Servicing terms fixture requires local Development.");
    var factory=app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>();
    await using var db=await factory.CreateDbContextAsync();
    DemoDatabase.ValidateDemoTarget(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)!);
    await using var transaction=await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.BeginTransactionAsync(db.Database,System.Data.IsolationLevel.Serializable);
    await BackOffice.Infrastructure.Policies.ServicingTermsSeed.SeedAsync(db);await transaction.CommitAsync();
    Console.WriteLine("Missing fictional servicing terms templates and delivery scenario added.");return;
}
if(args.Contains("--seed-quote-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Quote fixture requires local Development.");
    var factory=app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>();
    await using(var db=await factory.CreateDbContextAsync()) DemoDatabase.ValidateDemoTarget(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)!);
    var result=await new BackOffice.Infrastructure.Quotes.QuoteDemo(factory,TimeProvider.System).SeedAsync();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));return;
}
if(args.Contains("--seed-agency-invitation-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Invitation fixture requires local Development.");
    var id=await BackOffice.Infrastructure.Agencies.AgencyInvitationDemo.Create(
        app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>(),
        app.Services.GetRequiredService<BackOffice.Infrastructure.Agencies.AgencyNotificationPayload>());
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{invitationId=id}));return;
}
if(args.Contains("--seed-agency-notification-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Notification fixture requires local Development.");
    var id=await BackOffice.Infrastructure.Agencies.AgencyNotificationDemo.Create(
        app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>(),
        app.Services.GetRequiredService<BackOffice.Infrastructure.Agencies.AgencyNotificationPayload>());
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{agencyId=id}));return;
}
app.UseExceptionHandler(handler => handler.Run(context =>
{
    var error=context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    return error is BadHttpRequestException bad
        ? IdentityEndpoints.Problem(context,bad.StatusCode,"invalid-request","Request could not be read.").ExecuteAsync(context)
        : IdentityEndpoints.Problem(context,503,"service-unavailable","Service temporarily unavailable.").ExecuteAsync(context);
}));
app.UseStatusCodePages(context => IdentityEndpoints.Problem(context.HttpContext,context.HttpContext.Response.StatusCode,"request-failed","Request could not be completed.").ExecuteAsync(context.HttpContext));
app.UseLocalIdentity();
app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapIdentity();
app.MapInvitations();
app.MapAgencyUsers();
app.MapOperationalJobs();
app.MapOperationalReads();
app.MapOperationalRetries();
app.MapOperationalSettings();
app.MapClients();
app.MapAgencies();
app.MapAgencyStateRequests();
app.MapAgencyTerms();
app.MapAgencyPermissions();
app.MapAgencySharing();
app.MapAgencyContext();
app.MapAgencyEvidence();
app.MapAgencyNotifications();
app.MapContacts();
app.MapSupportFlags();
app.MapMatches();
app.MapQuotes();
app.MapQuoteDiscovery();
app.MapQuoteLookups();
app.MapQuoteEvidence();
app.MapQuoteLifecycle();
app.MapQuoteUnderwriting();
app.MapUnderwritingEvidence();
app.MapQuoteReferrals();
app.MapCapacity();
app.MapQuoteTerms();
app.MapQuoteIssue();
app.MapPolicies();
app.MapServicingDrafts();
app.MapRenewalPreparation();
app.MapServicingRatings();
app.MapServicingProofReads();
app.MapServicingProofCommands();
app.MapServicingSubmissions();
app.MapServicingCapacity();
app.MapServicingTerms();
app.MapServicingIssue();
// Domain endpoints are added only alongside their authentication and persistence.
app.Run();

public partial class Program;
