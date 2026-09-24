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

var builder = WebApplication.CreateBuilder(args.Where(x=>x is not ("--seed-commercial-proposals-demo" or "--seed-commercial-authority-demo" or "--seed-operational-demo" or "--prepare-operational-commercial-demo" or "--register-operational-mid-demo")).ToArray());
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.AddLocalIdentity();
builder.Services.AddSingleton<PartyPaging>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.TaskService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.NoteService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.ThreadService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.AgencyResponseService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.LegacyOperationalBridge>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.OperationalDemoSeed>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.OperationalDemoQuoteSeed>();
if(builder.Environment.IsDevelopment()&&builder.Configuration.GetValue("Cover:LegacyOperationalWorkerEnabled",false))
    builder.Services.AddHostedService<LegacyOperationalDispatcher>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.MessageDeliveryService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.DocumentPackService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.DeliveryReadService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.IncidentOccurrenceResolver>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.IncidentService>();
MessageDeliveryDispatcher.Register(builder);
ClaimsDispatcher.Register(builder);
MidDispatcher.Register(builder);
FinanceSubmissionDispatcher.Register(builder);
FinancePaymentDispatcher.Register(builder);
CancellationOperationsDispatcher.Register(builder);
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.ClaimsHandoffService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.ClaimsSummaryService>();
builder.Services.AddSingleton<BackOffice.Application.Operations.IPolicyDocumentRenderer, BackOffice.Infrastructure.Operations.PolicyDocumentRenderer>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.PolicyDocumentRenderService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.DocumentService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Operations.DocumentGenerationWorker>();
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
RenewalLifecycleDispatcher.Register(builder);
WorkflowTaskDispatcher.Register(builder);
FileFinalizationDispatcher.Register(builder);
DocumentGenerationDispatcher.Register(builder);
CancellationNoticeDispatcher.Register(builder);
QuoteDeliveryDispatcher.Register(builder);
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.QuoteIssueService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.ServicingIssueService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.PolicyReadService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Finance.FinanceLedgerService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Finance.FinanceStatementService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Finance.FinanceBordereauService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Finance.FinanceSubmissionService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Finance.FinanceReceiptService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Finance.FinanceReconciliationService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Finance.FinanceRefundService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Finance.FinancePaymentService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Finance.FinancePeriodService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.CommercialExposureReadModel>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.PolicyHistoryService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.ServicingDraftService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.RenewalPreparationService>();
builder.Services.AddScoped<BackOffice.Infrastructure.Policies.CancellationReviewService>();
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
if(args.Contains("--register-operational-mid-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Operational fixtures require local Development.");
    var index=Array.IndexOf(args,"--version-id");
    if(index<0||index+1>=args.Length||!Guid.TryParse(args[index+1],out var versionId)||versionId==Guid.Empty)throw new InvalidOperationException("An existing issued Motor Trade --version-id is required.");
    var factory=app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>();
    await using var db=await factory.CreateDbContextAsync();DemoDatabase.ValidateDemoTarget(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)!);
    var user=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Set<StaffUser>(),x=>x.Email=="senior-underwriter@cover.example"&&x.State=="active"&&x.AgencyId==null);
    var roles=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToArrayAsync(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id select role.Code);
    var actor=new BackOffice.Application.ActorContext(user.Id,user.TeamId,null,roles.ToHashSet(StringComparer.Ordinal));
    await using var seedScope=app.Services.CreateAsyncScope();
    var service=seedScope.ServiceProvider.GetRequiredService<BackOffice.Infrastructure.Operations.MidSubmissionRegistration>();
    var work=await service.RegisterMissingInitial(actor,versionId,"Prepare retained fictional Motor Trade MID demonstration","operational-mid-demo:"+versionId.ToString("D"));
    var submission=await service.Register(work.ResourceId);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{versionId,workId=work.ResourceId,submissionId=submission}));return;
}
if(args.Contains("--prepare-operational-commercial-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Operational fixtures require local Development.");
    string Value(string name){var index=Array.IndexOf(args,name);if(index<0||index+1>=args.Length)throw new InvalidOperationException(name+" is required.");return args[index+1];}
    if(!Guid.TryParse(Value("--relationship-id"),out var relationshipId)||relationshipId==Guid.Empty||!Guid.TryParse(Value("--product-version-id"),out var productVersionId)||productVersionId==Guid.Empty||
        !DateOnly.TryParseExact(Value("--starts-on"),"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var startsOn))
        throw new InvalidOperationException("Choose valid existing relationship/product IDs and a YYYY-MM-DD start date.");
    var factory=app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>();
    await using var db=await factory.CreateDbContextAsync();DemoDatabase.ValidateDemoTarget(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)!);
    var user=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Set<StaffUser>(),x=>x.Email=="senior-underwriter@cover.example"&&x.State=="active"&&x.AgencyId==null);
    var roles=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToArrayAsync(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id select role.Code);
    var actor=new BackOffice.Application.ActorContext(user.Id,user.TeamId,null,roles.ToHashSet(StringComparer.Ordinal));
    await using var seedScope=app.Services.CreateAsyncScope();
    var result=await seedScope.ServiceProvider.GetRequiredService<BackOffice.Infrastructure.Operations.OperationalDemoQuoteSeed>().PrepareCommercialIncident(actor,relationshipId,productVersionId,startsOn);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));return;
}
if(args.Contains("--seed-operational-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Operational fixtures require local Development.");
    var factory=app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>();
    await using(var db=await factory.CreateDbContextAsync()) DemoDatabase.ValidateDemoTarget(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)!);
    await using var seedScope=app.Services.CreateAsyncScope();
    var seed=seedScope.ServiceProvider.GetRequiredService<BackOffice.Infrastructure.Operations.OperationalDemoSeed>();
    var matching=await seed.Initialize();
    await using var read=await factory.CreateDbContextAsync();
    var operatorEmail=app.Configuration["Cover:OperationalDemoOperator"]??"senior-underwriter@cover.example";
    var user=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(read.Set<StaffUser>(),x=>x.Email==operatorEmail&&x.State=="active"&&x.AgencyId==null);
    var roles=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToArrayAsync(from link in read.Set<UserRole>() join role in read.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id select role.Code);
    var actor=new BackOffice.Application.ActorContext(user.Id,user.TeamId,null,roles.ToHashSet(StringComparer.Ordinal));
    var renewals=await seed.InitializeRenewals(actor);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{matching,renewals}));return;
}
if(args.Contains("--seed-quote-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Quote fixture requires local Development.");
    var factory=app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>();
    await using(var db=await factory.CreateDbContextAsync()) DemoDatabase.ValidateDemoTarget(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)!);
    var result=await new BackOffice.Infrastructure.Quotes.QuoteDemo(factory,TimeProvider.System).SeedAsync();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));return;
}
if(args.Contains("--seed-commercial-authority-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Commercial demo authority requires local Development.");
    var factory=app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>();
    await using var db=await factory.CreateDbContextAsync();
    DemoDatabase.ValidateDemoTarget(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)!);
    await using var transaction=await db.Database.BeginTransactionAsync();
    var id=await BackOffice.Infrastructure.Policies.CommercialDemoAuthoritySeed.SeedAsync(db,app.Services.GetRequiredService<TimeProvider>().GetUtcNow());
    await transaction.CommitAsync();Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { grantId=id }));return;
}
if(args.Contains("--seed-commercial-proposals-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Commercial fixtures require local Development.");
    var factory=app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>();
    await using var db=await factory.CreateDbContextAsync();
    DemoDatabase.ValidateDemoTarget(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)!);
    Guid RequiredId(string name)
    {
        var index=Array.IndexOf(args,name);
        if(index<0||index+1>=args.Length||!Guid.TryParse(args[index+1],out var id)||id==Guid.Empty)throw new InvalidOperationException($"{name} requires an existing fictional identifier.");
        return id;
    }
    var user=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Set<StaffUser>(),x=>x.Email=="senior-underwriter@cover.example"&&x.State=="active");
    var roles=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToArrayAsync(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id select role.Code);
    var actor=new BackOffice.Application.ActorContext(user.Id,user.TeamId,user.AgencyId,roles.ToHashSet(StringComparer.Ordinal));
    var result=await new BackOffice.Infrastructure.Policies.CommercialDemoSeed(factory,app.Services.GetRequiredService<TimeProvider>())
        .SeedAsync(actor,RequiredId("--relationship-id"),RequiredId("--product-version-id"));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));return;
}
if(args.Contains("--seed-servicing-drafts-demo",StringComparer.Ordinal))
{
    if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Servicing draft fixtures require local Development.");
    var factory=app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BackOfficeDbContext>>();
    await using var db=await factory.CreateDbContextAsync();
    DemoDatabase.ValidateDemoTarget(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(db.Database)!);
    var user=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Set<StaffUser>(),x=>x.Email=="underwriter@cover.example" && x.State=="active");
    var roles=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToArrayAsync(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id select role.Code);
    var actor=new BackOffice.Application.ActorContext(user.Id,user.TeamId,user.AgencyId,roles.ToHashSet(StringComparer.Ordinal));
    var policies=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToArrayAsync(from policy in db.Set<Policy>() join product in db.Set<Product>() on policy.ProductId equals product.Id
        where product.Code=="motor-trade-combined" || product.Code=="motor-trade-road-risks" orderby policy.Number select new{policy.Id,product.Code});
    var selected=policies.GroupBy(x=>x.Code).Select(x=>x.First()).ToArray();
    if(selected.Length!=2)throw new InvalidOperationException("Issue both fictional Motor Trade product examples before seeding servicing drafts.");
    var seed=new BackOffice.Infrastructure.Policies.ServicingDemoSeed(factory,app.Services.GetRequiredService<TimeProvider>());
    var results=new List<BackOffice.Infrastructure.Policies.ServicingDemoDraft>();
    foreach(var policy in selected)results.AddRange(await seed.SeedDraftsAsync(actor,policy.Id));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(results));return;
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
app.MapCommercialExposure();
app.MapPolicies();
app.MapFinanceLedger();
app.MapFinanceStatements();
app.MapFinanceBordereaux();
app.MapFinanceSubmissions();
app.MapFinanceReceipts();
app.MapFinanceReconciliations();
app.MapFinanceRefunds();
app.MapFinancePayments();
app.MapFinancePeriods();
app.MapTasks();
app.MapCommunications();
app.MapDeliveries();
app.MapIncidents();
app.MapClaims();
app.MapMid();
app.MapCancellationOperations();
app.MapFiles();
app.MapDocuments();
app.MapServicingDrafts();
app.MapRenewalPreparation();
app.MapRenewalLifecycle();
app.MapCancellationReview();
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
