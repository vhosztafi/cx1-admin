using BackOffice.Infrastructure.Persistence;

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
var app = builder.Build();
app.UseExceptionHandler();
app.MapHealthChecks("/health/live");
// Domain endpoints are added only alongside their authentication and persistence.
app.Run();

public partial class Program;
