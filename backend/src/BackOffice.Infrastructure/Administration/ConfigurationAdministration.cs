using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Application.Parties;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Administration;

public sealed record ConfigurationEdit(string Scope, JsonElement Values, DateTimeOffset EffectiveFrom, string Reason);
public sealed record TemplateEdit(string Title, string Notice, DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveTo, string Reason);

public sealed class ConfigurationAdministration(IDbContextFactory<BackOfficeDbContext> factory, SqlCommandBoundary commands,
    IPolicyDocumentRenderer renderer, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly string[] BaseScopes = ["organisation", "matching-rule", "message-template/standard"];
    private static bool Scope(string value) => BaseScopes.Contains(value) || value.StartsWith("workflow-task/", StringComparison.Ordinal) ||
        AdministrativeConfiguration.FlagTypes.Any(x => value == "flag-definition/" + x);
    private static string Etag(SettingVersion row) => AdministrationRequests.Etag(row);
    private static string Etag(TemplateVersion row) => $"\"{row.Id:N}-{row.Version}\"";
    private static object View(SettingVersion row) => new { row.Id, row.Scope, row.Version, row.EffectiveFrom,
        values = JsonSerializer.Deserialize<JsonElement>(row.Values), etag = Etag(row) };
    private static object View(TemplateVersion row) => new { row.Id, row.ProductId, row.Code, row.Version, row.Kind, row.EffectiveFrom, row.EffectiveTo,
        values = JsonSerializer.Deserialize<JsonElement>(row.ContentJson), etag = Etag(row) };

    public async Task<object> ListAsync(ActorContext actor, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdminAccess.Authorize(db, actor, ct);
        var rows = await db.Set<SettingVersion>().AsNoTracking().Where(x => BaseScopes.Contains(x.Scope) ||
            x.Scope.StartsWith("workflow-task/") || x.Scope.StartsWith("flag-definition/")).ToArrayAsync(ct);
        var latest = rows.Where(x => Scope(x.Scope)).GroupBy(x => x.Scope).Select(x => x.MaxBy(y => y.Version)!).ToList();
        var settings = latest.Select(View).ToList();
        foreach (var scope in BaseScopes.Concat(AdministrativeConfiguration.FlagTypes.Select(x => "flag-definition/" + x)))
            if (!latest.Any(x => x.Scope == scope)) settings.Add(new { id = Guid.Empty, scope, version = 0,
                effectiveFrom = (DateTimeOffset?)null, values = Defaults(scope), etag = "\"new\"" });
        var templates = await db.Set<TemplateVersion>().AsNoTracking().Where(x => x.State == "published").ToArrayAsync(ct);
        var result = new { settings, templates = templates.GroupBy(x => new { x.ProductId, x.Code }).Select(x => View(x.MaxBy(y => y.Version)!)),
            teams = await db.Set<Team>().AsNoTracking().Select(x => new { x.Id, x.Name }).ToArrayAsync(ct),
            products = await db.Set<Product>().AsNoTracking().Select(x => new { x.Id, x.Name }).ToArrayAsync(ct) };
        await tx.CommitAsync(ct); return result;
    }

    public Task<CommandOutcome> SaveAsync(ActorContext actor, ConfigurationEdit input, string etag, string key, CancellationToken ct = default)
        => Command(actor, "/api/v1/admin/configuration", key, new { input, etag }, async (db, token) =>
        {
            AdminAccess.Text(input.Reason, 1000);
            if (input.Scope is null || !Scope(input.Scope) || input.Values.ValueKind != JsonValueKind.Object || input.Values.GetRawText().Length > 16384)
                throw Invalid();
            if (input.EffectiveFrom.Offset != TimeSpan.Zero || input.EffectiveFrom < time.GetUtcNow().AddMinutes(-5)) throw Invalid();
            var source = await db.Set<SettingVersion>().Where(x => x.Scope == input.Scope).OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
            if (string.IsNullOrEmpty(etag)) throw new QuoteOperationException(428, "administration-version-required");
            if (etag != (source is null ? "\"new\"" : Etag(source))) throw new QuoteOperationException(412, "administration-version-changed");
            if (source?.EffectiveFrom > time.GetUtcNow()) throw new QuoteOperationException(409, "configuration-future-version-pending");
            if (source is null && input.Scope.StartsWith("workflow-task/", StringComparison.Ordinal)) throw Invalid();
            var row = new SettingVersion { Scope = input.Scope, Version = (source?.Version ?? 0) + 1,
                EffectiveFrom = input.EffectiveFrom < time.GetUtcNow() ? time.GetUtcNow() : input.EffectiveFrom, CreatedBy = actor.UserId, CreatedAt = time.GetUtcNow() };
            row.Values = await ValidValues(db, row, input.Values, token);
            db.Add(row); AdminAccess.Audit(db, actor, row.Id, "administration.configuration-published", input.Reason,
                source is null ? null : View(source), View(row), time.GetUtcNow());
            return Outcome(row.Id, View(row), 201);
        }, ct);

    public Task<CommandOutcome> TemplateAsync(ActorContext actor, Guid id, TemplateEdit input, string etag, string key, CancellationToken ct = default)
        => Command(actor, $"/api/v1/admin/templates/{id}/successor", key, new { input, etag }, async (db, token) =>
        {
            var source = await db.Set<TemplateVersion>().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new QuoteOperationException(404, "template-not-found");
            if (string.IsNullOrEmpty(etag)) throw new QuoteOperationException(428, "administration-version-required");
            var latest = await db.Set<TemplateVersion>().Where(x => x.ProductId == source.ProductId && x.Code == source.Code).OrderByDescending(x => x.Version).FirstAsync(token);
            if (Etag(latest) != etag || latest.Id != id) throw new QuoteOperationException(412, "administration-version-changed");
            ValidateTemplate(input); AdminAccess.Text(input.Reason, 1000);
            if (input.EffectiveFrom < time.GetUtcNow().AddMinutes(-5) || latest.EffectiveFrom > time.GetUtcNow()) throw Invalid();
            var node = System.Text.Json.Nodes.JsonNode.Parse(source.ContentJson)!;
            node["title"] = input.Title.Trim(); node["notice"] = input.Notice.Trim();
            var row = new TemplateVersion { ProductId = source.ProductId, Code = source.Code, Version = source.Version + 1,
                Kind = source.Kind, State = "published", ContentJson = node.ToJsonString(),
                EffectiveFrom = input.EffectiveFrom < time.GetUtcNow() ? time.GetUtcNow() : input.EffectiveFrom,
                EffectiveTo = input.EffectiveTo, CreatedBy = actor.UserId, CreatedAt = time.GetUtcNow() };
            db.Add(row); AdminAccess.Audit(db, actor, row.Id, "administration.template-published", input.Reason, View(source), View(row), time.GetUtcNow());
            return Outcome(row.Id, View(row), 201);
        }, ct);

    public async Task<byte[]> PreviewAsync(ActorContext actor, Guid id, TemplateEdit input, CancellationToken ct = default)
    {
        ValidateTemplate(input);
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdminAccess.Authorize(db, actor, ct);
        var template = await db.Set<TemplateVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new QuoteOperationException(404, "template-not-found");
        var product = await db.Set<Product>().SingleAsync(x => x.Id == template.ProductId, ct);
        var commercial = product.Code == CommercialCaptureRules.ProductCode;
        var pins = new QuoteVersionPins(Guid.NewGuid(), Guid.NewGuid(), "1.0", commercial ? CommercialCaptureRules.QuestionVersion : QuoteCatalogueIdentity.Version,
            commercial ? CommercialCaptureRules.ReferenceVersion : QuoteCatalogueIdentity.Version);
        var resource = commercial ? "CommercialDemo.Ready" : "QuoteDemo.quote-capture-" + product.Code + ".json";
        await using var stream = typeof(ConfigurationAdministration).Assembly.GetManifestResourceStream(resource)!;
        using var example = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var proposal = commercial ? example.RootElement.GetRawText() : example.RootElement.GetProperty("proposal").GetRawText();
        var prepared = QuoteRules.Prepare(proposal, product.Code, pins);
        // The production renderer shows a fictional quotation's title/notice.
        // No stored customer source, file object, outbox or delivery is created.
        var json = JsonSerializer.Serialize(new { format = "quote-template-1", title = input.Title.Trim(), notice = input.Notice.Trim() });
        var render = new DocumentRenderInput(Guid.NewGuid(), "quote-revision", prepared.Input.Json, prepared.Input.ContentHash,
            id, json, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))), product.Code,
            "statement-of-fact", "FICTIONAL-TEMPLATE-PREVIEW", product.Code, "quote-terms", pins);
        var result = renderer.Render(render); await tx.CommitAsync(ct); return result.Bytes;
    }

    public async Task<object> MessageTemplatesAsync(ActorContext actor, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var identity = await IdentitySnapshot.Lock(db, new(actor.UserId, actor.AgencyId), ct);
        if (identity is null || actor.AgencyId != null || !new ActorContext(actor.UserId, identity.User.TeamId, null, identity.Roles.Select(x => x.Code).ToHashSet()).HasCapability("message-write"))
            throw new QuoteOperationException(403, "administration-access-denied");
        var organisation = await AdministrativeConfiguration.Organisation(db, time.GetUtcNow(), ct);
        var row = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope == "message-template/standard" && x.EffectiveFrom <= time.GetUtcNow()).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        var template = row is null ? (MessageTemplateConfiguration)Defaults("message-template/standard") : Parse<MessageTemplateConfiguration>(JsonSerializer.Deserialize<JsonElement>(row.Values), "name", "body", "enabled");
        var items = template.Enabled ? new[] { new { id = row?.Id ?? Guid.Empty, template.Name, body = template.Body.Replace("{{organisation}}", organisation.Name, StringComparison.Ordinal) + (string.IsNullOrWhiteSpace(organisation.NotificationSignature) ? "" : "\n\n" + organisation.NotificationSignature) } } : [];
        await tx.CommitAsync(ct); return new { items };
    }

    private static async Task<string> ValidValues(BackOfficeDbContext db, SettingVersion row, JsonElement values, CancellationToken ct)
    {
        object value;
        if (row.Scope.StartsWith("workflow-task/", StringComparison.Ordinal))
        {
            WorkflowTaskDefinition rule;
            try { rule = WorkflowTaskRules.Parse(values.GetRawText(), row.Scope); } catch (TaskRuleException) { throw Invalid(); }
            if (rule.AssignmentTeamId is Guid team && !await db.Set<Team>().AnyAsync(x => x.Id == team, ct)) throw Invalid();
            value = rule;
        }
        else if (row.Scope == "matching-rule")
        {
            var fields=new[]{"id","version","duplicateQuotePolicy","requireReview","summary"};
            var rule = Parse<MatchRuleSnapshot>(values, values.TryGetProperty("brokerOfRecordDays",out _)?[..fields,"brokerOfRecordDays"]:fields);
            if (rule.BrokerOfRecordDays is <1 or >3650 || rule.DuplicateQuotePolicy is not ("refer" or "allow-competing" or "broker-of-record")) throw Invalid();
            value = rule with { Id = row.Id, Version = row.Version, Summary = AdminAccess.Text(rule.Summary, 1000) };
        }
        else if (row.Scope.StartsWith("flag-definition/", StringComparison.Ordinal))
        {
            var flag = Parse<FlagConfiguration>(values, "enabled", "maximumReviewDays", "agencySharingAllowed");
            if (flag.MaximumReviewDays is < 1 or > 3650) throw Invalid(); value = flag;
        }
        else if (row.Scope == "organisation")
        {
            var organisation = Parse<OrganisationConfiguration>(values, "name", "clientReferencePrefix", "notificationsEnabled", "notificationSignature");
            AdminAccess.Text(organisation.Name);
            if (organisation.ClientReferencePrefix is null || !Regex.IsMatch(organisation.ClientReferencePrefix, "^[A-Z][A-Z0-9]{1,7}$") ||
                organisation.NotificationSignature is null || organisation.NotificationSignature.Length > 1000 || organisation.NotificationSignature.IndexOfAny(['<','>']) >= 0) throw Invalid();
            value = organisation;
        }
        else
        {
            var template = Parse<MessageTemplateConfiguration>(values, "name", "body", "enabled"); AdminAccess.Text(template.Name);
            AdminAccess.Text(template.Body, 7000);
            var plain = template.Body.Replace("{{organisation}}", "", StringComparison.Ordinal);
            if (plain.IndexOfAny(['<','>','{','}']) >= 0) throw Invalid(); value = template;
        }
        return JsonSerializer.Serialize(value, Json);
    }
    private static T Parse<T>(JsonElement input, params string[] fields)
    {
        var keys = input.EnumerateObject().Select(x => x.Name).ToArray();
        if (keys.Length != fields.Length || !keys.ToHashSet(StringComparer.Ordinal).SetEquals(fields)) throw Invalid();
        try { return input.Deserialize<T>(Json) ?? throw Invalid(); } catch (JsonException) { throw Invalid(); }
    }
    private static object Defaults(string scope) => scope switch
    {
        "organisation" => AdministrativeConfiguration.DefaultOrganisation,
        "matching-rule" => new MatchRuleSnapshot(Guid.Empty, 0, "refer", true, "Review possible duplicate client identities; never merge automatically."),
        "message-template/standard" => new MessageTemplateConfiguration("Standard follow-up", "Please review the attached information.\n\n{{organisation}}", true),
        _ => AdministrativeConfiguration.DefaultFlag
    };
    private static void ValidateTemplate(TemplateEdit input)
    {
        AdminAccess.Text(input.Title); AdminAccess.Text(input.Notice, 4000);
        if (input.Title.IndexOfAny(['<','>','{','}']) >= 0 || input.Notice.IndexOfAny(['<','>','{','}']) >= 0 ||
            input.EffectiveFrom.Offset != TimeSpan.Zero || input.EffectiveTo.Offset != TimeSpan.Zero || input.EffectiveFrom >= input.EffectiveTo) throw Invalid();
    }
    private Task<CommandOutcome> Command<T>(ActorContext actor, string route, string key, T body, Func<BackOfficeDbContext,CancellationToken,Task<CommandOutcome>> write, CancellationToken ct)
        => commands.ExecuteAuthorizedAsync(new(actor.UserId, route, key, Guid.NewGuid()), body, "administration.configuration-command",
            (db, token) => AdminAccess.Authorize(db, actor, token), write, ct, IsolationLevel.Serializable);
    private static QuoteOperationException Invalid() => new(400, "configuration-input-invalid");
    private static CommandOutcome Outcome(Guid id, object value, int status) => new(id, status, JsonSerializer.Serialize(value, Json));
}
