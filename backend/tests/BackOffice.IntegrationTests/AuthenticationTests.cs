using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task RealSqlAuthenticationSurvivesRestartAndEnforcesSecurity()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");
        connection.InitialCatalog=ownedName; connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql => sql.UseCompatibilityLevel(160)).Options;
        var keys=Path.GetFullPath(Path.Combine(".local","auth-test-keys",ownedName));
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        try
        {
            await using (var seed=new BackOfficeDbContext(options)) {await seed.Database.MigrateAsync(); await DemoDatabase.SeedAsync(seed,password);}
            string sessionCookie;
            using (var first=Factory(connection.ConnectionString,keys))
            using (var client=Client(first))
            {
                using var anonymous=await client.GetAsync("/api/v1/account");
                Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
                Assert.Equal("application/problem+json",anonymous.Content.Headers.ContentType?.MediaType);
                using var missingCsrf=await client.PostAsJsonAsync("/api/v1/auth/login",new {email="servicing@cover.example",password});
                Assert.Equal(HttpStatusCode.Forbidden,missingCsrf.StatusCode);
                var csrf=await Csrf(client);
                using var unknownField=await Post(client,csrf,new {email="servicing@cover.example",password,role="system-admin"});
                Assert.Equal(HttpStatusCode.BadRequest,unknownField.StatusCode);
                using var wrong=await Post(client,csrf,new {email="servicing@cover.example",password="Incorrect!a1234"});
                Assert.Equal(HttpStatusCode.Unauthorized,wrong.StatusCode);
                using var login=await Post(client,csrf,new {email="servicing@cover.example",password});
                Assert.Equal(HttpStatusCode.OK,login.StatusCode);
                var body=await login.Content.ReadAsStringAsync();
                Assert.DoesNotContain(password,body); Assert.DoesNotContain("securityStamp",body); Assert.DoesNotContain("passwordHash",body);
                var header=login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("cover-dev-session=",StringComparison.Ordinal));
                Assert.Contains("httponly",header,StringComparison.OrdinalIgnoreCase);
                Assert.Contains("samesite=lax",header,StringComparison.OrdinalIgnoreCase);
                Assert.Contains("path=/",header,StringComparison.OrdinalIgnoreCase);
                sessionCookie=header.Split(';')[0];
                using var actor=await client.GetAsync("/api/v1/account");
                Assert.Equal(HttpStatusCode.OK,actor.StatusCode);
                Assert.True(actor.Headers.CacheControl?.NoStore);
                await using (var db=new BackOfficeDbContext(options))
                    await db.Set<UserSession>().ExecuteUpdateAsync(x=>x.SetProperty(session=>session.LastSeenAt,DateTimeOffset.UtcNow.AddMinutes(-2)));
                var parallel=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>client.GetAsync("/api/v1/account")));
                foreach(var response in parallel){Assert.Equal(HttpStatusCode.OK,response.StatusCode);response.Dispose();}

                using var forbidden=await client.GetAsync("/test/admin");
                Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
                using var staleCsrf=await Post(client,csrf,null,"/api/v1/auth/logout");
                Assert.Equal(HttpStatusCode.Forbidden,staleCsrf.StatusCode); // Anonymous token cannot mutate authenticated session.
            }
            using (var restarted=Factory(connection.ConnectionString,keys))
            using (var client=Client(restarted,sessionCookie))
            {
                using var actor=await client.GetAsync("/api/v1/account");
                Assert.Equal(HttpStatusCode.OK,actor.StatusCode);
                using var logout=await Post(client,await Csrf(client),null,"/api/v1/auth/logout");
                Assert.Equal(HttpStatusCode.OK,logout.StatusCode);
                // The captured cookie is still sent manually: SQL revocation must reject it.
                using var revoked=await client.GetAsync("/api/v1/account");
                Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
            }
            using (var factory=Factory(connection.ConnectionString,keys))
            {
                // Persisted lockout survives independently created request clients.
                using var failedClient=Client(factory);
                var csrf=await Csrf(failedClient);
                for (var attempt=0;attempt<5;attempt++)
                {
                    using var failed=await Post(failedClient,csrf,new {email="finance@cover.example",password="Wrong!a123456"});
                    Assert.Equal(HttpStatusCode.Unauthorized,failed.StatusCode);
                }
                using var locked=await Post(failedClient,csrf,new {email="finance@cover.example",password});
                Assert.Equal(HttpStatusCode.Unauthorized,locked.StatusCode);
                await using (var db=new BackOfficeDbContext(options))
                {
                    var credential=await db.Set<UserCredential>().SingleAsync(x => x.ProviderSubject=="FINANCE@COVER.EXAMPLE");
                    Assert.Equal(5,credential.FailedAttempts); Assert.True(credential.LockedUntil>DateTimeOffset.UtcNow);
                }
                foreach (var rejection in new[] {"ticket-subject","expired","stamp","suspended"})
                {
                    using var client=Client(factory);
                    using var login=await Post(client,await Csrf(client),new {email="underwriter@cover.example",password});
                    Assert.Equal(HttpStatusCode.OK,login.StatusCode);
                    await using (var db=new BackOfficeDbContext(options))
                    {
                        var user=await db.Set<StaffUser>().SingleAsync(x => x.Email=="underwriter@cover.example");
                        if (rejection=="ticket-subject")
                        {
                            var source=await db.Set<StaffUser>().Where(x=>x.Email=="servicing@cover.example").Select(x=>x.Id).SingleAsync();
                            var copied=await db.Set<UserSession>().Where(x=>x.UserId==source).OrderByDescending(x=>x.CreatedAt).Select(x=>x.TicketCiphertext).FirstAsync();
                            var session=await db.Set<UserSession>().Where(x=>x.UserId==user.Id).OrderByDescending(x=>x.CreatedAt).FirstAsync();
                            session.TicketCiphertext=copied;
                        }
                        else if (rejection=="expired")
                        {
                            var session=await db.Set<UserSession>().Where(x => x.UserId==user.Id).OrderByDescending(x => x.CreatedAt).FirstAsync();
                            session.CreatedAt=DateTimeOffset.UtcNow.AddHours(-10); session.ExpiresAt=DateTimeOffset.UtcNow.AddHours(-1);
                        }
                        else if (rejection=="stamp") user.SecurityStamp=Guid.NewGuid().ToString("N");
                        else user.State="suspended";
                        await db.SaveChangesAsync();
                    }
                    using var denied=await client.GetAsync("/api/v1/account");
                    Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
                }
            }
            if (OperatingSystem.IsWindows())
            {
                using var production=Factory(connection.ConnectionString,keys,"Production");
                using var client=Client(production,null,true);
                using var login=await Post(client,await Csrf(client),new {email="system-admin@cover.example",password});
                Assert.Equal(HttpStatusCode.OK,login.StatusCode);
                var header=login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-cover-session=",StringComparison.Ordinal));
                Assert.Contains("secure",header,StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("domain=",header,StringComparison.OrdinalIgnoreCase);
                using var allowed=await client.GetAsync("/test/admin"); Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
                using var plain=Client(production);
                using var insecure=await plain.GetAsync("/api/v1/auth/csrf"); Assert.Equal(HttpStatusCode.BadRequest,insecure.StatusCode);
            }
            await using (var audit=new BackOfficeDbContext(options))
            {
                var events=await audit.Set<AuditEvent>().ToListAsync();
                Assert.Contains(events,x => x.EventType=="authentication.succeeded");
                Assert.Contains(events,x => x.EventType=="authentication.failed");
                Assert.Contains(events,x => x.EventType=="authentication.session-revoked");
                Assert.All(events,x => Assert.DoesNotContain(password,JsonSerializer.Serialize(x)));
                Assert.All(await audit.Set<UserSession>().ToListAsync(),x => {Assert.Equal(32,x.TokenHash.Length);Assert.NotEmpty(x.TicketCiphertext);});
            }
        }
        finally
        {
            if (connection.InitialCatalog!=ownedName) throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static WebApplicationFactory<Program> Factory(string connection,string keys,string environment="Development") => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseEnvironment(environment).UseSetting("Cover:SqlConnection",connection)
            .UseSetting("Cover:DataProtectionPath",keys).ConfigureServices(services => services.AddSingleton<IStartupFilter,PermissionProbe>()));
    private static HttpClient Client(WebApplicationFactory<Program> factory,string? cookie=null,bool https=false)
    {
        var client=factory.CreateClient(new WebApplicationFactoryClientOptions {BaseAddress=new Uri(https ? "https://localhost" : "http://localhost"),AllowAutoRedirect=false});
        if (cookie is not null) client.DefaultRequestHeaders.Add("Cookie",cookie);
        return client;
    }
    private static async Task<string> Csrf(HttpClient client)
    {
        using var response=await client.GetAsync("/api/v1/auth/csrf"); response.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.GetProperty("requestToken").GetString()!;
    }
    private static Task<HttpResponseMessage> Post(HttpClient client,string csrf,object? body,string path="/api/v1/auth/login")
    {
        var request=new HttpRequestMessage(HttpMethod.Post,path) {Content=body is null ? null : JsonContent.Create(body)};
        request.Headers.Add("X-CSRF-Token",csrf); return client.SendAsync(request);
    }
    // Test-only probe of the registered cookie handler and capability policy; no production route is added.
    private sealed class PermissionProbe : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context,proceed) =>
            {
                if (context.Request.Path!="/test/admin") {await proceed(context);return;}
                var authentication=await context.AuthenticateAsync();
                if (!authentication.Succeeded) {await context.ChallengeAsync();return;}
                var authorization=context.RequestServices.GetRequiredService<IAuthorizationService>();
                if (!(await authorization.AuthorizeAsync(authentication.Principal!,null,"platform-admin")).Succeeded) {await context.ForbidAsync();return;}
                await context.Response.WriteAsync("Allowed");
            });
            next(app);
        };
    }
}
