using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class OperationalSettingEndpoints
{
    private static readonly string[] Scopes = ["diagnostic-probe/success", "diagnostic-probe/reject", "diagnostic-probe/fail-once", "diagnostic-probe/timeout-after-success"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull};

    public static void MapOperationalSettings(this WebApplication app)
    {
        var enabled = app.Environment.IsDevelopment() && app.Configuration.GetValue("Cover:DiagnosticWorkerEnabled", true);
        app.MapGet("/api/v1/admin/integrations", async (HttpContext context, OperationalPaging paging, IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time) =>
        {
            var page = paging.Read(context);
            if (page is null) return IdentityEndpoints.Problem(context, 400, "invalid-query", "Use a valid page size and cursor.");
            await using var db = await factory.CreateDbContextAsync(context.RequestAborted);
            var versions = db.Set<SettingVersion>().AsNoTracking().Where(x => Scopes.Contains(x.Scope) && x.EffectiveFrom <= page.AsOf && x.CreatedAt <= page.AsOf);
            var query = versions.Where(x => !versions.Any(newer => newer.Scope == x.Scope && newer.Version > x.Version));
            var total = await query.CountAsync(context.RequestAborted);
            var rows = await query.OrderBy(x => x.Scope).ThenBy(x => x.Id).Skip(page.Offset).Take(page.Size + 1).ToListAsync(context.RequestAborted);
            try
            {
                var items = rows.Take(page.Size).Select(x => View(x, enabled)).ToArray();
                await OperationalReadEndpoints.Inspect(db, page.Actor, "operations.integrations-read", time.GetUtcNow(), context.RequestAborted);
                return Results.Json(new {items, nextCursor = paging.Next(page, rows.Count > page.Size), totalCount = total}, Json);
            }
            catch (JsonException) {return Invalid(context);}
        }).RequireAuthorization("integration-admin");
        app.MapGet("/api/v1/admin/integrations/{integrationId:guid}", async (Guid integrationId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time) =>
        {
            await using var db = await factory.CreateDbContextAsync(context.RequestAborted);
            var now = time.GetUtcNow();
            var row = await db.Set<SettingVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == integrationId && Scopes.Contains(x.Scope) && x.EffectiveFrom <= now, context.RequestAborted);
            if (row is null) return IdentityEndpoints.Problem(context, 404, "integration-not-found", "Integration setting not found.");
            try
            {
                var item = View(row, enabled);
                await OperationalReadEndpoints.Inspect(db, LocalIdentityService.Actor(context.User).UserId, "operations.integrations-read", now, context.RequestAborted);
                return Results.Json(item, Json);
            }
            catch (JsonException) {return Invalid(context);}
        }).RequireAuthorization("integration-admin");
    }

    private static SettingView View(SettingVersion row, bool enabled)
    {
        var scenario = JsonSerializer.Deserialize<DiagnosticScenario>(row.Values, Json);
        if (scenario is null || scenario.Kind != SqlJobLeases.DiagnosticKind || row.Scope != "diagnostic-probe/" + scenario.Scenario) throw new JsonException();
        return new SettingView(row.Id, SqlJobLeases.DiagnosticKind, enabled, "demo", scenario.Scenario, row.Version, JobRetryBudget.InitialLimit, [5,30,120,600,1800]);
    }
    private static IResult Invalid(HttpContext context) => IdentityEndpoints.Problem(context, 503, "invalid-integration-setting", "The integration setting is unavailable.");
    private sealed record SettingView(Guid Id, string Kind, bool Enabled, string Mode, string Scenario, int Version, int MaxAttempts, int[] RetrySeconds);
}
