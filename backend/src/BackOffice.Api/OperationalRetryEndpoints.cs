using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Platform;

namespace BackOffice.Api;

public static class OperationalRetryEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull};

    public static void MapOperationalRetries(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
            app.MapPost("/api/v1/jobs/{jobId:guid}/retry", Retry).RequireAuthorization("integration-retry");
    }

    private static async Task<IResult> Retry(Guid jobId, RetryInput input, HttpContext context, SqlCommandBoundary commands, TimeProvider time)
    {
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000)
            return IdentityEndpoints.Problem(context, 422, "reason-required", "Supply a reason of at most 1000 characters.");
        var keys = context.Request.Headers["Idempotency-Key"];
        if (keys.Count != 1 || keys[0] is not {Length: >= 16 and <= 200} key || key != key.Trim())
            return IdentityEndpoints.Problem(context, 400, "idempotency-key-required", "Supply one command key of 16 to 200 characters.");
        var versions = context.Request.Headers.IfMatch;
        if (versions.Count == 0) return IdentityEndpoints.Problem(context, 428, "version-required", "Refresh the job and supply its ETag.");
        if (versions.Count != 1 || !TryVersion(versions[0], out var expected))
            return IdentityEndpoints.Problem(context, 400, "invalid-version", "Supply the job's exact ETag.");
        var actor = LocalIdentityService.Actor(context.User);
        var correlation = Guid.NewGuid();
        try
        {
            // Preconditions run inside the handler: a prior successful result replays first.
            var outcome = await commands.ExecuteAsync(new CommandIdentity(actor.UserId, $"/api/v1/jobs/{jobId}/retry", key, correlation),
                new RetryInput(input.Reason.Trim()), "diagnostic.retry-requested", async (db, token) =>
                {
                    var job = await SqlJobRetry.ApplyAsync(db, jobId, expected, actor.UserId, correlation, input.Reason.Trim(), time.GetUtcNow(), token);
                    return new CommandOutcome(job.Id, 202, JsonSerializer.Serialize(OperationalJobEndpoints.View(job), Json));
                }, context.RequestAborted);
            context.Response.Headers.Location = "/api/v1/jobs/" + jobId;
            return Results.Content(outcome.Body, "application/json", statusCode: outcome.Status);
        }
        catch (JobRetryException failure) { return IdentityEndpoints.Problem(context, failure.Status, failure.Code, "Refresh the job; this retry cannot be applied."); }
        catch (CommandKeyConflictException) { return IdentityEndpoints.Problem(context, 409, "idempotency-conflict", "This command key was used with different input."); }
        catch (CommandBusyException) { return IdentityEndpoints.Problem(context, 409, "command-busy", "Retry with the same command key."); }
    }

    private static bool TryVersion(string? text, out byte[] version)
    {
        version = [];
        if (text is null || text.Length != 14 || text[0] != '"' || text[^1] != '"') return false;
        try { version = Convert.FromBase64String(text[1..^1]); return version.Length == 8; }
        catch (FormatException) { return false; }
    }
    public sealed record RetryInput([property: JsonRequired] string Reason);
}
