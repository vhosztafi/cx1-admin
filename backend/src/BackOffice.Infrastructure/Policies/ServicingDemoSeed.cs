using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingDemoDraft(string Scenario, Guid PolicyId, Guid DraftId);

public sealed class ServicingDemoSeed(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public async Task<IReadOnlyList<ServicingDemoDraft>> SeedDraftsAsync(ActorContext actor, Guid policyId, CancellationToken token = default)
    {
        // Authorize even repeat runs. Normal command services create every draft
        // and lease; this helper never writes policy, decision or finance rows.
        var policy = await new PolicyReadService(factory, time).ReadAsync(actor, policyId, token: token);
        if (!policy.TryGetValue("termId", out var selected) || selected is not Guid termId)
            throw new QuoteOperationException(409, "servicing-demo-covered-term-required");
        await using var db = await factory.CreateDbContextAsync(token);
        await db.Database.OpenConnectionAsync(token);
        var resource = $"CoverMGA.ServicingDemo:{policyId:D}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=10000; IF @result<0 THROW 51610,'Servicing demo seed busy.',1;", token);
        try
        {
            var versions = await db.Set<PolicyVersion>().AsNoTracking().Where(x => x.PolicyId == policyId && x.TermId == termId)
                .OrderByDescending(x => x.Sequence).ThenByDescending(x => x.SliceOrdinal).ToArrayAsync(token);
            var basis = versions.First();
            var local = TimeZoneInfo.ConvertTime(basis.EffectiveAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/London"));
            var intent = JsonSerializer.SerializeToElement(new { localDate = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                localTime = local.ToString("HH:mm", CultureInfo.InvariantCulture), timeZone = "Europe/London" });
            var scenarios = new List<(string Name, Guid Basis, bool Lease)> { ("editable", basis.Id, false), ("leased", basis.Id, true) };
            if (versions.Length > 1) scenarios.Add(("historical-base", versions.Last().Id, false));
            var service = new ServicingDraftService(factory, time);
            var result = new List<ServicingDemoDraft>();
            foreach (var scenario in scenarios)
            {
                var reason = $"Fictional servicing demo v1: {scenario.Name}";
                var owned = await (from revision in db.Set<ServicingRevision>().FromSqlInterpolated($"SELECT * FROM ServicingRevision WHERE Sequence=1 AND JSON_VALUE(ProposalJson,'$.reason')={reason}").AsNoTracking()
                    join draft in db.Set<ServicingDraft>() on revision.DraftId equals draft.Id
                    where draft.PolicyId == policyId select draft.Id).ToArrayAsync(token);
                if (owned.Length > 1) throw new InvalidOperationException("Duplicate servicing demo scenario.");
                var key = $"servicing-demo-v1:{policyId:D}:{scenario.Name}";
                Guid draftId;
                if (owned.Length == 1) draftId = owned[0];
                else
                {
                    var listed = await service.ListAsync(actor, termId, token);
                    var created = await service.CreateAsync(actor, termId, Version(listed.Etag),
                        new("cancellation", scenario.Basis, intent, reason), key + ":create", Guid.NewGuid(), token);
                    draftId = created.ResourceId;
                }
                var saved = await service.ReadAsync(actor, draftId, token);
                using var body = JsonDocument.Parse(saved.Body);
                // Resume an interrupted initial acquisition, but never renew an
                // expired lease, steal a user's lease or reopen an abandoned draft.
                if (scenario.Lease && body.RootElement.GetProperty("state").GetString() == "draft" &&
                    body.RootElement.GetProperty("lease").ValueKind == JsonValueKind.Null)
                    await service.LeaseAsync(actor, draftId, Version(saved.Etag), "acquire", null, null, key + ":lease", Guid.NewGuid(), token);
                result.Add(new(scenario.Name, policyId, draftId));
            }
            return result;
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource={resource},@LockOwner='Session';", CancellationToken.None);
        }
    }

    private static byte[] Version(string etag) => Convert.FromBase64String(etag.Trim('"'));
}
