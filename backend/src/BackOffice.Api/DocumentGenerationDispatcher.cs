using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;

namespace BackOffice.Api;

public sealed class DocumentGenerationDispatcher(IServiceScopeFactory scopes,TimeProvider time,ILogger<DocumentGenerationDispatcher> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var requestOffset=0;var cancellationOffset=0;var workOffset=0;
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();var service=scope.ServiceProvider.GetRequiredService<DocumentService>();
                var requests=await service.UnregisteredRequests(requestOffset,stoppingToken);requestOffset=requests.Count==0?0:requestOffset+requests.Count;
                foreach(var request in requests)
                {
                    try{await service.RegisterOriginalRequest(request,stoppingToken);}
                    catch(OperationalAccessException){logger.LogWarning("Document request {RequestId} requires current original authority.",request);}
                }
                var notices=await service.UnregisteredCancellationNotices(cancellationOffset,stoppingToken);cancellationOffset=notices.Count==0?0:cancellationOffset+notices.Count;
                foreach(var notice in notices)
                {
                    try{await service.RegisterOriginalCancellationNotice(notice,stoppingToken);}
                    catch(OperationalAccessException){logger.LogWarning("Cancellation document {NoticeId} requires current original authority.",notice);}
                    catch(DocumentRenderException){logger.LogWarning("Cancellation document {NoticeId} failed its retained provenance check.",notice);}
                }
                var work=await service.PendingGenerations(workOffset,stoppingToken);workOffset=work.Count==0?0:workOffset+work.Count;
                foreach(var id in work)
                {
                    try{await scope.ServiceProvider.GetRequiredService<DocumentGenerationWorker>().Process(id,stoppingToken);}
                    catch(OperationalAccessException){logger.LogWarning("Document work {WorkId} requires current original authority.",id);}
                    catch(DocumentRenderException){logger.LogWarning("Document work {WorkId} failed its retained provenance check.",id);}
                }
                await scope.ServiceProvider.GetRequiredService<FileService>().CleanupExpiredTemporary(stoppingToken);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("Document processing was interrupted; retained requests remain recoverable.");}
            try{await Task.Delay(TimeSpan.FromSeconds(10),time,stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
        }
    }
    public static void Register(WebApplicationBuilder builder)
    {
        if (DeploymentBoundary.DemoWorkersEnabled(builder) &&builder.Configuration.GetValue("Cover:DocumentWorkerEnabled",false))builder.Services.AddHostedService<DocumentGenerationDispatcher>();
    }
}
