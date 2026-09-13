using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Identity;

public sealed class SqlTicketStore(IDbContextFactory<BackOfficeDbContext> factory,IDataProtectionProvider protection,TimeProvider time) : ITicketStore
{
    private readonly IDataProtector protector = protection.CreateProtector("CoverMGA.SqlSessionTicket.v1");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var userId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Id == userId);
        if (user.State != "active" || user.SecurityStamp != ticket.Principal.FindFirstValue(LocalIdentityService.StampClaim))
            throw new InvalidOperationException("Account changed during authentication.");
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = time.GetUtcNow();
        db.Add(new UserSession {UserId=userId,TokenHash=Hash(key),ExpiresAt=ticket.Properties.ExpiresUtc ?? now.AddHours(8),
            LastSeenAt=now,DeviceLabel="Back office browser",SecurityStamp=user.SecurityStamp,
            TicketCiphertext=protector.Protect(TicketSerializer.Default.Serialize(ticket))});
        db.Add(LocalIdentityService.AuthenticationAudit(userId,"authentication.succeeded",now));
        await db.SaveChangesAsync();
        return key;
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (key.Length != 43) return null;
        await using var db = await factory.CreateDbContextAsync();
        var hash = Hash(key);
        var now = time.GetUtcNow();
        var session = await db.Set<UserSession>().AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now) return null;
        var user = await db.Set<StaffUser>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == session.UserId);
        if (user is null || user.State != "active" || user.SecurityStamp != session.SecurityStamp) return null;
        var roles = await LocalIdentityService.RolesAsync(db,user.Id,CancellationToken.None);
        if (roles.Any(x => x.Scope != "internal")) return null;
        AuthenticationTicket? ticket;
        try { ticket = TicketSerializer.Default.Deserialize(protector.Unprotect(session.TicketCiphertext)); }
        catch (CryptographicException) { return null; }
        if (ticket is null || ticket.Properties.ExpiresUtc <= now) return null;
        ticket = new AuthenticationTicket(LocalIdentityService.Principal(user,roles),ticket.Properties,ticket.AuthenticationScheme);
        // Avoid rewriting encrypted tickets/rowversions on every asset/API fetch.
        if (session.LastSeenAt < now.AddMinutes(-1))
            await db.Set<UserSession>().Where(x => x.Id == session.Id && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt,now).SetProperty(x => x.UpdatedAt,now));
        return ticket;
    }

    public async Task RenewAsync(string key,AuthenticationTicket ticket)
    {
        // Fixed maximum lifetime; middleware is configured without sliding expiry.
        await using var db = await factory.CreateDbContextAsync();
        var hash = Hash(key);
        var encrypted = protector.Protect(TicketSerializer.Default.Serialize(ticket));
        var now = time.GetUtcNow();
        await db.Set<UserSession>().Where(x => x.TokenHash == hash && x.RevokedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TicketCiphertext,encrypted).SetProperty(x => x.UpdatedAt,now));
    }

    public async Task RemoveAsync(string key)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var hash = Hash(key);
        var session = await db.Set<UserSession>().AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (session is null) return;
        var now = time.GetUtcNow();
        var changed = await db.Set<UserSession>().Where(x => x.Id == session.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt,now).SetProperty(x => x.UpdatedAt,now));
        if (changed > 0)
        {
            db.Add(LocalIdentityService.AuthenticationAudit(session.UserId,"authentication.session-revoked",now));
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
    }

    private static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));
}
