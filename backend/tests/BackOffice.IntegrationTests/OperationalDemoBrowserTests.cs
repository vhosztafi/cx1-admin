using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class OperationalDemoTests
{
    internal static async Task RunMatchBrowser(WebApplicationFactory<Program> host,BackOfficeDbContext db,string password,Guid matchId,Guid messageId,
        string scenario="matching",Guid? policyId=null,Guid? termId=null)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"package.json")))root=root.Parent;Assert.NotNull(root);
        const string dist=".local/next-phase9-16-browser";Assert.True(File.Exists(Path.Combine(root.FullName,"apps/backoffice",dist,"BUILD_ID")),"Build the operational demo browser bundle first.");
        var output=Path.Combine(root.FullName,".local/browser-evidence/operational-demo",db.Database.GetDbConnection().Database);Directory.CreateDirectory(output);
        var api=host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();Assert.True(new Uri(api).IsLoopback);Assert.NotEqual(5000,new Uri(api).Port);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var webOrigin=$"http://127.0.0.1:{port}";
        Process Start(IEnumerable<string> args,Dictionary<string,string> env)
        {
            var info=new ProcessStartInfo("node"){WorkingDirectory=root.FullName,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in args)info.ArgumentList.Add(arg);foreach(var (key,value) in env)info.Environment[key]=value;
            return Process.Start(info)??throw new InvalidOperationException("Operational demo browser did not start.");
        }
        using var web=Start(["apps/backoffice/node_modules/next/dist/bin/next","start","apps/backoffice","--hostname","127.0.0.1","--port",port.ToString()],new(){["BACKOFFICE_API_ORIGIN"]=api,["COVER_NEXT_DIST_DIR"]=dist});
        var webOut=web.StandardOutput.ReadToEndAsync();var webErr=web.StandardError.ReadToEndAsync();Process? browser=null;
        try
        {
            using var probe=new HttpClient();var serving=false;
            for(var attempt=0;attempt<80&&!web.HasExited;attempt++){try{using var response=await probe.GetAsync(webOrigin+"/login");if(response.IsSuccessStatusCode){serving=true;break;}}catch(HttpRequestException){}await Task.Delay(250);}
            Assert.True(serving,"Isolated operational demo web did not start.");
            var fixture=JsonSerializer.Serialize(new{apiOrigin=api,webOrigin,output,matchId,messageId,scenario,policyId,termId});
            var script=scenario=="matching"?"scripts/verify-operational-match-browser.mjs":"scripts/verify-operational-lapse-browser.mjs";
            browser=Start([script,"--worker"],new(){["COVER_OPERATIONAL_DEMO_FIXTURE"]=fixture,["COVER_OPERATIONAL_DEMO_PASSWORD"]=password});
            var stdout=browser.StandardOutput.ReadToEndAsync();var stderr=browser.StandardError.ReadToEndAsync();
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(4));await browser.WaitForExitAsync(deadline.Token);
            await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),await stdout+await stderr);Assert.True(browser.ExitCode==0,"Operational demo browser failed; inspect sanitized evidence.");
            Assert.Equal(scenario=="matching"?"Staff revised this retained request; preserve this edit.":"Staff retained and annotated the historical lapse notice.",await db.Set<OperationalMessageDraft>().Where(x=>x.Id==messageId).Select(x=>x.Body).SingleAsync());
            if(scenario=="matching")Assert.Equal("recorded",await db.Set<MatchInformationRequest>().Where(x=>x.MatchId==matchId).Select(x=>x.DeliveryState).SingleAsync());
            else Assert.Equal(messageId,await db.Set<RenewalLapseCorrespondence>().Select(x=>x.MessageId).SingleAsync());
            Assert.Empty(await db.Set<OperationalDelivery>().ToListAsync());
            await File.WriteAllTextAsync(Path.Combine(output,"sql-readback.json"),JsonSerializer.Serialize(new{matchId,messageId,passed=true,verifiedAt=DateTimeOffset.UtcNow}));
            Directory.CreateDirectory(Path.Combine(root.FullName,".local/phase9-16-browser"));await File.WriteAllTextAsync(Path.Combine(root.FullName,$".local/phase9-16-browser/{scenario}.json"),JsonSerializer.Serialize(new{output,passed=true}));
        }
        finally
        {
            if(browser is not null){if(!browser.HasExited)browser.Kill(true);browser.Dispose();}
            if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(output,"web.log"),await webOut+await webErr);
        }
    }
}
