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
        var reference = await IdentitySnapshot.Reference(db,userId) ?? throw new InvalidOperationException("Account changed during authentication.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var snapshot = await IdentitySnapshot.Lock(db,reference);
        var user = snapshot?.User ?? throw new InvalidOperationException("Account changed during authentication.");
        if (!snapshot.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal).SetEquals(ticket.Principal.FindAll(ClaimTypes.Role).Select(x => x.Value)))
            throw new InvalidOperationException("Account roles changed during authentication.");
        if (!LocalIdentityService.ScopeMatches(ticket.Principal,user) || user.State != "active" || user.SecurityStamp != ticket.Principal.FindFirstValue(LocalIdentityService.StampClaim))
            throw new InvalidOperationException("Account changed during authentication.");
        var credential=await db.Set<UserCredential>().AsNoTracking().SingleAsync(x=>x.UserId==userId&&x.Provider=="local");
        if(credential.MustReset||credential.MfaSecretCiphertext!=null&&!ticket.Principal.HasClaim("amr","mfa"))throw new InvalidOperationException("Required second factor is not verified.");
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = time.GetUtcNow();
        var sessionId=Guid.NewGuid();var ticketIdentity=(ClaimsIdentity)ticket.Principal.Identity!;
        foreach(var prior in ticketIdentity.FindAll("cover:session").ToArray())ticketIdentity.RemoveClaim(prior);
        ticketIdentity.AddClaim(new("cover:session",sessionId.ToString()));
        db.Add(new UserSession {Id=sessionId,UserId=userId,TokenHash=Hash(key),CreatedAt=now,UpdatedAt=now,ExpiresAt=ticket.Properties.ExpiresUtc ?? now.AddHours(8),
            LastSeenAt=now,DeviceLabel="Back office browser",SecurityStamp=user.SecurityStamp,
            TicketCiphertext=protector.Protect(TicketSerializer.Default.Serialize(ticket))});
        db.Add(LocalIdentityService.AuthenticationAudit(userId,"authentication.succeeded",now));
        await db.SaveChangesAsync();
        await transaction.CommitAsync(); return key;
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (key.Length != 43) return null;
        await using var db = await factory.CreateDbContextAsync();
        var hash = Hash(key);
        var now = time.GetUtcNow();
        var sessionReference = await db.Set<UserSession>().AsNoTracking().Where(x => x.TokenHash == hash).Select(x => new { x.Id, x.UserId }).SingleOrDefaultAsync();
        if (sessionReference == null) return null;
        var reference = await IdentitySnapshot.Reference(db,sessionReference.UserId);
        if (reference == null) return null;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var snapshot = await IdentitySnapshot.Lock(db,reference);
        if (snapshot == null) return null;
        var user = snapshot.User; var roles = snapshot.Roles;
        var session = await db.Set<UserSession>().FromSqlInterpolated($"SELECT * FROM [Session] WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={sessionReference.Id} AND UserId={user.Id} AND TokenHash={hash}").AsNoTracking().SingleOrDefaultAsync();
        if (session == null || session.RevokedAt != null || session.ExpiresAt <= now || user.SecurityStamp != session.SecurityStamp) return null;
        AuthenticationTicket? ticket;
        try { ticket = TicketSerializer.Default.Deserialize(protector.Unprotect(session.TicketCiphertext)); }
        catch (CryptographicException) { return null; }
        if (ticket is null || !LocalIdentityService.ScopeMatches(ticket.Principal,user) || ticket.Properties.ExpiresUtc <= now ||
            ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier) != user.Id.ToString() ||
            ticket.Principal.FindFirstValue(LocalIdentityService.StampClaim) != session.SecurityStamp) return null;
        var credential=await db.Set<UserCredential>().AsNoTracking().SingleAsync(x=>x.UserId==user.Id&&x.Provider=="local");
        if(credential.MustReset||credential.MfaSecretCiphertext!=null&&!ticket.Principal.HasClaim("amr","mfa"))return null;
        var principal=LocalIdentityService.Principal(user,roles);var claims=(ClaimsIdentity)principal.Identity!;
        claims.AddClaim(new("cover:session",session.Id.ToString()));if(ticket.Principal.HasClaim("amr","mfa"))claims.AddClaim(new("amr","mfa"));
        ticket = new AuthenticationTicket(principal,ticket.Properties,ticket.AuthenticationScheme);
        // Avoid rewriting encrypted tickets/rowversions on every asset/API fetch.
        if (session.LastSeenAt < now.AddMinutes(-1))
            await db.Set<UserSession>().Where(x => x.Id == session.Id && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt,now).SetProperty(x => x.UpdatedAt,now));
        await transaction.CommitAsync(); return ticket;
    }

    public async Task RenewAsync(string key,AuthenticationTicket ticket)
    {
        // Renewal never extends the persisted maximum lifetime.
        if (key.Length != 43) return;
        await using var db = await factory.CreateDbContextAsync();
        var hash = Hash(key);
        var userId = await db.Set<UserSession>().AsNoTracking().Where(x => x.TokenHash == hash).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync();
        var reference = userId is Guid id ? await IdentitySnapshot.Reference(db,id) : null;
        if (reference == null) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var snapshot = await IdentitySnapshot.Lock(db,reference);
        if (snapshot == null || !LocalIdentityService.ScopeMatches(ticket.Principal,snapshot.User) || ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier) != snapshot.User.Id.ToString() ||
            ticket.Principal.FindFirstValue(LocalIdentityService.StampClaim) != snapshot.User.SecurityStamp ||
            !snapshot.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal).SetEquals(ticket.Principal.FindAll(ClaimTypes.Role).Select(x => x.Value))) return;
        var encrypted = protector.Protect(TicketSerializer.Default.Serialize(ticket)); var now = time.GetUtcNow();
        await db.Set<UserSession>().Where(x => x.TokenHash == hash && x.UserId == snapshot.User.Id && x.SecurityStamp == snapshot.User.SecurityStamp && x.RevokedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TicketCiphertext,encrypted).SetProperty(x => x.UpdatedAt,now));
        await transaction.CommitAsync();
    }

    public async Task RemoveAsync(string key)
    {
        await using var db = await factory.CreateDbContextAsync();
        var hash = Hash(key);
        var session = await db.Set<UserSession>().AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (session is null) return;
        var reference = await IdentitySnapshot.Reference(db,session.UserId);
        if (reference == null) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Removal remains allowed for a now-disabled identity, but follows the
        // same lock order before revoking its session and appending the audit.
        await IdentitySnapshot.Lock(db,reference);
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
