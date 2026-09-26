using System.Security.Cryptography;
using System.Text;

namespace BackOffice.Api;

public static class DeploymentBoundary
{
    public static bool DemoWorkersEnabled(WebApplicationBuilder builder) =>
        builder.Environment.IsDevelopment() ||
        (builder.Environment.IsStaging() && builder.Configuration.GetValue("Cover:HostedDemoEnabled", false));

    public static bool ValidSecret(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length >= 32;

    public static bool Matches(string? supplied, string expected) => supplied is not null &&
        supplied.Length <= 512 && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    public static void UseDeploymentBoundary(this WebApplication app)
    {
        var required = !app.Environment.IsDevelopment() || app.Configuration.GetValue("Cover:RequireOriginSecret", false);
        if (!required) return;
        var secret = app.Configuration["Cover:OriginSecret"];
        if (!ValidSecret(secret)) throw new InvalidOperationException("Configure a private origin secret of at least 32 characters before hosting the API.");
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
            var supplied = context.Request.Headers["X-Cx1-Origin-Key"];
            if (supplied.Count != 1 || !Matches(supplied[0], secret!))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Request.Headers.Remove("X-Cx1-Origin-Key");
            // Only the authenticated gateway can assert TLS termination for a loopback Tunnel listener.
            if (app.Configuration.GetValue("Cover:TrustGatewayHttps", false) &&
                context.Request.Headers["X-Cx1-Forwarded-Proto"].ToString() == "https")
                context.Request.Scheme = "https";
            context.Request.Headers.Remove("X-Cx1-Forwarded-Proto");
            await next(context);
        });
    }
}
