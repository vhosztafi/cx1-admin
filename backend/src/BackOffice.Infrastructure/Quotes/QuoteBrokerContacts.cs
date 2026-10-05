using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public sealed record QuoteBrokerContact(string Key,string Name,string? Email);
public static class QuoteBrokerContacts
{
    // Called only after the selected relationship/agency and current actor are held.
    public static async Task<QuoteBrokerContact[]> ListAsync(BackOfficeDbContext db,Guid agencyId,CancellationToken token)
    {
        var rows=await db.Set<StaffUser>().AsNoTracking().Where(x=>x.AgencyId==agencyId && x.State=="active" &&
            db.Set<UserRole>().Any(link=>link.UserId==x.Id && db.Set<Role>().Any(role=>role.Id==link.RoleId && role.Scope=="agency" && (role.Code=="broker-admin" || role.Code=="broker-user" || role.Code=="broker-readonly"))))
            .OrderBy(x=>x.DisplayName).Select(x=>new QuoteBrokerContact(x.Id.ToString(),x.DisplayName,x.Email)).ToListAsync(token);
        var details=await db.Set<AgencyOnboarding>().AsNoTracking().Where(x=>x.AgencyId==agencyId).Select(x=>x.Details).SingleOrDefaultAsync(token);
        if(details is not null)
        {
            using var document=JsonDocument.Parse(details);
            if(document.RootElement.TryGetProperty("mainContact",out var contact) && contact.ValueKind==JsonValueKind.Object && contact.TryGetProperty("name",out var name) && name.ValueKind==JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                rows.Insert(0,new("main-contact",name.GetString()!,contact.TryGetProperty("email",out var email) && email.ValueKind==JsonValueKind.String ? email.GetString() : null));
        }
        return rows.ToArray();
    }
}
