using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class OperationalRetryEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull};

    public static void MapOperationalRetries(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapPost("/api/v1/jobs/{jobId:guid}/retry", Retry).RequireAuthorization("integration-retry");
            app.MapPost("/api/v1/admin/jobs/retry-batch", RetryBatch).RequireAuthorization("integration-retry");
        }
    }

    private static async Task<IResult> RetryBatch(BatchInput input, HttpContext context, SqlCommandBoundary commands, TimeProvider time)
    {
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000 || input.Jobs is not {Length: >= 1 and <= 100} ||
            input.Jobs.Any(x => x is null || x.JobId == Guid.Empty) || input.Jobs.Select(x => x.JobId).Distinct().Count() != input.Jobs.Length)
            return IdentityEndpoints.Problem(context, 422, "invalid-retry-batch", "Select 1 to 100 distinct jobs and supply a reason.");
        var keys = context.Request.Headers["Idempotency-Key"];
        if (keys.Count != 1 || keys[0] is not {Length: >= 16 and <= 200} key || key != key.Trim())
            return IdentityEndpoints.Problem(context, 400, "idempotency-key-required", "Supply one command key of 16 to 200 characters.");
        var jobs = input.Jobs.OrderBy(x => x.JobId).ToArray();
        var versions = new Dictionary<Guid,byte[]>();
        foreach (var job in jobs)
        {
            if (!TryVersion(job.Etag, out var version)) return IdentityEndpoints.Problem(context, 400, "invalid-version", "Supply every selected job's exact ETag.");
            versions.Add(job.JobId, version);
        }
        var actor = LocalIdentityService.Actor(context.User); var correlation = Guid.NewGuid();
        var reason = input.Reason.Trim(); var jobIds = jobs.Select(x => x.JobId).ToArray();
        try
        {
            var outcome = await commands.ExecuteAsync(new CommandIdentity(actor.UserId, "/api/v1/admin/jobs/retry-batch", key, correlation),
                new {jobIds, reason}, "diagnostic.batch-retry-requested", async (db, token) =>
                {
                    // Stable lock order prevents overlapping batches taking opposite row locks.
                    var now = time.GetUtcNow();
                    foreach (var job in jobs) await SqlJobRetry.ApplyAsync(db, job.JobId, versions[job.JobId], actor.UserId, correlation, reason, now, token);
                    return new CommandOutcome(jobIds[0], 200, JsonSerializer.Serialize(new {jobIds}, Json));
                }, context.RequestAborted);
            return Results.Content(outcome.Body, "application/json", statusCode: outcome.Status);
        }
        catch (JobRetryException failure) { return IdentityEndpoints.Problem(context, failure.Status, failure.Code, "No jobs were queued; refresh the selection and retry."); }
        catch (CommandKeyConflictException) { return IdentityEndpoints.Problem(context, 409, "idempotency-conflict", "This command key was used with different input."); }
        catch (CommandBusyException) { return IdentityEndpoints.Problem(context, 409, "command-busy", "Retry with the same command key."); }
    }

    private static async Task<IResult> Retry(Guid jobId, HttpContext context, SqlCommandBoundary commands, TimeProvider time,
        IDbContextFactory<BackOfficeDbContext> factory, QuoteRatingJobs ratingJobs, CapacityJobs capacityJobs, QuoteDeliveryJobs deliveryJobs)
    {
        RetryInput input;
        try
        {
            QuoteEndpoints.Id(jobId);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); QuoteHttpInput.Keys(doc.RootElement, "reason");
            if (!doc.RootElement.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String) throw new QuoteHttpException(422, "reason-required");
            input = new(reason.GetString()!);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
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
            await using var db = await factory.CreateDbContextAsync(context.RequestAborted);
            if (await db.Set<OutboxWork>().AsNoTracking().AnyAsync(x => x.Id == jobId && x.Kind == QuoteTermsService.WorkKind, context.RequestAborted))
            {
                var deliveryOutcome = await deliveryJobs.RetryAsync(actor, jobId, expected, input.Reason, key, correlation, context.RequestAborted);
                context.Response.Headers.Location = "/api/v1/jobs/" + jobId;
                return QuoteEndpoints.Outcome(context, deliveryOutcome);
            }
            if (await db.Set<OutboxWork>().AsNoTracking().AnyAsync(x => x.Id == jobId && x.Kind == CapacityService.WorkKind, context.RequestAborted))
            {
                var capacityOutcome = await capacityJobs.RetryAsync(actor, jobId, expected, input.Reason, key, correlation, context.RequestAborted);
                context.Response.Headers.Location = "/api/v1/jobs/" + jobId;
                return QuoteEndpoints.Outcome(context, capacityOutcome);
            }
            if (await db.Set<OutboxWork>().AsNoTracking().AnyAsync(x => x.Id == jobId && x.Kind == QuoteRatingService.WorkKind, context.RequestAborted))
            {
                var ratingOutcome = await ratingJobs.RetryAsync(actor, jobId, expected, input.Reason, key, correlation, context.RequestAborted);
                context.Response.Headers.Location = "/api/v1/jobs/" + jobId;
                return QuoteEndpoints.Outcome(context, ratingOutcome);
            }
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
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static bool TryVersion(string? text, out byte[] version)
    {
        version = [];
        if (text is null || text.Length != 14 || text[0] != '"' || text[^1] != '"') return false;
        try { version = Convert.FromBase64String(text[1..^1]); return version.Length == 8; }
        catch (FormatException) { return false; }
    }
    public sealed record RetryInput([property: JsonRequired] string Reason);
    public sealed record BatchJob([property: JsonRequired] Guid JobId, [property: JsonRequired] string Etag);
    public sealed record BatchInput([property: JsonRequired] BatchJob[] Jobs, [property: JsonRequired] string Reason);
}
