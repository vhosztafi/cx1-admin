using System.Data;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Agencies;

public static class AgencyActionProjection
{
    public static async Task<IReadOnlyDictionary<Guid, AgencyActionCounts>> Read(BackOfficeDbContext db, Guid[]? agencyIds, DateOnly today, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Action counts require a serializable transaction through the directory/KPI read.");
        var agencies = db.Set<Agency>().AsNoTracking().Select(x => x.Id);
        if (agencyIds != null) agencies = agencies.Where(id => agencyIds.Contains(id));
        var states = await db.Set<AgencyStateRequest>().Where(x => agencies.Contains(x.AgencyId) && x.State == "pending").GroupBy(x => x.AgencyId).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, token);
        var terms = await db.Set<AgencyTermsRequest>().Where(x => agencies.Contains(x.AgencyId) && x.State == "pending").GroupBy(x => x.AgencyId).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, token);
        var permissions = await db.Set<AgencyPermissionRequest>().Where(x => agencies.Contains(x.AgencyId) && x.State == "pending").GroupBy(x => x.AgencyId).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, token);
        var followUps = await db.Set<AgencyFollowUp>().Where(x => agencies.Contains(x.AgencyId) && x.DueOn <= today).GroupBy(x => x.AgencyId).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, token);
        return states.Keys.Concat(terms.Keys).Concat(permissions.Keys).Concat(followUps.Keys).Distinct().ToDictionary(id => id,
            id => new AgencyActionCounts(states.GetValueOrDefault(id), terms.GetValueOrDefault(id), permissions.GetValueOrDefault(id), followUps.GetValueOrDefault(id)));
    }
}
