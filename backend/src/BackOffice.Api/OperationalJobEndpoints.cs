using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class OperationalJobEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private const string ProbeRoute = "/api/v1/admin/diagnostic-probes";

    public static void MapOperationalJobs(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
            app.MapPost(ProbeRoute, StartProbe).RequireAuthorization("integration-admin");
        app.MapGet("/api/v1/jobs/{jobId:guid}", GetJob).RequireAuthorization();
    }

    private static async Task<IResult> StartProbe(ProbeInput input, HttpContext context, SqlCommandBoundary commands, TimeProvider time)
    {
        if (input.Scenario is not ("success" or "reject" or "fail-once" or "timeout-after-success"))
            return IdentityEndpoints.Problem(context, 422, "invalid-scenario", "Choose a supported demo scenario.");
        var values = context.Request.Headers["Idempotency-Key"];
        if (values.Count != 1 || values[0] is not { Length: >= 16 and <= 200 } key || key != key.Trim())
            return IdentityEndpoints.Problem(context, 400, "idempotency-key-required", "Supply one command key of 16 to 200 characters.");
        var actor = LocalIdentityService.Actor(context.User);
        var correlation = Guid.NewGuid();
        try
        {
            var outcome = await commands.ExecuteAsync(new CommandIdentity(actor.UserId, ProbeRoute, key, correlation), input, "diagnostic.requested", async (db, token) =>
            {
                var now = time.GetUtcNow();
                var scope = "diagnostic-probe/" + input.Scenario;
                var setting = await db.Set<SettingVersion>().Where(x => x.Scope == scope && x.EffectiveFrom <= now)
                    .OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
                if (setting is null) throw new DiagnosticConfigurationException();
                var job = new OutboxWork { Kind = SqlJobLeases.DiagnosticKind, ScenarioVersionId = setting.Id,
                    Payload = "{\"probe\":\"foundation\"}", NextAttemptAt = now, CreatedBy = actor.UserId, CorrelationId = correlation };
                job.OperationKey = "diagnostic/" + job.Id.ToString("N");
                db.Add(job);
                return new CommandOutcome(job.Id, 202, JsonSerializer.Serialize(View(job), Json));
            }, context.RequestAborted);
            context.Response.Headers.Location = "/api/v1/jobs/" + outcome.ResourceId;
            return Results.Content(outcome.Body, "application/json", statusCode: outcome.Status);
        }
        catch (CommandKeyConflictException) { return IdentityEndpoints.Problem(context, 409, "idempotency-conflict", "This command key was used with different input."); }
        catch (CommandBusyException) { return IdentityEndpoints.Problem(context, 409, "command-busy", "Retry with the same command key."); }
        catch (DiagnosticConfigurationException) { return IdentityEndpoints.Problem(context, 503, "demo-not-initialized", "Initialize the demo scenarios before starting a probe."); }
    }

    private static async Task<IResult> GetJob(Guid jobId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time, BackOffice.Infrastructure.Underwriting.QuoteRatingJobs ratingJobs, BackOffice.Infrastructure.Underwriting.CapacityJobs capacityJobs)
    {
        var actor = LocalIdentityService.Actor(context.User);
        if (actor.AgencyId != null) return IdentityEndpoints.Problem(context, 403, "forbidden", "Access denied.");
        var administrator = actor.HasCapability("integration-admin");
        await using var db = await factory.CreateDbContextAsync(context.RequestAborted);
        if (await db.Set<OutboxWork>().AsNoTracking().AnyAsync(x => x.Id == jobId && x.Kind == "capacity-escalation", context.RequestAborted))
        {
            try
            {
                var read = await capacityJobs.ReadAsync(actor, jobId, context.RequestAborted);
                context.Response.Headers.ETag = "\"" + Convert.ToBase64String(read.Work.RowVersion) + "\"";
                return Results.Json(View(read.Work) with { RetryAllowed = read.RetryAllowed }, Json);
            }
            catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
        }
        if (await db.Set<OutboxWork>().AsNoTracking().AnyAsync(x => x.Id == jobId && x.Kind == "quote-rating", context.RequestAborted))
        {
            try
            {
                var read = await ratingJobs.ReadAsync(actor, jobId, context.RequestAborted);
                context.Response.Headers.ETag = "\"" + Convert.ToBase64String(read.Work.RowVersion) + "\"";
                return Results.Json(View(read.Work) with { RetryAllowed = read.RetryAllowed }, Json);
            }
            catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
        }
        // Business-job subject scope is supplied by its owning phase. It cannot inherit
        // diagnostic creator access or administrator access by sharing this endpoint.
        var job = await db.Set<OutboxWork>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId &&
            x.Kind == SqlJobLeases.DiagnosticKind && (x.CreatedBy == actor.UserId || administrator), context.RequestAborted);
        if (job is null) return IdentityEndpoints.Problem(context, 404, "job-not-found", "Job not found.");
        if (administrator && job.CreatedBy != actor.UserId)
        {
            db.Add(new AuditEvent { ActorId = actor.UserId, CreatedBy = actor.UserId, SubjectRecordId = job.Id,
                EventType = "diagnostic.inspected", OccurredAt = time.GetUtcNow(), CorrelationId = Guid.NewGuid() });
            await db.SaveChangesAsync(context.RequestAborted);
        }
        context.Response.Headers.ETag = "\"" + Convert.ToBase64String(job.RowVersion) + "\"";
        var receiptId = await db.Set<DiagnosticReceipt>().Where(x => x.WorkId == job.Id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(context.RequestAborted);
        return Results.Json(View(job) with { ResultResourceId = job.State == "succeeded" ? receiptId : null }, Json);
    }

    internal static JobView View(OutboxWork job) => new(job.Id, job.Kind, job.State, job.Attempts,
        job.State == "pending" ? job.NextAttemptAt : null, job.CompletedAt, null, SafeCode(job.ErrorCode), job.AttemptLimit,
        JobRetryBudget.ExpandedLimit(job.State, job.ErrorCode, job.Attempts, job.AttemptLimit) is not null);

    private static string? SafeCode(string? code) => code switch
    {
        null => null,
        "provider-unavailable" or "provider-timeout" or "provider-rejected" or "invalid-payload" or "provider-conflict" or "attempts-exhausted" => code,
        _ => "job-failed"
    };
    public sealed record ProbeInput([property: JsonRequired] string Scenario);
    internal sealed record JobView(Guid Id, string Kind, string State, int Attempts, DateTimeOffset? NextAttemptAt, DateTimeOffset? CompletedAt, Guid? ResultResourceId, string? ErrorCode, int AttemptLimit, bool RetryAllowed);
    private sealed class DiagnosticConfigurationException : Exception;
}
