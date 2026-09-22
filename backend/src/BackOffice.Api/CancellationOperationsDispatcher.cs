using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public sealed class CancellationOperationsDispatcher(IServiceScopeFactory scopes, ILogger<CancellationOperationsDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var noticeOffset = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>();
                await using var db = await factory.CreateDbContextAsync(token);
                var worker = scope.ServiceProvider.GetRequiredService<CancellationOperationsWorker>();
                var leases = scope.ServiceProvider.GetRequiredService<SqlJobLeases>();
                var notice = await db.Set<OutboxWork>().AsNoTracking().Where(x => x.Kind == "cancellation-notice" && (x.State == "pending" || x.State == "leased"))
                    .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip(noticeOffset).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(token);
                noticeOffset = notice is null ? 0 : noticeOffset + 1;
                if (notice is Guid id)
                {
                    try { if (await worker.PrepareNotice(id, scope.ServiceProvider.GetRequiredService<MessageDeliveryService>(), token) &&
                        await leases.ClaimWorkAsync("cancellation-notice", id, token) is { } noticeLease)
                    {
                        try { await worker.ApplyNotice(noticeLease, token); }
                        catch (Exception e) when (e is OperationalAccessException or QuoteOperationException) { await leases.FailAsync(noticeLease, JobFailure.Superseded, token); }
                    }}
                    catch(OperationalAccessException e) when(e.Code=="document-not-ready") { }
                    catch(Exception e) when(e is OperationalAccessException or QuoteOperationException) { await worker.RecordNoticeUnavailable(id,token); }
                }
                var lease = await leases.ClaimKindAsync("cancellation-certificate-withdrawal", token) ?? await leases.ClaimKindAsync("cancellation-task-close", token);
                if (lease is not null)
                {
                    try { await worker.Apply(lease, token); }
                    catch (Exception e) when (e is OperationalAccessException or QuoteOperationException) { await leases.FailAsync(lease, JobFailure.Superseded, token); }
                }
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Cancellation processing interrupted; original durable work remains recoverable.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }
    }
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<CancellationOperationsWorker>();
        builder.Services.AddScoped<CancellationOperationsService>();
        if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue("Cover:OperationalCancellationWorkerEnabled", true)) builder.Services.AddHostedService<CancellationOperationsDispatcher>();
    }
}
