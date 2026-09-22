using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Platform;

namespace BackOffice.Api;

public static partial class TaskEndpoints
{
    private static readonly JsonSerializerOptions InputJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void MapTasks(this WebApplication app)
    {
        MapTaskAttachments(app);
        app.MapGet("/api/v1/tasks", List).RequireAuthorization("task-read");
        app.MapGet("/api/v1/tasks/summary", Summary).RequireAuthorization("task-read");
        app.MapGet("/api/v1/task-assignees", Assignees).RequireAuthorization("task-assign");
        app.MapGet("/api/v1/tasks/{taskId:guid}/comments", (Guid taskId, HttpContext context, Microsoft.EntityFrameworkCore.IDbContextFactory<BackOffice.Infrastructure.Persistence.BackOfficeDbContext> factory, PartyPaging paging) => History(taskId, context, factory, paging, false)).RequireAuthorization("task-read");
        app.MapGet("/api/v1/tasks/{taskId:guid}/events", (Guid taskId, HttpContext context, Microsoft.EntityFrameworkCore.IDbContextFactory<BackOffice.Infrastructure.Persistence.BackOfficeDbContext> factory, PartyPaging paging) => History(taskId, context, factory, paging, true)).RequireAuthorization("task-read");
        app.MapPost("/api/v1/operational-subjects", (HttpContext context, TaskService service) => Run(context, async () =>
        {
            var input = await Input<SubjectInput>(context); QuoteEndpoints.Id(input.ParentId);
            if (input.Kind is not ("agency" or "relationship" or "quote" or "policy" or "servicing-draft")) throw new QuoteHttpException(422, "invalid-subject-kind");
            return await service.Register(LocalIdentityService.Actor(context.User), new(input.Kind, input.ParentId), QuoteHttpInput.Key(context.Request), context.RequestAborted);
        })).RequireAuthorization("subject-read");
        app.MapPost("/api/v1/tasks", (HttpContext context, TaskService service) => Run(context, async () =>
        {
            var input = await Input<CreateInput>(context); QuoteEndpoints.Id(input.SubjectRecordId);
            return await service.Create(LocalIdentityService.Actor(context.User), input.SubjectRecordId, new(input.TypeCode, input.Title, input.Priority, input.Assignment, input.DueOn), QuoteHttpInput.Key(context.Request), context.RequestAborted);
        })).RequireAuthorization("task-write");
        app.MapGet("/api/v1/tasks/{taskId:guid}", (Guid taskId, HttpContext context, TaskService service) => Run(context, () =>
        {
            QuoteEndpoints.Id(taskId); QuoteHttpInput.NoQuery(context.Request);
            return service.Read(LocalIdentityService.Actor(context.User), taskId, context.RequestAborted);
        })).RequireAuthorization("task-read");
        app.MapPut("/api/v1/tasks/{taskId:guid}", (Guid taskId, HttpContext context, TaskService service) => Run(context, async () =>
        {
            QuoteEndpoints.Id(taskId); var input = await Input<TaskWrite>(context);
            return await service.Update(LocalIdentityService.Actor(context.User), taskId, Version(context), QuoteHttpInput.Key(context.Request), input, context.RequestAborted);
        })).RequireAuthorization("task-write");
        app.MapPost("/api/v1/tasks/{taskId:guid}/transition", (Guid taskId, HttpContext context, TaskService service) => Run(context, async () =>
        {
            QuoteEndpoints.Id(taskId); var input = await Input<TransitionInput>(context);
            return await service.Transition(LocalIdentityService.Actor(context.User), taskId, Version(context), QuoteHttpInput.Key(context.Request), input.State, input.Reason, context.RequestAborted);
        })).RequireAuthorization("task-write");
        app.MapPut("/api/v1/tasks/{taskId:guid}/checklist", (Guid taskId, HttpContext context, TaskService service) => Run(context, async () =>
        {
            QuoteEndpoints.Id(taskId); var input = await Input<ChecklistInput>(context);
            return await service.Checklist(LocalIdentityService.Actor(context.User), taskId, Version(context), QuoteHttpInput.Key(context.Request), input.Items, input.Reason, context.RequestAborted);
        })).RequireAuthorization("task-write");
        app.MapPost("/api/v1/tasks/{taskId:guid}/comments", (Guid taskId, HttpContext context, TaskService service) => Run(context, async () =>
        {
            QuoteEndpoints.Id(taskId); var input = await Input<CommentInput>(context);
            return await service.Comment(LocalIdentityService.Actor(context.User), taskId, Version(context), QuoteHttpInput.Key(context.Request), input.Body, context.RequestAborted);
        })).RequireAuthorization("task-write");
        app.MapPost("/api/v1/tasks/bulk-assignment", (HttpContext context, TaskService service) => Run(context, async () =>
        {
            var input = await Input<BulkAssignmentInput>(context);
            return await service.BulkAssign(LocalIdentityService.Actor(context.User), input.Tasks, QuoteHttpInput.Key(context.Request), input.Assignment, input.Reason, context.RequestAborted);
        })).RequireAuthorization("task-assign");
        app.MapPost("/api/v1/tasks/bulk-due-date", (HttpContext context, TaskService service) => Run(context, async () =>
        {
            var input = await Input<BulkDueInput>(context);
            return await service.BulkDue(LocalIdentityService.Actor(context.User), input.Tasks, QuoteHttpInput.Key(context.Request), input.DueOn, input.Reason, context.RequestAborted);
        })).RequireAuthorization("task-assign");
        app.MapPost("/api/v1/tasks/bulk-completion", (HttpContext context, TaskService service) => Run(context, async () =>
        {
            var input = await Input<BulkCompleteInput>(context);
            return await service.BulkComplete(LocalIdentityService.Actor(context.User), input.Tasks, QuoteHttpInput.Key(context.Request), input.Reason, context.RequestAborted);
        })).RequireAuthorization("task-write");
    }

    public static async Task<T> Input<T>(HttpContext context)
    {
        using var json = await QuoteHttpInput.Read(context.Request, context.RequestAborted, 65536);
        try { return json.RootElement.Deserialize<T>(InputJson) ?? throw new QuoteHttpException(400, "invalid-task-input"); }
        catch (JsonException) { throw new QuoteHttpException(400, "invalid-task-input"); }
    }
    private static string Version(HttpContext context) => TaskService.Etag(QuoteHttpInput.Version(context.Request));
    private static async Task<IResult> Run(HttpContext context, Func<Task<CommandOutcome>> command)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        try { return QuoteEndpoints.Outcome(context, await command()); }
        catch (OperationalAccessException error) { return IdentityEndpoints.Problem(context, error.Status, error.Code, "The task request could not be completed."); }
        catch (TaskRuleException error) { return IdentityEndpoints.Problem(context, 422, error.Code, "Check the task details."); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    public sealed record SubjectInput([property: JsonRequired] string Kind, [property: JsonRequired] Guid ParentId);
    public sealed record CreateInput([property: JsonRequired] Guid SubjectRecordId, [property: JsonRequired] string TypeCode,
        [property: JsonRequired] string Title, [property: JsonRequired] string Priority, [property: JsonRequired] TaskAssignment Assignment, DateOnly? DueOn);
    public sealed record TransitionInput([property: JsonRequired] string State, [property: JsonRequired] string Reason);
    public sealed record ChecklistInput([property: JsonRequired] TaskChecklistChange[] Items, [property: JsonRequired] string Reason);
    public sealed record CommentInput([property: JsonRequired] string Body);
    public sealed record BulkAssignmentInput([property: JsonRequired] TaskSelection[] Tasks, [property: JsonRequired] TaskAssignment Assignment, [property: JsonRequired] string Reason);
    public sealed record BulkDueInput([property: JsonRequired] TaskSelection[] Tasks, [property: JsonRequired] DateOnly DueOn, [property: JsonRequired] string Reason);
    public sealed record BulkCompleteInput([property: JsonRequired] TaskSelection[] Tasks, [property: JsonRequired] string Reason);
}
