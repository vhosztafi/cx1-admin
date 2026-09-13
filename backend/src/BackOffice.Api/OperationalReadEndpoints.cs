using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class OperationalReadEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull};
    private static readonly Dictionary<string,string> Summaries = new()
    {
        ["authentication.succeeded"] = "Signed in.", ["authentication.failed"] = "Sign-in attempt failed.",
        ["authentication.session-revoked"] = "Session revoked.", ["diagnostic.requested"] = "Demo probe requested.",
        ["diagnostic.completed"] = "Demo probe completed.", ["diagnostic.rejected"] = "Demo probe rejected.",
        ["diagnostic.callback-quarantined"] = "Changed demo callback quarantined.", ["diagnostic.inspected"] = "Demo job inspected by an administrator.",
        ["operations.jobs-read"] = "Operational job list inspected.", ["operations.audit-read"] = "Audit list inspected."
    };
    private static readonly string[] Kinds = ["rating","document","storage","email","lookup","mid","claims","payment","bordereau","export","diagnostic-probe"];

    public static void MapOperationalReads(this WebApplication app)
    {
        app.MapGet("/api/v1/admin/jobs", Jobs).RequireAuthorization("integration-admin");
        app.MapGet("/api/v1/admin/audit", Audit).RequireAuthorization("audit-read");
    }

    private static async Task<IResult> Jobs(HttpContext context, OperationalPaging paging, IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
    {
        var page = paging.Read(context, "state", "kind");
        var state = context.Request.Query["state"].ToString(); var kind = context.Request.Query["kind"].ToString();
        if (page is null || (state != "" && state is not ("pending" or "leased" or "succeeded" or "failed")) || (kind != "" && !Kinds.Contains(kind))) return Invalid(context);
        await using var db = await factory.CreateDbContextAsync(context.RequestAborted);
        var query = db.Set<OutboxWork>().AsNoTracking().Where(x => x.Kind == SqlJobLeases.DiagnosticKind && x.CreatedAt <= page.AsOf);
        if (state != "") query = query.Where(x => x.State == state);
        if (kind != "") query = query.Where(x => x.Kind == kind);
        var total = await query.CountAsync(context.RequestAborted);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip(page.Offset).Take(page.Size + 1).ToListAsync(context.RequestAborted);
        var ids = rows.Take(page.Size).Select(x => x.Id).ToArray();
        var receipts = await db.Set<DiagnosticReceipt>().Where(x => ids.Contains(x.WorkId)).ToDictionaryAsync(x => x.WorkId, x => x.Id, context.RequestAborted);
        var items = rows.Take(page.Size).Select(x => OperationalJobEndpoints.View(x) with
            {ResultResourceId = x.State == "succeeded" && receipts.TryGetValue(x.Id, out var id) ? id : null}).ToArray();
        await Inspect(db, page.Actor, "operations.jobs-read", time.GetUtcNow(), context.RequestAborted);
        return Results.Json(new {items, nextCursor = paging.Next(page, rows.Count > page.Size), totalCount = total}, Json);
    }

    private static async Task<IResult> Audit(HttpContext context, OperationalPaging paging, IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
    {
        var page = paging.Read(context, "actorId", "subjectRecordId", "from", "to", "eventType");
        if (page is null || !GuidFilter(context, "actorId", out var actorId) || !GuidFilter(context, "subjectRecordId", out var subjectId) ||
            !DateFilter(context, "from", out var from) || !DateFilter(context, "to", out var to) || (from.HasValue && to.HasValue && from > to)) return Invalid(context);
        var eventType = context.Request.Query["eventType"].ToString();
        if (eventType.Length > 100) return Invalid(context);
        await using var db = await factory.CreateDbContextAsync(context.RequestAborted);
        var known = Summaries.Keys.ToArray();
        // Each future event type needs its own reviewed disclosure before joining this view.
        var query = db.Set<AuditEvent>().AsNoTracking().Where(x => known.Contains(x.EventType) && x.OccurredAt <= page.AsOf);
        if (actorId.HasValue) query = query.Where(x => x.ActorId == actorId);
        if (subjectId.HasValue) query = query.Where(x => x.SubjectRecordId == subjectId);
        if (from.HasValue) query = query.Where(x => x.OccurredAt >= from);
        if (to.HasValue) query = query.Where(x => x.OccurredAt <= to);
        if (eventType != "") query = query.Where(x => x.EventType == eventType);
        var total = await query.CountAsync(context.RequestAborted);
        var rows = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Skip(page.Offset).Take(page.Size + 1)
            .Select(x => new {x.Id, x.EventType, x.OccurredAt, x.SubjectRecordId, x.CorrelationId,
                ActorLabel = db.Set<StaffUser>().Where(u => u.Id == x.ActorId).Select(u => u.DisplayName).FirstOrDefault() ?? "System"}).ToListAsync(context.RequestAborted);
        var items = rows.Take(page.Size).Select(x => new {x.Id, x.ActorLabel, x.EventType, x.OccurredAt, x.SubjectRecordId, x.CorrelationId, summary = Summaries[x.EventType]}).ToArray();
        await Inspect(db, page.Actor, "operations.audit-read", time.GetUtcNow(), context.RequestAborted);
        return Results.Json(new {items, nextCursor = paging.Next(page, rows.Count > page.Size), totalCount = total}, Json);
    }

    private static async Task Inspect(BackOfficeDbContext db, Guid actor, string eventType, DateTimeOffset now, CancellationToken token)
    {
        db.Add(new AuditEvent {ActorId = actor, CreatedBy = actor, EventType = eventType, OccurredAt = now, CorrelationId = Guid.NewGuid()});
        await db.SaveChangesAsync(token);
    }
    private static bool GuidFilter(HttpContext context, string name, out Guid? value)
    {
        value = null;
        if (!context.Request.Query.ContainsKey(name)) return true;
        if (!Guid.TryParseExact(context.Request.Query[name], "D", out var parsed)) return false;
        value = parsed; return true;
    }
    private static bool DateFilter(HttpContext context, string name, out DateTimeOffset? value)
    {
        value = null;
        if (!context.Request.Query.ContainsKey(name)) return true;
        var text = context.Request.Query[name].ToString();
        var offset = text.EndsWith('Z') || (text.Length >= 6 && text[^6] is '+' or '-');
        if (!offset || text.Length > 40 || !DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        value = parsed.ToUniversalTime(); return true;
    }
    private static IResult Invalid(HttpContext context) => IdentityEndpoints.Problem(context, 400, "invalid-query", "Use valid filters, page size and an unexpired cursor from this list.");
}
