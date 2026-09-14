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
DiagnosticDispatcher.Register(builder);
var app = builder.Build();
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
app.MapOperationalJobs();
app.MapOperationalReads();
app.MapOperationalRetries();
app.MapOperationalSettings();
app.MapClients();
// Domain endpoints are added only alongside their authentication and persistence.
app.Run();

public partial class Program;
