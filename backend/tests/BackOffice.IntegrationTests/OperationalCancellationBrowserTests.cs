using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlOperationalCancellationBrowserCommercialWithoutEl()
    {
        IDbContextFactory<BackOfficeDbContext>? factory=null;ActorContext? actor=null;RatingClock? clock=null;
        await CommercialTermsScenario(async(db,cycle,acceptance,actorId,now)=>
        {
            var source=await CommercialIssueCommand(db,cycle,acceptance,actorId);factory=source.Factory;actor=source.Actor;
            await source.Service.IssueAsync(source.Actor,source.Quote.Id,source.Quote.RowVersion,source.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            await using(var tx=await db.Database.BeginTransactionAsync()){await CommercialUnderwritingCancellationSeed.SeedAsync(db);await tx.CommitAsync();}
            var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();clock=new RatingClock{Current=term.StartsAt.AddDays(1)};
            await VerifyCancellationDocument(db,factory,clock,actor,afterIssue:()=>Task.CompletedTask);
        },stopAfterAccepted:true,configureProposal:proposal=>
        {
            proposal["risk"]!["declarations"]!["answers"]!.AsArray().Single(x=>x!["questionId"]!.GetValue<string>()=="prototype.quote.36ef01068295")!["value"]=false;
            proposal["risk"]!["liability"]!.AsObject().Remove("employersLimit");proposal["risk"]!["liability"]!.AsObject().Remove("employersReferenceNumber");
        },inspectAccepted:(db,password)=>RunCancellationBrowser(db,password,factory!,clock!,actor!));
    }
    [Fact]
    public Task RealSqlOperationalCancellationBrowserMotor()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await VerifyCancellationDocument(db,f.Factory,f.Clock,f.Underwriter,afterIssue:()=>RunCancellationBrowser(db,password,f.Factory,f.Clock,f.Underwriter));
    });
    private static async Task RunCancellationBrowser(BackOfficeDbContext db,string password,IDbContextFactory<BackOfficeDbContext> factory,RatingClock clock,ActorContext actor)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"package.json")))root=root.Parent;Assert.NotNull(root);
        const string dist=".local/next-phase9-15-browser";Assert.True(File.Exists(Path.Combine(root.FullName,"apps/backoffice",dist,"BUILD_ID")),"Build cancellation browser bundle first.");
        var notice=await db.Set<CancellationConsequence>().AsNoTracking().SingleAsync(x=>x.Kind=="notice");
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==notice.VersionId);var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();
        clock.Current=await db.Set<CancellationIssueDecision>().Select(x=>x.EffectiveAt).SingleAsync();
        var commercial=JsonDocument.Parse(version.SnapshotJson).RootElement.GetProperty("productCode").GetString()=="commercial-combined";
        var expectsWithdrawal=await db.Set<CancellationConsequence>().AnyAsync(x=>x.Kind=="certificate-withdrawal");
        await using(var tx=await db.Database.BeginTransactionAsync()){await DocumentTemplateSeed.SeedAsync(db);await OperationalDeliverySeed.Seed(db);await OperationalMidSeed.Seed(db);await tx.CommitAsync();}
        var output=Path.Combine(root.FullName,".local/browser-evidence/cancellation",db.Database.GetDbConnection().Database);Directory.CreateDirectory(output);
        var email=await db.Set<StaffUser>().Where(x=>x.Id==actor.UserId).Select(x=>x.Email).SingleAsync();
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseUrls("http://127.0.0.1:0")
            .UseSetting("Cover:SqlConnection",db.Database.GetConnectionString())
            .UseSetting("Cover:DataProtectionPath",Path.Combine(output,"keys")).UseSetting("Cover:FileStoragePath",Path.Combine(output,"files")).UseSetting("Cover:FileWorkerEnabled","false").UseSetting("Cover:DocumentWorkerEnabled","true")
            .UseSetting("Cover:OperationalDeliveryWorkerEnabled","true").UseSetting("Cover:OperationalCancellationWorkerEnabled","true").UseSetting("Cover:WorkflowTaskWorkerEnabled","false").UseSetting("Cover:DiagnosticWorkerEnabled","false").ConfigureServices(services=>services.AddSingleton<TimeProvider>(clock)));
        host.UseKestrel(0);using var client=host.CreateClient();var api=host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();Assert.True(new Uri(api).IsLoopback);Assert.NotEqual(5000,new Uri(api).Port);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var webOrigin=$"http://127.0.0.1:{port}";
        Process Start(IEnumerable<string> args,Dictionary<string,string> env)
        {
            var info=new ProcessStartInfo("node"){WorkingDirectory=root.FullName,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in args)info.ArgumentList.Add(arg);foreach(var (key,value) in env)info.Environment[key]=value;
            return Process.Start(info)??throw new InvalidOperationException("Cancellation browser did not start.");
        }
        using var web=Start(["apps/backoffice/node_modules/next/dist/bin/next","start","apps/backoffice","--hostname","127.0.0.1","--port",port.ToString()],new(){["BACKOFFICE_API_ORIGIN"]=api,["COVER_NEXT_DIST_DIR"]=dist});
        var webOut=web.StandardOutput.ReadToEndAsync();var webErr=web.StandardError.ReadToEndAsync();Process? browser=null;
        try
        {
            using var probe=new HttpClient();var serving=false;
            for(var attempt=0;attempt<80&&!web.HasExited;attempt++){try{using var response=await probe.GetAsync(webOrigin+"/login");if(response.IsSuccessStatusCode){serving=true;break;}}catch(HttpRequestException){}await Task.Delay(250);}
            Assert.True(serving,"Isolated cancellation web did not start.");
            var fixture=JsonSerializer.Serialize(new{apiOrigin=api,webOrigin,output,email,policyId=policy.Id,versionId=version.Id,commercial,expectsWithdrawal});
            browser=Start(["scripts/verify-cancellation-browser.mjs","--worker"],new(){["COVER_CANCELLATION_BROWSER_FIXTURE"]=fixture,["COVER_CANCELLATION_BROWSER_PASSWORD"]=password});
            var stdout=browser.StandardOutput.ReadToEndAsync();var stderr=browser.StandardError.ReadToEndAsync();
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(7));await browser.WaitForExitAsync(deadline.Token);
            await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),await stdout+await stderr);Assert.True(browser.ExitCode==0,"Cancellation browser failed; inspect sanitized failure evidence.");
            Assert.Equal(1,await db.Set<CancellationNoticeDispatch>().CountAsync());
            Assert.Equal(1,await db.Set<OperationalDelivery>().CountAsync());
            Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==MessageDeliveryService.WorkKind));
            Assert.Equal(expectsWithdrawal?1:0,await db.Set<CertificateWithdrawal>().CountAsync());
            Assert.Equal(expectsWithdrawal?3:2,await db.Set<CancellationOperationalReceipt>().CountAsync());
            Assert.Empty(await db.Set<CancellationNoticeReceipt>().ToArrayAsync());
            Assert.Equal(version.ContentHash,(await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==version.Id)).ContentHash);
            await File.WriteAllTextAsync(Path.Combine(output,"sql-readback.json"),JsonSerializer.Serialize(new{passed=true,versionId=version.Id,noticeId=notice.Id,expectsWithdrawal,commercial}));
            Directory.CreateDirectory(Path.Combine(root.FullName,".local/phase9-15-browser"));
            await File.WriteAllTextAsync(Path.Combine(root.FullName,".local/phase9-15-browser/"+(commercial?"commercial":"motor-trade")+".json"),JsonSerializer.Serialize(new{output,passed=true,verifiedAt=DateTimeOffset.UtcNow}));
        }
        finally
        {
            if(browser is not null){if(!browser.HasExited)browser.Kill(true);browser.Dispose();}
            if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(output,"web.log"),await webOut+await webErr);
        }
    }
}
