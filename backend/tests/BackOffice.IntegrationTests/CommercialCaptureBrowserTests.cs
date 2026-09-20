using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public Task RealSqlCommercialCaptureBusinessLossBrowser() => RunCommercialCaptureBrowser("business-loss");

    [Fact]
    public Task RealSqlCommercialCaptureFullBrowser() => RunCommercialCaptureBrowser("full");

    [Fact]
    public Task RealSqlCommercialRatingBrowser() => RunCommercialCaptureBrowser("rating");

    [Fact]
    public Task RealSqlCommercialReferralBrowser() => RunCommercialCaptureBrowser("underwriting");

    [Fact]
    public Task RealSqlCommercialTermsBrowser() => RunCommercialCaptureBrowser("terms");

    [Fact]
    public Task RealSqlCommercialIssueBrowser() => RunCommercialCaptureBrowser("issue");

    [Fact]
    public Task RealSqlCommercialPolicyReadBrowser() => RunCommercialCaptureBrowser("issue");

    [Fact]
    public Task RealSqlCommercialServicingDraftBrowser() => RunCommercialCaptureBrowser("issue", servicing: true);

    [Fact]
    public Task RealSqlCommercialServicingIssueBrowser() => RunCommercialCaptureBrowser("issue", servicing: true, servicingIssue: true);

    private async Task RunCommercialCaptureBrowser(string stage, bool servicing = false, bool servicingIssue = false)
    {
        await WithDatabase(async (db, password) =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "package.json"))) root = root.Parent;
            Assert.NotNull(root);
            Assert.True(File.Exists(Path.Combine(root.FullName, "apps/backoffice/.next/BUILD_ID")));
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeCommercialCapture: true, includeUnderwriting: stage is "rating" or "underwriting" or "terms" or "issue");
            if (stage is "rating" or "underwriting" or "terms" or "issue")
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                await CommercialUnderwritingSeed.SeedAsync(db); await QuoteTermsSeed.SeedAsync(db); await CapacitySeed.SeedAsync(db);
                if (stage == "issue") { await BackOffice.Infrastructure.Policies.CommercialExposureSeed.SeedAsync(db); await BackOffice.Infrastructure.Policies.PolicyTemplateSeed.SeedAsync(db); }
                if(servicingIssue) { await BackOffice.Infrastructure.Policies.ServicingRatingSeed.SeedAsync(db); await BackOffice.Infrastructure.Policies.ServicingTermsSeed.SeedAsync(db); }
                await transaction.CommitAsync();
            }
            var cc = await CreateFixture(db, "-CC-BROWSER", CommercialCaptureRules.ProductCode, stage is "rating" or "underwriting" or "terms" or "issue" ? 3 : 2, fullTerms: stage is "rating" or "underwriting" or "terms" or "issue");
            if (stage is "underwriting" or "terms" or "issue")
            {
                var reviewer = await db.Set<StaffUser>().SingleAsync(x => x.Email == "underwriter@cover.example");
                var admin = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example");
                var authority = await db.Set<AuthorityVersion>().SingleAsync(x => x.ProductVersionId == cc.ProductVersion);
                db.Add(new UserAuthorityGrant { UserId = reviewer.Id, AuthorityVersionId = authority.Id, GrantedBy = admin.Id, CreatedBy = admin.Id,
                    CreatedAt = new QuoteTime().GetUtcNow(), EffectiveFrom = authority.EffectiveFrom, EffectiveTo = authority.EffectiveTo, Reason = "Explicit browser-test Commercial Combined grant" });
                await db.SaveChangesAsync();
            }
            if (stage is "terms" or "issue")
            {
                var person = new Person { FullName = "Fictional CC browser customer" }; db.Add(person); await db.SaveChangesAsync();
                db.Add(new Contact { ClientId = cc.Client, RelationshipId = cc.Relationship, PersonId = person.Id, DeclaredFullName = person.FullName,
                    NormalizedName = person.FullName.ToUpperInvariant(), Role = "director", Email = "cc-browser@example.invalid",
                    MarketingConsent = "{\"state\":\"not-asked\",\"email\":false,\"telephone\":false,\"source\":\"Fictional browser\",\"recordedAt\":\"2026-09-16T12:00:00Z\"}" });
                await db.SaveChangesAsync();
            }
            var mt = await CreateFixture(db, "-MT-BROWSER");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={cc.Agency} OR Id={mt.Agency}");
            var fixtureCreatedAt = new QuoteTime().GetUtcNow().AddDays(-1);
            var address = """{"line1":"1 Fictional Street","town":"London","postcode":"SW1A 1AA","country":"GB"}""";
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAccount SET CreatedAt={fixtureCreatedAt},Address={address} WHERE Id={cc.Client} OR Id={mt.Client}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAccount SET LegalName=N'Fictional quote client CC browser',NormalizedName=N'FICTIONAL QUOTE CLIENT CC BROWSER' WHERE Id={cc.Client}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAccount SET LegalName=N'Fictional quote client MT browser',NormalizedName=N'FICTIONAL QUOTE CLIENT MT BROWSER' WHERE Id={mt.Client}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAgencyRelationship SET CreatedAt={fixtureCreatedAt} WHERE Id={cc.Relationship} OR Id={mt.Relationship}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET CreatedAt={fixtureCreatedAt} WHERE Id={cc.Agency} OR Id={mt.Agency}");
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
                .UseEnvironment("Development").UseUrls("http://127.0.0.1:0")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString())
                .UseSetting("Cover:DataProtectionPath", Path.Combine(root.FullName, ".local/commercial-browser-keys", db.Database.GetDbConnection().Database))
                .UseSetting("Cover:CancellationNoticeWorkerEnabled", "false")
                .UseSetting("Cover:RenewalLifecycleWorkerEnabled", "false")
                .UseSetting("Cover:ServicingDeliveryWorkerEnabled", servicingIssue ? "true" : "false")
                .ConfigureServices(services => {
                    services.AddSingleton<TimeProvider>(new QuoteTime());
                    services.AddScoped(provider => new QuoteService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), new QuoteTime()));
                }));
            host.UseKestrel(0); using var client = host.CreateClient();
            await using (var configured = await host.Services.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>().CreateDbContextAsync())
            {
                Assert.Equal(db.Database.GetDbConnection().Database, configured.Database.GetDbConnection().Database);
                var storedClient = await configured.Set<ClientAccount>().AsNoTracking().SingleAsync(x => x.Id == cc.Client);
                Assert.Equal("CL-QUOTE-STORAGE-CC-BROWSER", storedClient.Reference);
                Assert.True(storedClient.CreatedAt <= host.Services.GetRequiredService<TimeProvider>().GetUtcNow());
            }
            var api = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            Assert.True(new Uri(api).IsLoopback); Assert.NotEqual(5000, new Uri(api).Port);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var output = Path.Combine(root.FullName, ".local/browser-evidence/commercial-capture", db.Database.GetDbConnection().Database);
            Directory.CreateDirectory(output);
            Process StartNode(string[] args, Dictionary<string, string> environment)
            {
                var info = new ProcessStartInfo("node") { WorkingDirectory = root.FullName, UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in args) info.ArgumentList.Add(arg);
                foreach (var (key, value) in environment) info.Environment[key] = value;
                return Process.Start(info) ?? throw new InvalidOperationException("Commercial browser child could not start.");
            }
            using var web = StartNode(["apps/backoffice/node_modules/next/dist/bin/next", "start", "apps/backoffice", "--hostname", "127.0.0.1", "--port", port.ToString()], new() { ["BACKOFFICE_API_ORIGIN"] = api });
            var webOut = web.StandardOutput.ReadToEndAsync(); var webErr = web.StandardError.ReadToEndAsync(); Process? browser = null;
            try
            {
                using var probe = new HttpClient(); var serving = false;
                for (var attempt = 0; attempt < 100 && !web.HasExited; attempt++)
                {
                    try { using var response = await probe.GetAsync($"http://127.0.0.1:{port}/login"); if (response.IsSuccessStatusCode) { serving = true; break; } }
                    catch (HttpRequestException) { }
                    await Task.Delay(250);
                }
                Assert.True(serving, "Isolated commercial preview did not start.");
                var fixture = JsonSerializer.Serialize(new { stage, servicing, servicingIssue, apiOrigin = api, webOrigin = $"http://127.0.0.1:{port}", output, clockNow = new QuoteTime().GetUtcNow(), ccRelationship = cc.Relationship, mtRelationship = mt.Relationship });
                browser = StartNode(["scripts/verify-commercial-capture-browser.mjs", "--worker", "--stage", stage], new() {
                    ["COVER_COMMERCIAL_BROWSER_FIXTURE"] = fixture, ["COVER_COMMERCIAL_BROWSER_PASSWORD"] = password });
                var stdout = browser.StandardOutput.ReadToEndAsync(); var stderr = browser.StandardError.ReadToEndAsync();
                await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(servicingIssue ? 25 : servicing ? 12 : 8));
                var text = await stdout + await stderr; await File.WriteAllTextAsync(Path.Combine(output, "browser.log"), text);
                Assert.True(browser.ExitCode == 0, text);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "report.json")));
                var quoteId = report.RootElement.GetProperty("quoteId").GetGuid();
                var revisions = await db.Set<QuoteRevision>().AsNoTracking().Where(x => x.QuoteId == quoteId).OrderBy(x => x.Number).ToArrayAsync();
                Assert.True(revisions.Length >= 7);
                using var final = JsonDocument.Parse(revisions.Last().ProposalJson);
                Assert.Equal("Concurrent saved business", final.RootElement.GetProperty("insured").GetProperty("legalName").GetString());
                Assert.Empty(final.RootElement.GetProperty("risk").GetProperty("losses").EnumerateArray());
                var declarations = final.RootElement.GetProperty("risk").GetProperty("declarations").GetProperty("answers").EnumerateArray();
                Assert.False(declarations.Single(x => x.GetProperty("questionId").GetString() == "prototype.quote.385743089b72").GetProperty("value").GetBoolean());
                foreach (var question in report.RootElement.GetProperty("verifiedQuestionIds").EnumerateArray())
                    Assert.Contains(revisions, revision => revision.ProposalJson.Contains(question.GetString()!, StringComparison.Ordinal));
                if (stage is "full" or "rating" or "underwriting" or "terms" or "issue")
                {
                    Assert.Equal(109, report.RootElement.GetProperty("verifiedQuestionIds").GetArrayLength());
                    Assert.Equal(2, final.RootElement.GetProperty("risk").GetProperty("locations").GetArrayLength());
                    Assert.Equal(2, final.RootElement.GetProperty("risk").GetProperty("wages").GetArrayLength());
                    Assert.Equal("estimated-gross-profit", final.RootElement.GetProperty("risk").GetProperty("businessInterruption").GetProperty("basis").GetString());
                }
                Assert.Equal(2, await db.Set<Quote>().CountAsync());
                if (servicing)
                {
                    using var evidence = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"commercial-servicing.json")));
                    var draftId = evidence.RootElement.GetProperty("draftId").GetGuid();
                    var saved = await db.Set<ServicingRevision>().AsNoTracking().Where(x=>x.DraftId==draftId).OrderByDescending(x=>x.Sequence).FirstAsync();
                    Assert.True(JsonNode.DeepEquals(JsonNode.Parse(saved.ProposalJson), JsonNode.Parse(evidence.RootElement.GetProperty("proposal").GetRawText())));
                    var issued = await db.Set<PolicyVersion>().AsNoTracking().OrderBy(x=>x.Sequence).FirstAsync();
                    Assert.Equal(evidence.RootElement.GetProperty("issuedHash").GetString(),Convert.ToHexString(issued.ContentHash).ToLowerInvariant());
                    if(servicingIssue)
                    {
                        using var result=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"commercial-servicing-issue.json")));
                        var transactionId=result.RootElement.GetProperty("receipt").GetProperty("transactionId").GetGuid();
                        var versions=await db.Set<PolicyVersion>().AsNoTracking().Where(x=>x.TransactionId==transactionId).ToArrayAsync();
                        Assert.NotEmpty(versions);Assert.All(versions,x=>Assert.Equal("issued-commercial-servicing-1",JsonDocument.Parse(x.SnapshotJson).RootElement.GetProperty("snapshotFormat").GetString()));
                        Assert.Equal(1+versions.Length,await db.Set<CommercialExposureVersion>().CountAsync());
                        Assert.Equal(1+versions.Length,await db.Set<CommercialExposureIssueDecision>().CountAsync());
                        Assert.Equal("issued",await db.Set<ServicingDraft>().Where(x=>x.Id==draftId).Select(x=>x.State).SingleAsync());
                        Assert.Equal(2,await db.Set<Journal>().CountAsync());Assert.False(await db.Set<PolicyMidIntent>().AnyAsync());
                    }
                    else
                    {
                        Assert.Equal(1,await db.Set<CommercialExposureVersion>().CountAsync());
                        Assert.Equal(1,await db.Set<CommercialExposureIssueDecision>().CountAsync());
                        Assert.False(await db.Set<ServicingCycle>().AnyAsync());
                    }
                }
            }
            finally
            {
                if (browser is not null) { if (!browser.HasExited) browser.Kill(entireProcessTree: true); browser.Dispose(); }
                if (!web.HasExited) web.Kill(entireProcessTree: true);
                await web.WaitForExitAsync(); await File.WriteAllTextAsync(Path.Combine(output, "web.log"), await webOut + await webErr);
            }
        });
    }
}
