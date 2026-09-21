using System.Data;
using System.Globalization;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static partial class TaskEndpoints
{
    private static async Task<IResult> History(Guid taskId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging, bool events)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        try
        {
            QuoteEndpoints.Id(taskId); var token = context.RequestAborted; var actor = LocalIdentityService.Actor(context.User);
            await using var db = await factory.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var task = await TaskService.HoldRead(db, actor, taskId, token);
            var page = paging.ReadBound(context, actor, events ? "task-events-v1" : "task-comments-v1", TaskService.Etag(task.RowVersion));
            if (page is null) throw new QuoteHttpException(400, "invalid-task-cursor");
            object items; int count;
            if (events)
            {
                var rows = db.Set<OperationalTaskEvent>().AsNoTracking().Where(x => x.TaskId == taskId && x.CreatedAt <= page.AsOf);
                count = await rows.CountAsync(token);
                items = await rows.OrderBy(x => x.Sequence).Skip(page.Offset).Take(page.Size).Select(x => new { x.Id, x.TaskId, x.Sequence, x.Kind, x.Reason, x.ActorLabel, recordedAt = x.CreatedAt }).ToArrayAsync(token);
            }
            else
            {
                var rows = db.Set<OperationalTaskComment>().AsNoTracking().Where(x => x.TaskId == taskId && x.CreatedAt <= page.AsOf);
                count = await rows.CountAsync(token);
                items = await rows.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip(page.Offset).Take(page.Size).Select(x => new { x.Id, x.TaskId, x.Body, x.AuthorLabel, x.CreatedAt }).ToArrayAsync(token);
            }
            await transaction.CommitAsync(token);
            return Results.Json(new { items, totalCount = count, nextCursor = paging.Next(page, page.Offset + page.Size < count) }, ClientEndpoints.Json);
        }
        catch (OperationalAccessException error) { return IdentityEndpoints.Problem(context, error.Status, error.Code, "The task history could not be read."); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> List(HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging, TaskService service)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        try
        {
            var token = context.RequestAborted; var query = context.Request.Query;
            string Value(string name) => query[name].ToString();
            Guid? Id(string name)
            {
                if (!query.ContainsKey(name)) return null;
                if (Guid.TryParseExact(Value(name), "D", out var id) && id != Guid.Empty) return id;
                throw new QuoteHttpException(400, "invalid-task-query");
            }
            var subjectId = Id("subjectRecordId"); var owner = Id("ownerId"); var team = Id("teamId");
            var state = Value("state"); var priority = Value("priority"); var view = Value("view"); var search = Value("q"); var window = Value("dueWindow");
            var type = Value("typeCode"); if (query.ContainsKey("kind")) { if (type.Length > 0) throw new QuoteHttpException(400, "invalid-task-query"); type = Value("kind"); }
            if (state.Length > 0 && !TaskRules.States.Contains(state) || type.Length > 0 && !TaskRules.Types.Contains(type) || search.Length > 300 ||
                priority.Length > 0 && priority is not ("low" or "normal" or "high" or "urgent") ||
                view.Length > 0 && view is not ("my-open" or "team" or "created-by-me" or "completed") ||
                window.Length > 0 && window is not ("overdue" or "today" or "next-seven-days")) throw new QuoteHttpException(400, "invalid-task-query");
            DateOnly? before = null;
            if (query.ContainsKey("dueBefore"))
            {
                if (!DateOnly.TryParseExact(Value("dueBefore"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) throw new QuoteHttpException(400, "invalid-task-query");
                before = parsed;
            }
            await using var db = await factory.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var actor = await TaskDiscovery.Authorize(db, LocalIdentityService.Actor(context.User), token);
            var visible = TaskDiscovery.Rows(db, actor);
            var page = paging.ReadBound(context, actor, "task-reference-v1", await TaskDiscovery.Version(db, visible, token), "ownerId", "teamId", "state", "priority", "dueBefore", "subjectRecordId", "kind", "dueWindow", "view", "typeCode", "q");
            if (page is null) throw new QuoteHttpException(400, "invalid-task-cursor");
            var rows = visible.Where(x => x.CreatedAt <= page.AsOf);
            if (subjectId is not null) rows = rows.Where(x => x.SubjectId == subjectId);
            if (owner is not null) rows = rows.Where(x => x.OwnerId == owner);
            if (team is not null) rows = rows.Where(x => x.TeamId == team);
            if (state.Length > 0) rows = rows.Where(x => x.State == state);
            if (priority.Length > 0) rows = rows.Where(x => x.Priority == priority);
            if (type.Length > 0) rows = rows.Where(x => x.TypeCode == type);
            if (search.Length > 0) rows = rows.Where(x => x.Title.Contains(search) || x.Reference.Contains(search));
            if (before is not null) rows = rows.Where(x => x.DueOn < before);
            if (view == "my-open") rows = rows.Where(x => x.OwnerId == actor.UserId && x.State != "completed" && x.State != "cancelled");
            if (view == "created-by-me") rows = rows.Where(x => x.CreatedBy == actor.UserId);
            if (view == "completed") rows = rows.Where(x => x.State == "completed");
            if (view == "team") rows = rows.Where(x => actor.TeamId != null && (x.TeamId == actor.TeamId || db.Set<StaffUser>().Any(u => u.Id == x.OwnerId && u.TeamId == actor.TeamId)));
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(page.AsOf, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
            if (window == "overdue") rows = rows.Where(x => x.DueOn < today && x.State != "completed" && x.State != "cancelled");
            if (window == "today") rows = rows.Where(x => x.DueOn == today);
            if (window == "next-seven-days") { var until = today.AddDays(7); rows = rows.Where(x => x.DueOn >= today && x.DueOn < until); }
            var count = await rows.CountAsync(token);
            var selected = await rows.OrderBy(x => x.Reference).ThenBy(x => x.Id).Skip(page.Offset).Take(page.Size).ToArrayAsync(token);
            var items = await service.Views(db, selected, token); await transaction.CommitAsync(token);
            return Results.Json(new { items, totalCount = count, nextCursor = paging.Next(page, page.Offset + items.Length < count) }, ClientEndpoints.Json);
        }
        catch (OperationalAccessException error) { return IdentityEndpoints.Problem(context, error.Status, error.Code, "The task list could not be read."); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
