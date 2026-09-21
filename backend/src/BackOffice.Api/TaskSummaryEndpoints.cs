using System.Data;
using System.Text.Json;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static partial class TaskEndpoints
{
    private static async Task<IResult> Summary(HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        try
        {
            QuoteHttpInput.NoQuery(context.Request);
            var token = context.RequestAborted;
            await using var db = await factory.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var actor = await TaskDiscovery.Authorize(db, LocalIdentityService.Actor(context.User), token);
            var rows = TaskDiscovery.Rows(db, actor);
            var now = time.GetUtcNow();
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
            var active = rows.Where(x => x.State != "completed" && x.State != "cancelled");
            var open = await active.CountAsync(token);
            var dueToday = await active.CountAsync(x => x.DueOn == today, token);
            var overdue = await active.CountAsync(x => x.DueOn < today, token);
            var awaitingOthers = await active.CountAsync(x => x.State == "awaiting-information", token);
            // Comments and edits do not count as completions. Only immutable
            // transition events for currently completed, visible tasks qualify.
            var since = now.AddDays(-7);
            var completions = await db.Set<OperationalTaskEvent>().AsNoTracking()
                .Where(e => e.CreatedAt >= since && e.CreatedAt <= now && (e.Kind == "task.transitioned" || e.Kind == "task.bulk-completed") && rows.Any(t => t.Id == e.TaskId && t.State == "completed"))
                .Select(e => new { e.TaskId, e.SnapshotJson }).ToArrayAsync(token);
            var completedIds = new HashSet<Guid>();
            foreach (var completion in completions)
            {
                using var snapshot = JsonDocument.Parse(completion.SnapshotJson);
                if (snapshot.RootElement.GetProperty("state").GetString() == "completed") completedIds.Add(completion.TaskId);
            }
            await transaction.CommitAsync(token);
            return Results.Json(new { open, dueToday, overdue, awaitingOthers, completedSevenDays = completedIds.Count, asOf = now }, ClientEndpoints.Json);
        }
        catch (OperationalAccessException error) { return IdentityEndpoints.Problem(context, error.Status, error.Code, "The task summary could not be read."); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
