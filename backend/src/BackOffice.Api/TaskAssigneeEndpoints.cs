using System.Data;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static partial class TaskEndpoints
{
    private static async Task<IResult> Assignees(HttpContext context, IDbContextFactory<BackOfficeDbContext> factory)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        try
        {
            var query = context.Request.Query; var kind = query["kind"].ToString(); var search = query["q"].ToString();
            var rawIds = query["subjectRecordId"];
            if (kind is not ("user" or "team") || query["kind"].Count != 1 || query["q"].Count > 1 || search.Length > 100 || rawIds.Count is < 1 or > 100 || query.Keys.Any(x => x is not ("kind" or "q" or "subjectRecordId"))) throw new QuoteHttpException(400, "invalid-task-assignee-query");
            var ids = new List<Guid>();
            foreach (var raw in rawIds)
            {
                if (!Guid.TryParseExact(raw, "D", out var id) || id == Guid.Empty || ids.Contains(id)) throw new QuoteHttpException(400, "invalid-task-assignee-query");
                ids.Add(id);
            }
            var token = context.RequestAborted;
            await using var db = await factory.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var held = await OperationalScope.HoldSubjects(db, LocalIdentityService.Actor(context.User), ids, "task-assign", token);
            var choices = await TaskService.Assignees(db, held, kind, search, token);
            await transaction.CommitAsync(token);
            return Results.Json(choices, ClientEndpoints.Json);
        }
        catch (OperationalAccessException error) { return IdentityEndpoints.Problem(context, error.Status, error.Code, "Eligible task owners could not be read."); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
