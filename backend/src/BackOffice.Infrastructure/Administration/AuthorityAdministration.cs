using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Administration;

public sealed record AuthorityProposal(Guid SourceAuthorityId, string SourceEtag, Guid ProductVersionId,
    DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveTo, JsonElement Limits, Guid RoutingTeamId,
    Guid[] UserIds, string Reason);
internal sealed record AuthorityRequestData(AuthorityProposal Input, Guid RuntimeId, string BinderEtag, string Definition, string Version);

public sealed class AuthorityAdministration(IDbContextFactory<BackOfficeDbContext> factory, SqlCommandBoundary commands, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = ProductAdministration.Json;
    public async Task<object> ListAsync(ActorContext actor, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdminAccess.Authorize(db, actor, ct);
        var versions = await db.Set<AuthorityVersion>().AsNoTracking().OrderBy(x => x.Version).ToArrayAsync(ct);
        var products = await db.Set<Product>().AsNoTracking().ToArrayAsync(ct);
        var runtime = await Runtime(db, ct);
        var grants = await db.Set<UserAuthorityGrant>().AsNoTracking().ToArrayAsync(ct);
        var result = new { versions = versions.Select(x => new { x.Id, x.ProductVersionId, x.BinderVersionId, x.Version, x.State,
            productName = products.Single(p => p.Id == x.ProductId).Name, x.EffectiveFrom, x.EffectiveTo,
            limits = JsonDocument.Parse(x.DefinitionJson).RootElement.GetProperty("limits").Clone(), etag = AdminAccess.Etag(x.RowVersion) }),
            grants = grants.Select(x => new { x.Id, x.UserId, x.AuthorityVersionId, x.EffectiveFrom, x.EffectiveTo, x.RevokedAt, x.RevocationReason, etag = AdminAccess.Etag(x.RowVersion) }),
            users = await db.Set<StaffUser>().AsNoTracking().Where(x => x.AgencyId == null && x.State == "active").Select(x => new { x.Id, x.DisplayName }).ToArrayAsync(ct),
            teams = await db.Set<Team>().AsNoTracking().Select(x => new { x.Id, x.Name }).ToArrayAsync(ct),
            routingTeamId = UnderwritingRuntimeConfiguration.Parse(runtime.Values)?.RoutingTeamId,
            requests = await AdministrationRequests.List(db, "authority", ct) };
        await tx.CommitAsync(ct); return result;
    }

    public Task<CommandOutcome> ProposeAsync(ActorContext actor, AuthorityProposal input, string key, CancellationToken ct = default)
        => Command(actor, "/api/v1/admin/authority/requests", key, input, async (db, token) =>
        {
            AdminAccess.Text(input.Reason, 1000);
            if (input.Limits.ValueKind != JsonValueKind.Object || input.Limits.GetRawText().Length > 65536)
                throw new QuoteOperationException(400, "authority-input-invalid");
            var source = await Source(db, input.SourceAuthorityId, token); AdminAccess.Version(source, input.SourceEtag);
            var runtime = await Runtime(db, token);
            var version = "admin-" + Guid.NewGuid().ToString("N")[..16];
            var node = JsonNode.Parse(source.DefinitionJson)!.AsObject(); node["version"] = version;
            node["effectiveFrom"] = input.EffectiveFrom; node["effectiveTo"] = input.EffectiveTo;
            node["limits"] = JsonNode.Parse(input.Limits.GetRawText());
            var definition = node.ToJsonString();
            var binder = await Validate(db, source, runtime, input, definition, token);
            var row = AdministrationRequests.Create(db, actor, "authority",
                new AuthorityRequestData(input, runtime.Id, AdminAccess.Etag(binder.RowVersion), definition, version), time.GetUtcNow());
            AdminAccess.Audit(db, actor, row.Id, "administration.authority-requested", input.Reason,
                new { sourceId = source.Id, source.DefinitionJson, runtimeId = runtime.Id }, new { input, definition }, time.GetUtcNow());
            return Outcome(row.Id, AdministrationRequests.View(row), 201);
        }, ct);

    public Task<CommandOutcome> DecideAsync(ActorContext actor, Guid id, string etag, bool approve, string reason, string key, CancellationToken ct = default)
        => Command(actor, $"/api/v1/admin/authority/requests/{id}/decision", key, new { etag, approve, reason }, async (db, token) =>
        {
            AdminAccess.Text(reason, 1000);
            var row = await AdministrationRequests.Hold(db, id, etag, "authority", token);
            var request = AdministrationRequests.View(row);
            if (request.RequestedBy == actor.UserId) throw new QuoteOperationException(403, "independent-approval-required");
            if (approve)
            {
                await AdminAccess.Authorize(db, new ActorContext(request.RequestedBy, null, null, new HashSet<string>()), token);
                var proposed = request.Proposal.Deserialize<AuthorityRequestData>(Json)!;
                var source = await Source(db, proposed.Input.SourceAuthorityId, token); AdminAccess.Version(source, proposed.Input.SourceEtag);
                var runtime = await Runtime(db, token);
                if (runtime.Id != proposed.RuntimeId) throw new QuoteOperationException(409, "authority-request-stale");
                var binder = await Validate(db, source, runtime, proposed.Input, proposed.Definition, token);
                AdminAccess.Version(binder, proposed.BinderEtag);
                var input = proposed.Input;
                var authority = new AuthorityVersion { ProductId = source.ProductId, ProductVersionId = input.ProductVersionId,
                    BinderVersionId = binder.Id, Version = proposed.Version, EffectiveFrom = input.EffectiveFrom, EffectiveTo = input.EffectiveTo,
                    DefinitionJson = proposed.Definition, CreatedBy = actor.UserId, CreatedAt = time.GetUtcNow(), UpdatedAt = time.GetUtcNow() };
                db.Add(authority); await db.SaveChangesAsync(token);
                foreach (var user in input.UserIds)
                    db.Add(new UserAuthorityGrant { UserId = user, AuthorityVersionId = authority.Id,
                        EffectiveFrom = input.EffectiveFrom, EffectiveTo = input.EffectiveTo, GrantedBy = actor.UserId,
                        CreatedBy = actor.UserId, CreatedAt = time.GetUtcNow(), UpdatedAt = time.GetUtcNow(), Reason = reason });
                var current = UnderwritingRuntimeConfiguration.Parse(runtime.Values)!;
                var sourcePin = current.Products[source.ProductVersionId];
                var values = current.Products.Values.Where(x => x.ProductVersionId != input.ProductVersionId)
                    .Append(new UnderwritingRuntimeProduct(input.ProductVersionId, sourcePin.RatingRuleVersionId, binder.Id, authority.Id));
                db.Add(new SettingVersion { Scope = runtime.Scope, Version = runtime.Version + 1,
                    EffectiveFrom = input.EffectiveFrom > time.GetUtcNow() ? input.EffectiveFrom : time.GetUtcNow(), CreatedBy = actor.UserId,
                    Values = JsonSerializer.Serialize(new { demo = true, kind = "underwriting-runtime", schemaVersion = "1",
                        scenarioVersionId = current.ScenarioVersionId, routingTeamId = input.RoutingTeamId, products = values }, Json) });
                AdminAccess.Audit(db, actor, authority.Id, "administration.authority-published", reason,
                    new { sourceId = source.Id, runtimeId = runtime.Id }, new { authority.Id, requestId = id, input }, time.GetUtcNow());
            }
            var decision = AdministrationRequests.Decide(db, row, actor, approve ? "approved" : "rejected", reason, time.GetUtcNow());
            AdminAccess.Audit(db, actor, id, "administration.authority-decision", reason, request, AdministrationRequests.View(decision), time.GetUtcNow());
            return Outcome(id, AdministrationRequests.View(decision));
        }, ct);

    public Task<CommandOutcome> RevokeAsync(ActorContext actor, Guid id, string etag, string reason, string key, CancellationToken ct = default)
        => Command(actor, $"/api/v1/admin/authority/grants/{id}/revoke", key, new { etag, reason }, async (db, token) =>
        {
            AdminAccess.Text(reason, 1000);
            var row = await db.Set<UserAuthorityGrant>().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new QuoteOperationException(404, "authority-grant-not-found");
            AdminAccess.Version(row, etag);
            if (row.RevokedAt != null) throw new QuoteOperationException(409, "authority-grant-revoked");
            row.RevokedAt = time.GetUtcNow(); row.RevokedBy = actor.UserId; row.RevocationReason = reason; row.UpdatedAt = time.GetUtcNow();
            AdminAccess.Audit(db, actor, id, "administration.authority-revoked", reason, new { row.UserId, row.AuthorityVersionId }, new { row.RevokedAt }, time.GetUtcNow());
            return Outcome(id, new { id });
        }, ct);

    private async Task<BinderVersion> Validate(BackOfficeDbContext db, AuthorityVersion source, SettingVersion runtime,
        AuthorityProposal input, string definition, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (input.EffectiveFrom.Offset != TimeSpan.Zero || input.EffectiveTo.Offset != TimeSpan.Zero || input.EffectiveFrom >= input.EffectiveTo ||
            input.EffectiveTo <= now || input.UserIds is null || input.UserIds.Length > 100 || input.UserIds.Distinct().Count() != input.UserIds.Length)
            throw new QuoteOperationException(400, "authority-input-invalid");
        var product = await db.Set<Product>().SingleAsync(x => x.Id == source.ProductId, ct);
        var target = await db.Set<ProductVersion>().SingleOrDefaultAsync(x => x.Id == input.ProductVersionId, ct);
        var binder = await db.Set<BinderVersion>().SingleAsync(x => x.Id == source.BinderVersionId, ct);
        var configured = UnderwritingRuntimeConfiguration.Parse(runtime.Values);
        if (configured is null || !configured.Products.TryGetValue(source.ProductVersionId, out var pin) ||
            configured.Products.Count >= 32 && !configured.Products.ContainsKey(input.ProductVersionId) ||
            runtime.EffectiveFrom > now || target is null || target.State != "published" || target.ProductId != source.ProductId || target.ProviderId != binder.ProviderId ||
            target.EffectiveFrom > input.EffectiveFrom || target.EffectiveTo < input.EffectiveTo || binder.State != "published" || source.State != "published" ||
            pin.BinderVersionId != binder.Id || !await db.Set<CapacityProvider>().AnyAsync(x => x.Id == binder.ProviderId && x.State == "active", ct))
            throw new QuoteOperationException(409, "authority-scope-incompatible");
        var rating = await db.Set<RatingRuleVersion>().SingleAsync(x => x.Id == pin.RatingRuleVersionId, ct);
        if (rating.State != "published" || rating.ProductId != source.ProductId || rating.EffectiveFrom > input.EffectiveFrom || rating.EffectiveTo < input.EffectiveTo)
            throw new QuoteOperationException(409, "authority-rating-window-unavailable");
        using var authorityJson = JsonDocument.Parse(definition); using var binderJson = JsonDocument.Parse(binder.DefinitionJson);
        var within = product.Code == CommercialCaptureRules.ProductCode
            ? CommercialUnderwritingConfiguration.WithinBinder(authorityJson.RootElement, binderJson.RootElement)
            : UnderwritingConfiguration.WithinBinder(authorityJson.RootElement, binderJson.RootElement);
        if (!within) throw new QuoteOperationException(400, "authority-outside-binder");
        if (!await db.Set<Team>().AnyAsync(x => x.Id == input.RoutingTeamId, ct)) throw new QuoteOperationException(400, "referral-team-unavailable");
        foreach (var userId in input.UserIds.Order())
        {
            var user = await IdentitySnapshot.Lock(db, new(userId, null), ct);
            if (user is null || !new ActorContext(userId, user.User.TeamId, null, user.Roles.Select(x => x.Code).ToHashSet()).HasCapability("underwriting-decide-within-authority"))
                throw new QuoteOperationException(400, "authority-user-unavailable");
            if (await db.Set<UserAuthorityGrant>().AnyAsync(x => x.UserId == userId && x.RevokedAt == null &&
                x.EffectiveFrom < input.EffectiveTo && x.EffectiveTo > input.EffectiveFrom &&
                db.Set<AuthorityVersion>().Any(a => a.Id == x.AuthorityVersionId && a.ProductVersionId == input.ProductVersionId && a.BinderVersionId == binder.Id), ct))
                throw new QuoteOperationException(409, "authority-grant-overlap");
        }
        return binder;
    }
    private static async Task<AuthorityVersion> Source(BackOfficeDbContext db, Guid id, CancellationToken ct)
        => await db.Set<AuthorityVersion>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new QuoteOperationException(404, "authority-not-found");
    private static async Task<SettingVersion> Runtime(BackOfficeDbContext db, CancellationToken ct)
        => await db.Set<SettingVersion>().Where(x => x.Scope == "underwriting-runtime").OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
            ?? throw new QuoteOperationException(409, "underwriting-runtime-unavailable");
    private Task<CommandOutcome> Command<T>(ActorContext actor, string route, string key, T body,
        Func<BackOfficeDbContext,CancellationToken,Task<CommandOutcome>> write, CancellationToken ct)
        => commands.ExecuteAuthorizedAsync(new(actor.UserId, route, key, Guid.NewGuid()), body, "administration.authority-command",
            (db, token) => AdminAccess.Authorize(db, actor, token), write, ct, IsolationLevel.Serializable);
    private static CommandOutcome Outcome(Guid id, object body, int status = 200) => new(id, status, JsonSerializer.Serialize(body, Json));
}
