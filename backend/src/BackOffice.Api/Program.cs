var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
var app = builder.Build();
app.UseExceptionHandler();
app.MapHealthChecks("/health/live");
// Domain endpoints are added only alongside their authentication and persistence.
app.Run();

public partial class Program;
