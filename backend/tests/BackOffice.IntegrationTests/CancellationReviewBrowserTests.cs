using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
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
    public Task RealSqlCancellationReviewBrowser(string product)=>RunCancellationBrowser(product,false);

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public Task RealSqlCancellationIssueBrowser(string product)=>RunCancellationBrowser(product,true);

    private async Task RunCancellationBrowser(string product,bool issue)
    {
        await WithDatabase(async(db,password)=>
        {
            var root=new DirectoryInfo(AppContext.BaseDirectory);
            while(root is not null && !File.Exists(Path.Combine(root.FullName,"package.json")))root=root.Parent;
            Assert.NotNull(root);var dist=OperationalBrowserBuild.Select(root.FullName,Environment.GetEnvironmentVariable("COVER_NEXT_DIST_DIR")??".next");Assert.True(File.Exists(Path.Combine(root.FullName,"apps/backoffice",dist,"BUILD_ID")));
            var setup=await AcceptedIssue(db,password,product);var f=setup.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            using var host=ServicingRatingApiHost(db,f.Clock,false).WithWebHostBuilder(builder=>builder
                .UseSetting("Cover:CancellationNoticeWorkerEnabled",issue?"true":"false").UseSetting("Cover:RenewalLifecycleWorkerEnabled","false").UseSetting("Cover:ServicingDeliveryWorkerEnabled","false")
                .ConfigureServices(services=>services.AddSingleton<TimeProvider>(f.Clock)));
            host.UseKestrel(0);using var client=host.CreateClient();
            var api=host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();Assert.True(new Uri(api).IsLoopback);
            Assert.NotEqual(5000,new Uri(api).Port);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            var webOrigin=$"http://127.0.0.1:{port}";var output=Path.Combine(root.FullName,issue?".local/browser-evidence/cancellation-issue":".local/browser-evidence/cancellation-review",db.Database.GetDbConnection().Database);
            Directory.CreateDirectory(output);
            Process StartNode(IEnumerable<string> args,Dictionary<string,string> environment)
            {
                var info=new ProcessStartInfo("node"){WorkingDirectory=root.FullName,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var arg in args)info.ArgumentList.Add(arg);foreach(var (key,value) in environment)info.Environment[key]=value;
                return Process.Start(info)??throw new InvalidOperationException("Cancellation browser child could not start.");
            }
            using var web=StartNode(["apps/backoffice/node_modules/next/dist/bin/next","start","apps/backoffice","--hostname","127.0.0.1","--port",port.ToString()],new(){["BACKOFFICE_API_ORIGIN"]=api,["COVER_NEXT_DIST_DIR"]=dist});
            var webOut=web.StandardOutput.ReadToEndAsync();var webErr=web.StandardError.ReadToEndAsync();Process? browser=null;
            try
            {
                using var probe=new HttpClient();var serving=false;
                for(var attempt=0;attempt<60 && !web.HasExited;attempt++)
                {try{using var response=await probe.GetAsync(webOrigin+"/login");if(response.IsSuccessStatusCode){serving=true;break;}}catch(HttpRequestException){}await Task.Delay(250);}
                Assert.True(serving,"Isolated web preview did not start.");
                var fixture=JsonSerializer.Serialize(new{issue,apiOrigin=api,webOrigin,policyId=term.PolicyId,termId=term.Id,product,output,originalVersionId=basis.Id,clockNow=f.Clock.GetUtcNow()});
                browser=StartNode(["scripts/verify-cancellationreview-browser.mjs","--worker"],new(){["COVER_CANCELLATION_BROWSER_FIXTURE"]=fixture,["COVER_CANCELLATION_BROWSER_PASSWORD"]=password});
                var browserOut=browser.StandardOutput.ReadToEndAsync();var browserErr=browser.StandardError.ReadToEndAsync();
                await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5));
                var browserText=await browserOut;var browserError=await browserErr;await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),browserText+browserError);
                Assert.True(browser.ExitCode==0,browserError);
                var approval=await db.Set<CancellationApproval>().AsNoTracking().SingleAsync();var draft=await db.Set<ServicingDraft>().AsNoTracking().SingleAsync();
                Assert.NotEqual(draft.CreatedBy,approval.CreatedBy);Assert.Equal(issue?"issued":"abandoned",draft.State);
                Assert.Equal(1,await db.Set<CancellationPreview>().CountAsync());Assert.Equal(1,await db.Set<PolicyTerm>().CountAsync());
                Assert.Equal(issue?2:1,await db.Set<PolicyTransaction>().CountAsync());Assert.Equal(issue?2:1,await db.Set<Journal>().CountAsync());Assert.Empty(await db.Set<ServicingCycle>().ToArrayAsync());
                Assert.Equal(basis.ContentHash,(await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==basis.Id)).ContentHash);
            }
            finally
            {
                if(browser is not null){if(!browser.HasExited)browser.Kill(entireProcessTree:true);browser.Dispose();}
                if(!web.HasExited)web.Kill(entireProcessTree:true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(output,"web.log"),await webOut+await webErr);
            }
        });
    }
}
