using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Administration;
using BackOffice.Infrastructure.Identity;
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
        ["diagnostic.retry-authorized"] = "A bounded demo recovery cycle was authorized.", ["diagnostic.retry-requested"] = "Demo recovery queued.",
        ["diagnostic.batch-retry-requested"] = "A batch of demo recovery jobs was queued.",
        ["operations.integrations-read"] = "Integration settings inspected.",
        ["operations.jobs-read"] = "Operational job list inspected.", ["operations.audit-read"] = "Audit list inspected."
    };
    private static readonly string[] Kinds = ["rating","document","storage","email","lookup","mid","claims","payment","bordereau","export","diagnostic-probe"];

    public static void MapOperationalReads(this WebApplication app)
    {
        app.MapGet("/api/v1/admin/jobs", Jobs).RequireAuthorization("integration-admin");
        app.MapGet("/api/v1/admin/audit", (HttpContext c, OperationalPaging p, IDbContextFactory<BackOfficeDbContext> f, TimeProvider t)=>AdministrationEndpoints.Work(c,()=>Audit(c,p,f,t))).RequireAuthorization("audit-read");
        app.MapGet("/api/v1/admin/audit/{id:guid}",(Guid id,HttpContext c,IDbContextFactory<BackOfficeDbContext> f)=>AdministrationEndpoints.Work(c,async()=>
        {
            await using var db=await f.CreateDbContextAsync(c.RequestAborted);await using var tx=await db.Database.BeginTransactionAsync(c.RequestAborted);
            await AdminAccess.Authorize(db,LocalIdentityService.Actor(c.User),c.RequestAborted);
            var row=await db.Set<AuditEvent>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,c.RequestAborted);
            if(row==null)return IdentityEndpoints.Problem(c,404,"audit-not-found","Audit record not found.");
            var result=new{row.Id,row.ActorId,row.EventType,row.OccurredAt,row.SubjectRecordId,row.CorrelationId,row.Reason,before=AuditDisclosure.Read(row.Before),after=AuditDisclosure.Read(row.After)};
            await tx.CommitAsync(c.RequestAborted);return Results.Json(result);
        })).RequireAuthorization("audit-read");
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
        var page = paging.Read(context, "actorId", "subjectRecordId", "from", "to", "eventType", "reason");
        if (page is null || !GuidFilter(context, "actorId", out var actorId) || !GuidFilter(context, "subjectRecordId", out var subjectId) ||
            !DateFilter(context, "from", out var from) || !DateFilter(context, "to", out var to) || (from.HasValue && to.HasValue && from > to)) return Invalid(context);
        var eventType = context.Request.Query["eventType"].ToString();
        var reason=context.Request.Query["reason"].ToString();
        if (eventType.Length > 100 || reason.Length>1000) return Invalid(context);
        await using var db = await factory.CreateDbContextAsync(context.RequestAborted);
        await using var tx=await db.Database.BeginTransactionAsync(context.RequestAborted);
        await AdminAccess.Authorize(db,LocalIdentityService.Actor(context.User),context.RequestAborted);
        // Metadata is visible to security administrators; detailed payload fields
        // pass a separate disclosure allowlist, never raw serialized snapshots.
        var query = db.Set<AuditEvent>().AsNoTracking().Where(x => x.OccurredAt <= page.AsOf);
        if (actorId.HasValue) query = query.Where(x => x.ActorId == actorId);
        if (subjectId.HasValue) query = query.Where(x => x.SubjectRecordId == subjectId);
        if (from.HasValue) query = query.Where(x => x.OccurredAt >= from);
        if (to.HasValue) query = query.Where(x => x.OccurredAt <= to);
        if (eventType != "") query = query.Where(x => x.EventType == eventType);
        if (reason!="") query=query.Where(x=>x.Reason!=null&&x.Reason.Contains(reason));
        var total = await query.CountAsync(context.RequestAborted);
        var rows = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Skip(page.Offset).Take(page.Size + 1)
            .Select(x => new {x.Id, x.ActorId, x.EventType, x.OccurredAt, x.SubjectRecordId, x.CorrelationId, x.Reason,
                ActorLabel = db.Set<StaffUser>().Where(u => u.Id == x.ActorId).Select(u => u.DisplayName).FirstOrDefault() ?? "System"}).ToListAsync(context.RequestAborted);
        var items = rows.Take(page.Size).Select(x => new {x.Id, x.ActorId, x.ActorLabel, x.EventType, x.OccurredAt, x.SubjectRecordId, x.CorrelationId, x.Reason, summary = Summaries.GetValueOrDefault(x.EventType,"Recorded action.")}).ToArray();
        await Inspect(db, page.Actor, "operations.audit-read", time.GetUtcNow(), context.RequestAborted);
        await tx.CommitAsync(context.RequestAborted);
        return Results.Json(new {items, nextCursor = paging.Next(page, rows.Count > page.Size), totalCount = total}, Json);
    }

    internal static async Task Inspect(BackOfficeDbContext db, Guid actor, string eventType, DateTimeOffset now, CancellationToken token)
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
