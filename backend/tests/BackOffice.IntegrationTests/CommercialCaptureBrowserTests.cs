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
    private sealed class CommercialCancellationBrowserClock:TimeProvider { public override DateTimeOffset GetUtcNow()=>new(2026,9,18,12,0,0,TimeSpan.Zero); }

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

    [Fact]
    public Task RealSqlCommercialRenewalBrowser() => RunCommercialCaptureBrowser("issue", servicingIssue: true, renewal: true);

    [Fact]
    public Task RealSqlCommercialCancellationBrowser()=>RunCommercialCaptureBrowser("issue",cancellation:true);

    [Fact]
    public Task RealSqlCommercialDemoNormalApiIssueAndRepeat()=>RunCommercialCaptureBrowser("issue",demo:true);

    [Fact]
    public Task RealSqlCommercialDemoNormalApiAdjustmentAndRepeat()=>RunCommercialCaptureBrowser("issue",demo:true,servicingIssue:true);

    [Fact]
    public Task RealSqlCommercialDemoNormalApiRenewalAndRepeat()=>RunCommercialCaptureBrowser("issue",demo:true,servicingIssue:true,renewal:true);

    [Fact]
    public Task RealSqlCommercialDemoNormalApiCancellationAndRepeat()=>RunCommercialCaptureBrowser("issue",demo:true,servicingIssue:true,renewal:true,cancellation:true);

    [Fact]
    public Task RealSqlCommercialDemoNormalApiReferralCapacityAndRepeat()=>RunCommercialCaptureBrowser("issue",demo:true,demoReferrals:true);

    [Fact]
    public Task RealSqlCommercialDemoNormalApiFullLifecycleAndRepeat()=>RunCommercialCaptureBrowser("issue",demo:true,servicingIssue:true,renewal:true,cancellation:true,demoReferrals:true);

    private async Task RunCommercialCaptureBrowser(string stage, bool servicing = false, bool servicingIssue = false, bool renewal = false,bool cancellation=false,bool demo=false,bool demoReferrals=false)
    {
        await WithDatabase(async (db, password) =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "package.json"))) root = root.Parent;
            Assert.NotNull(root);
            var dist=OperationalBrowserBuild.Select(root.FullName,Environment.GetEnvironmentVariable("COVER_NEXT_DIST_DIR")??".next");
            Assert.True(File.Exists(Path.Combine(root.FullName, "apps/backoffice",dist,"BUILD_ID")));
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeCommercialCapture: true, includeUnderwriting: stage is "rating" or "underwriting" or "terms" or "issue");
            if (stage is "rating" or "underwriting" or "terms" or "issue")
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                await CommercialUnderwritingSeed.SeedAsync(db); await QuoteTermsSeed.SeedAsync(db); await CapacitySeed.SeedAsync(db);
                if (stage == "issue") { await BackOffice.Infrastructure.Policies.CommercialExposureSeed.SeedAsync(db); await BackOffice.Infrastructure.Policies.PolicyTemplateSeed.SeedAsync(db); }
                if(servicingIssue) { await BackOffice.Infrastructure.Policies.ServicingRatingSeed.SeedAsync(db); await BackOffice.Infrastructure.Policies.ServicingTermsSeed.SeedAsync(db); }
                if(renewal) { await BackOffice.Infrastructure.Policies.RenewalPreparationSeed.SeedCommercialAsync(db); await BackOffice.Infrastructure.Policies.RenewalLifecycleSeed.SeedAsync(db); }
                if(cancellation)await BackOffice.Infrastructure.Policies.CommercialUnderwritingCancellationSeed.SeedAsync(db);
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
            Guid? cancellationPolicyId=null;
            if(cancellation&&!demo)cancellationPolicyId=await new UnderwritingRuntimeTests().SeedCommercialCancellationBrowserPolicy(db);
            TimeProvider browserClock=cancellation?new CommercialCancellationBrowserClock():new QuoteTime();
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
                .UseEnvironment("Development").UseUrls("http://127.0.0.1:0")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString())
                .UseSetting("Cover:DataProtectionPath", Path.Combine(root.FullName, ".local/commercial-browser-keys", db.Database.GetDbConnection().Database))
                .UseSetting("Cover:CancellationNoticeWorkerEnabled", cancellation?"true":"false")
                .UseSetting("Cover:OperationalCancellationWorkerEnabled", "false")
                .UseSetting("Cover:RenewalLifecycleWorkerEnabled", "false")
                .UseSetting("Cover:ServicingDeliveryWorkerEnabled", servicingIssue ? "true" : "false")
                .ConfigureServices(services => {
                    services.AddSingleton<TimeProvider>(browserClock);
                    services.AddScoped(provider => new QuoteService(provider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(), browserClock));
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
                if(demo)
                {
                    var senior=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="senior-underwriter@cover.example");
                    var admin=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example");
                    var authority=await db.Set<AuthorityVersion>().SingleAsync(x=>x.ProductVersionId==cc.ProductVersion);
                    db.Add(new UserAuthorityGrant{UserId=senior.Id,AuthorityVersionId=authority.Id,GrantedBy=admin.Id,CreatedBy=admin.Id,
                        EffectiveFrom=authority.EffectiveFrom,EffectiveTo=authority.EffectiveTo,CreatedAt=browserClock.GetUtcNow(),Reason="Explicit isolated normal-API commercial demo grant"});
                    await db.SaveChangesAsync();
                    var actor=new BackOffice.Application.ActorContext(senior.Id,senior.TeamId,null,new HashSet<string>{"senior-underwriter"});
                    var seed=new BackOffice.Infrastructure.Policies.CommercialDemoSeed(host.Services.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),browserClock);
                    var quotes=await seed.SeedAsync(actor,cc.Relationship,cc.ProductVersion);
                    var quoteFile=Path.Combine(output,"demo-quotes.json");await File.WriteAllTextAsync(quoteFile,JsonSerializer.Serialize(quotes));
                    var environment=new Dictionary<string,string>{["COVER_WEB_ORIGIN"]=$"http://127.0.0.1:{port}",["COVER_COMMERCIAL_DEMO_API_ORIGIN"]=api,
                        ["COVER_COMMERCIAL_DEMO_DIRECTORY"]=output,["COVER_COMMERCIAL_DEMO_PASSWORD"]=password,["COVER_COMMERCIAL_DEMO_KNOWN_AT"]=browserClock.GetUtcNow().ToString("O")};
                    async Task RunDemo(int number)
                    {
                        using var process=StartNode(["scripts/seed-commercial-lifecycle-demo.mjs",quoteFile,demoReferrals?(cancellation?"full":"scenarios"):cancellation?"cancellation":renewal?"renewal":servicingIssue?"adjustment":"issue"],environment);
                        var standard=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
                        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(demoReferrals?20:10));}
                        finally{if(!process.HasExited)process.Kill(entireProcessTree:true);}
                        var log=(await standard+await errors).Replace(password,"[redacted]",StringComparison.Ordinal)
                            .Replace(Uri.EscapeDataString(password),"[redacted]",StringComparison.OrdinalIgnoreCase);
                        await File.WriteAllTextAsync(Path.Combine(output,$"demo-{number}.log"),log);
                        Assert.True(process.ExitCode==0,$"Commercial demo failed; inspect sanitized demo-{number}.log in {output}.");
                    }
                    await RunDemo(1);
                    var expectedJournals=cancellation?4:renewal?3:servicingIssue?2:1;
                    Assert.Single(await db.Set<Policy>().ToArrayAsync());Assert.Equal(expectedJournals,await db.Set<Journal>().CountAsync());
                    var payloads=await db.Set<PolicyDocumentRequest>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>x.PayloadJson).ToArrayAsync();Assert.Equal((cancellation?3:expectedJournals)*3,payloads.Length);
                    var workCount=await db.Set<OutboxWork>().CountAsync();var demoRevisionCount=await db.Set<QuoteRevision>().CountAsync();
                    await RunDemo(2);Assert.Equal(quotes,await seed.SeedAsync(actor,cc.Relationship,cc.ProductVersion));
                    Assert.Equal(workCount,await db.Set<OutboxWork>().CountAsync());Assert.Equal(demoRevisionCount,await db.Set<QuoteRevision>().CountAsync());
                    Assert.Equal(payloads,await db.Set<PolicyDocumentRequest>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>x.PayloadJson).ToArrayAsync());
                    Assert.Equal(expectedJournals,await db.Set<Journal>().CountAsync());return;
                }
                var fixture = JsonSerializer.Serialize(new { stage, servicing, servicingIssue, renewal, cancellation,cancellationPolicyId, apiOrigin = api, webOrigin = $"http://127.0.0.1:{port}", output, clockNow = browserClock.GetUtcNow(), ccRelationship = cc.Relationship, mtRelationship = mt.Relationship });
                browser = StartNode([cancellation?"scripts/verify-commercial-cancellation-browser.mjs":"scripts/verify-commercial-capture-browser.mjs", "--worker", "--stage", stage], new() {
                    ["COVER_COMMERCIAL_BROWSER_FIXTURE"] = fixture, ["COVER_COMMERCIAL_BROWSER_PASSWORD"] = password });
                var stdout = browser.StandardOutput.ReadToEndAsync(); var stderr = browser.StandardError.ReadToEndAsync();
                await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(cancellation?15:renewal ? 40 : servicingIssue ? 25 : servicing ? 12 : 8));
                var text = await stdout + await stderr; await File.WriteAllTextAsync(Path.Combine(output, "browser.log"), text);
                Assert.True(browser.ExitCode == 0, text);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "report.json")));
                if(cancellation)
                {
                    Assert.Equal(cancellationPolicyId,report.RootElement.GetProperty("policyId").GetGuid());
                    var cancellationDraft=await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x=>x.PolicyId==cancellationPolicyId);
                    Assert.Equal("cancellation",cancellationDraft.Kind);Assert.Equal("issued",cancellationDraft.State);
                    Assert.Equal(2,await db.Set<CommercialExposureVersion>().CountAsync(x=>x.PolicyId==cancellationPolicyId));
                    Assert.Equal(2,await db.Set<Journal>().CountAsync());
                    Assert.False(await db.Set<CancellationConsequence>().AnyAsync(x=>x.Kind=="mid-removal"));
                    Assert.True(await db.Set<CancellationNoticeReceipt>().AnyAsync());
                    return;
                }
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
                if(renewal)
                {
                    using var result=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"commercial-servicing-issue.json")));
                    var draftId=result.RootElement.GetProperty("draftId").GetGuid();
                    var draft=await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x=>x.Id==draftId);
                    Assert.Equal("renewal",draft.Kind);Assert.Equal("issued",draft.State);
                    Assert.Equal(2,await db.Set<PolicyTerm>().CountAsync());Assert.Equal(2,await db.Set<CommercialExposureVersion>().CountAsync());
                    Assert.Equal(2,await db.Set<Journal>().CountAsync());Assert.False(await db.Set<PolicyMidIntent>().AnyAsync());
                    var experience=Assert.Single(await db.Set<RenewalExperienceVersion>().AsNoTracking().ToArrayAsync());
                    Assert.NotNull(experience.CommercialSubjectsJson);Assert.Equal(draft.CurrentRevisionId,experience.CommercialRevisionId);
                }
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
