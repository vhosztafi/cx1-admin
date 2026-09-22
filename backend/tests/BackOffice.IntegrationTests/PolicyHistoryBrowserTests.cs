using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public async Task RealSqlPolicyHistoryBrowser(string product)
    {
        await WithDatabase(async (db, password) =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "package.json"))) root = root.Parent;
            Assert.NotNull(root); var dist = OperationalBrowserBuild.Select(root.FullName, ".local/next-policy-history");
            Assert.True(File.Exists(Path.Combine(root.FullName, "apps/backoffice", dist, "BUILD_ID")), "Build the isolated history preview first.");
            var setup = await AcceptedIssue(db, password, product); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var basis = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            await VerifyCancellationOfAdjustedLedger(db, f, basis.Id, requireAdjusted: false);
            var changed = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.Id != basis.Id);
            using var host = ServicingRatingApiHost(db, f.Clock, false).WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(f.Clock)));
            host.UseKestrel(0); using var client = host.CreateClient();
            var api = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            Assert.True(new Uri(api).IsLoopback);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var webOrigin = $"http://127.0.0.1:{port}";
            var output = Path.Combine(root.FullName, ".local/browser-evidence/policy-history", db.Database.GetDbConnection().Database); Directory.CreateDirectory(output);
            Process StartNode(IEnumerable<string> args, Dictionary<string, string> env)
            {
                var info = new ProcessStartInfo("node") { WorkingDirectory = root.FullName, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in args) info.ArgumentList.Add(arg); foreach (var entry in env) info.Environment[entry.Key] = entry.Value;
                return Process.Start(info) ?? throw new InvalidOperationException("History test process did not start.");
            }
            using var web = StartNode(["apps/backoffice/node_modules/next/dist/bin/next", "start", "apps/backoffice", "--hostname", "127.0.0.1", "--port", port.ToString()], new() { ["BACKOFFICE_API_ORIGIN"] = api, ["COVER_NEXT_DIST_DIR"] = dist });
            var webOut = web.StandardOutput.ReadToEndAsync(); var webErr = web.StandardError.ReadToEndAsync(); Process? browser = null;
            try
            {
                using var probe = new HttpClient(); var serving = false;
                for (var attempt = 0; attempt < 60 && !web.HasExited; attempt++)
                { try { using var response = await probe.GetAsync(webOrigin + "/login"); if (response.IsSuccessStatusCode) { serving = true; break; } } catch (HttpRequestException) { } await Task.Delay(250); }
                Assert.True(serving, "Isolated policy history preview did not start.");
                var fixture = JsonSerializer.Serialize(new { apiOrigin = api, webOrigin, policyId = basis.PolicyId, originalVersionId = basis.Id, changedVersionId = changed.Id, changedEffectiveAt = changed.EffectiveAt, product, output, clockNow = f.Clock.GetUtcNow() });
                browser = StartNode(["scripts/verify-policyhistory-browser.mjs", "--worker"], new() { ["COVER_HISTORY_BROWSER_FIXTURE"] = fixture, ["COVER_HISTORY_BROWSER_PASSWORD"] = password });
                var browserOut = browser.StandardOutput.ReadToEndAsync(); var browserErr = browser.StandardError.ReadToEndAsync();
                await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5));
                var error = System.Text.RegularExpressions.Regex.Replace(await browserErr, @"(?im)^.*cookie:.*$", "    [test session cookie redacted]");
                await File.WriteAllTextAsync(Path.Combine(output, "browser.log"), await browserOut + error);
                Assert.True(browser.ExitCode == 0, error);
                Assert.Single(await db.Set<PolicyQuoteClone>().ToArrayAsync());
                Assert.Equal(2, await db.Set<PolicyReconstructionRequest>().CountAsync());
                Assert.Equal(basis.ContentHash, await db.Set<PolicyVersion>().Where(x => x.Id == basis.Id).Select(x => x.ContentHash).SingleAsync());
                Assert.Equal(2, await db.Set<PolicyVersion>().CountAsync());
            }
            finally
            {
                if (browser is not null) { if (!browser.HasExited) browser.Kill(entireProcessTree: true); browser.Dispose(); }
                if (!web.HasExited) web.Kill(entireProcessTree: true); await web.WaitForExitAsync();
                await File.WriteAllTextAsync(Path.Combine(output, "web.log"), await webOut + await webErr);
            }
        });
    }
}
