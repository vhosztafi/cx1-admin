using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class IdentityEndpoints
{
    public static void AddLocalIdentity(this WebApplicationBuilder builder)
    {
        var development = builder.Environment.IsDevelopment();
        var connection = builder.Configuration["Cover:SqlConnection"] ?? Environment.GetEnvironmentVariable("COVER_SQL_CONNECTION") ?? DemoDatabase.DefaultConnection;
        builder.Services.AddDbContextFactory<BackOfficeDbContext>(options => options.UseSqlServer(connection,sql => sql.UseCompatibilityLevel(160)));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<LocalIdentityService>();
        builder.Services.AddSingleton<SqlTicketStore>();
        var keyPath = builder.Configuration["Cover:DataProtectionPath"];
        if (!development && string.IsNullOrWhiteSpace(keyPath)) throw new InvalidOperationException("Configure persistent Data Protection storage for this environment.");
        var keys = new DirectoryInfo(Path.GetFullPath(keyPath ?? Path.Combine(builder.Environment.ContentRootPath,".local","data-protection")));
        var protection = builder.Services.AddDataProtection().SetApplicationName("CoverMGA.BackOffice.v1").PersistKeysToFileSystem(keys);
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        else if (!development) throw new InvalidOperationException("Configure encrypted Data Protection keys before hosting outside local Windows development.");
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow);
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name=development ? "cover-dev-session" : "__Host-cover-session";
            options.Cookie.HttpOnly=true; options.Cookie.Path="/"; options.Cookie.SameSite=SameSiteMode.Lax;
            options.Cookie.SecurePolicy=development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan=TimeSpan.FromHours(8); options.SlidingExpiration=false;
            options.Events.OnRedirectToLogin=context => Problem(context.HttpContext,401,"authentication-required","Sign in required.").ExecuteAsync(context.HttpContext);
            options.Events.OnRedirectToAccessDenied=context => Problem(context.HttpContext,403,"forbidden","Access denied.").ExecuteAsync(context.HttpContext);
        });
        builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<SqlTicketStore>((options,store) => options.SessionStore=store);
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy=new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var capability in new[] {"platform-admin","integration-admin","integration-retry","audit-read","client-servicing","finance","finance-read","finance-cash-write","finance-reconcile","finance-refund-request","finance-refund-approve","statement-generate","finance-bordereau","subject-read","task-read","task-write","task-assign",
                "document-read","document-download","document-upload","document-generate",
                "mid-read","mid-retry","incident-read","incident-write","incident-handoff","internal-note-read","internal-note-write","message-read","message-write","message-send","document-send",
                "client-read","client-write","relationship-read","contact-write","support-internal-read","support-write","support-safe-read-explicit-grant","match-read","match-review","agency-read","agency-admin","quote-read","quote-capture",
                "quote-rate","quote-submit","quote-revise","quote-terms","quote-acceptance","underwriting-read","underwriting-evidence-write","underwriting-evidence-review",
                "underwriting-decide-within-authority","underwriting-escalate","underwriting-record-capacity","policy-read","policy-discovery-read","policy-issue-within-authority","policy-draft-write","policy-draft-rate","policy-draft-takeover"})
                options.AddPolicy(capability,policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
                    context.User.Identity?.IsAuthenticated == true && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier),out _) &&
                    LocalIdentityService.Actor(context.User).HasCapability(capability)));
        });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("commercial-exposure-read", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
            {
                if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out _)) return false;
                var actor = LocalIdentityService.Actor(context.User);
                return actor.HasCapability("policy-read") || actor.AgencyId != null;
            }));
            options.AddPolicy("agency-context", policy => policy.RequireAuthenticatedUser().RequireAssertion(context => context.User.Identity?.IsAuthenticated == true && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier),out _) && LocalIdentityService.Actor(context.User).AgencyId != null));
            options.AddPolicy("agency-own-users", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
            {
                if (context.User.Identity?.IsAuthenticated != true || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier),out _)) return false;
                var actor = LocalIdentityService.Actor(context.User);
                return actor.HasCapability("agency-admin") || (actor.AgencyId != null && actor.Roles.SetEquals(new[] { "broker-admin" }));
            }));
        });
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName="X-CSRF-Token";
            // API clients use the header. Do not parse an untrusted multipart
            // body before the endpoint's explicit upload limits run.
            options.SuppressReadingTokenFromFormBody=true;
            options.Cookie.Name=development ? "cover-dev-csrf" : "__Host-cover-csrf";
            options.Cookie.Path="/"; options.Cookie.HttpOnly=true; options.Cookie.SameSite=SameSiteMode.Strict;
            options.Cookie.SecurePolicy=development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("invitation-acceptance",context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",_ => new FixedWindowRateLimiterOptions
                {PermitLimit=20,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
            options.AddPolicy("login",context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",_ => new FixedWindowRateLimiterOptions
                {PermitLimit=20,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
            options.OnRejected=async (context,token) =>
            {
                context.HttpContext.Response.Headers.RetryAfter="60";
                await Problem(context.HttpContext,429,"rate-limited","Try again later.").ExecuteAsync(context.HttpContext);
            };
        });
    }

    public static void UseLocalIdentity(this WebApplication app)
    {
        app.Use(async (context,next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.Headers.CacheControl="no-store";
                context.Response.Headers.Pragma="no-cache";
                if (!app.Environment.IsDevelopment() && !context.Request.IsHttps)
                {
                    await Problem(context,400,"https-required","HTTPS is required.").ExecuteAsync(context); return;
                }
            }
            await next(context);
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.Use(async (context,next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
            {
                try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException)
                {
                    await Problem(context,403,"csrf-invalid","Refresh the page and retry.").ExecuteAsync(context); return;
                }
            }
            await next(context);
        });
    }

    public static void MapIdentity(this WebApplication app)
    {
        app.MapGet("/api/v1/auth/csrf",(HttpContext context,IAntiforgery antiforgery) =>
            Results.Ok(new {requestToken=antiforgery.GetAndStoreTokens(context).RequestToken})).AllowAnonymous();
        app.MapPost("/api/v1/auth/login",async (LoginRequest input,HttpContext context,LocalIdentityService identity) =>
        {
            if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 254 || string.IsNullOrEmpty(input.Password) || input.Password.Length > 1024)
                return Problem(context,422,"invalid-credentials-input","Email and password are required.");
            var result = await identity.AuthenticateAsync(input.Email,input.Password,context.RequestAborted);
            if (result is null) return Problem(context,401,"invalid-credentials","Unable to sign in with these credentials.");
            // Rotate any existing session on explicit sign-in.
            if (context.User.Identity?.IsAuthenticated == true) await context.SignOutAsync();
            await context.SignInAsync(result.Principal,new AuthenticationProperties {IsPersistent=false});
            return Results.Ok(new {state="authenticated",user=result.View});
        }).AllowAnonymous().RequireRateLimiting("login");
        app.MapGet("/api/v1/account",async (HttpContext context,LocalIdentityService identity) =>
        {
            var actor = await identity.GetActorAsync(Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!),context.RequestAborted);
            return actor is null ? Problem(context,401,"authentication-required","Sign in required.") : Results.Ok(actor);
        }).RequireAuthorization();
        app.MapPost("/api/v1/auth/logout",async (HttpContext context,TimeProvider time) =>
        {
            var id=Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await context.SignOutAsync();
            return Results.Ok(new {id,updatedAt=time.GetUtcNow()});
        }).RequireAuthorization();
    }

    public static IResult Problem(HttpContext context,int status,string code,string title) => Results.Problem(
        type:"about:blank",statusCode:status,title:title,extensions:new Dictionary<string,object?> {{"code",code},{"traceId",context.TraceIdentifier}});
    public sealed record LoginRequest(string? Email,string? Password);
}
