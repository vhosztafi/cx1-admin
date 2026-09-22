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
public sealed partial class UnderwritingRuntimeTests
{
    private static async Task DriverTaskBrowser(WebApplicationFactory<Program> host,BackOfficeDbContext db,string email,string password,Guid policyId,Guid driverId,Guid taskId,Guid draftId)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"package.json")))root=root.Parent;Assert.NotNull(root);
        var dist=OperationalBrowserBuild.Select(root.FullName,".local/next-phase9-acceptance");
        var output=Path.Combine(root.FullName,".local/browser-evidence/driver-tasks",db.Database.GetDbConnection().Database);Directory.CreateDirectory(output);
        var api=host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();Assert.True(new Uri(api).IsLoopback);Assert.NotEqual(5000,new Uri(api).Port);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var webOrigin=$"http://127.0.0.1:{port}";
        Process Start(string[] arguments,Dictionary<string,string> environment)
        {
            var info=new ProcessStartInfo("node"){WorkingDirectory=root.FullName,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in arguments)info.ArgumentList.Add(arg);foreach(var pair in environment)info.Environment[pair.Key]=pair.Value;
            return Process.Start(info)??throw new InvalidOperationException("Driver task browser did not start.");
        }
        using var web=Start(["apps/backoffice/node_modules/next/dist/bin/next","start","apps/backoffice","--hostname","127.0.0.1","--port",port.ToString()],new(){["COVER_NEXT_DIST_DIR"]=dist,["BACKOFFICE_API_ORIGIN"]=api});
        var webOut=web.StandardOutput.ReadToEndAsync();var webErr=web.StandardError.ReadToEndAsync();Process? browser=null;
        try{
            using var probe=new HttpClient();var ready=false;
            for(var n=0;n<80&&!web.HasExited;n++){try{using var response=await probe.GetAsync(webOrigin+"/login");if(response.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(250);}
            Assert.True(ready,"Driver task web host did not become ready.");
            browser=Start(["scripts/verify-driver-task-browser.mjs","--worker"],new(){["COVER_DRIVER_TASK_PASSWORD"]=password,["COVER_DRIVER_TASK_FIXTURE"]=JsonSerializer.Serialize(new{apiOrigin=api,webOrigin,output,email,policyId,driverId,taskId,draftId})});
            var stdout=browser.StandardOutput.ReadToEndAsync();var stderr=browser.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));await browser.WaitForExitAsync(timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),await stdout+await stderr);Assert.True(browser.ExitCode==0,"Driver task browser failed; inspect local evidence.");
            Assert.Equal(driverId,await(from binding in db.Set<WorkflowTaskBinding>() join referral in db.Set<ServicingReferral>() on binding.ServicingReferralId equals referral.Id where binding.TaskId==taskId select referral.RiskItemId).SingleAsync());
            await File.WriteAllTextAsync(Path.Combine(output,"sql-readback.json"),JsonSerializer.Serialize(new{passed=true,taskId,driverId,policyId}));
            Directory.CreateDirectory(Path.Combine(root.FullName,".local/phase9-16-driver-browser"));await File.WriteAllTextAsync(Path.Combine(root.FullName,".local/phase9-16-driver-browser/current.json"),JsonSerializer.Serialize(new{output,passed=true}));
        }finally{if(browser is not null){if(!browser.HasExited)browser.Kill(true);browser.Dispose();}if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(output,"web.log"),await webOut+await webErr);}
    }
}
