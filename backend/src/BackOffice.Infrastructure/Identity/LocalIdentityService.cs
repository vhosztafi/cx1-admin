using System.Security.Claims;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Identity;

public sealed record ActorView(Guid Id,string DisplayName,string Email,string[] Roles,bool MfaEnabled);
public sealed record LocalIdentity(ActorView View,ClaimsPrincipal Principal);

public sealed class LocalIdentityService(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public const string StampClaim = "cover:security-stamp";
    public const string TeamClaim = "cover:team";
    private static readonly PasswordHasher<StaffUser> Hasher = new();
    private static readonly string DummyHash = Hasher.HashPassword(new StaffUser(),Guid.NewGuid().ToString("N"));

    public async Task<LocalIdentity?> AuthenticateAsync(string email,string password,CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var userId = await db.Set<UserCredential>().AsNoTracking().Where(x => x.Provider == "local" && x.ProviderSubject == normalized).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(cancellationToken);
        var reference = userId is Guid id ? await IdentitySnapshot.Reference(db,id,cancellationToken) : null;
        if (reference == null) { Hasher.VerifyHashedPassword(new StaffUser(),DummyHash,password); return null; }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var snapshot = await IdentitySnapshot.Lock(db,reference,cancellationToken);
        var user = snapshot?.User;
        var credential = await db.Set<UserCredential>().FromSqlInterpolated($"SELECT * FROM [UserCredential] WITH (UPDLOCK,HOLDLOCK) WHERE [Provider] = 'local' AND [ProviderSubject] = {normalized} AND [UserId] = {reference.UserId}").SingleOrDefaultAsync(cancellationToken);
        var now = time.GetUtcNow();
        var verification = Hasher.VerifyHashedPassword(user ?? new StaffUser(),credential?.PasswordHash ?? DummyHash,password);
        if (credential is null || user is null) return null;
        if (credential.LockedUntil > now || user.State != "active" || user.AgencyId is not null || credential.MustReset || credential.MfaSecretCiphertext is not null)
            return null; // MFA accounts cannot bypass their second factor while its flow is unimplemented.
        if (verification == PasswordVerificationResult.Failed)
        {
            if (credential.LockedUntil is not null) credential.FailedAttempts=0;
            credential.LockedUntil=null;
            credential.FailedAttempts++;
            if (credential.FailedAttempts >= 5) credential.LockedUntil=now.AddMinutes(15);
            db.Add(AuthenticationAudit(user.Id,"authentication.failed",now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        credential.FailedAttempts=0; credential.LockedUntil=null;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded) credential.PasswordHash=Hasher.HashPassword(user,password);
        var roles = snapshot!.Roles;
        // Foundation has internal identities only; mixed/external role sets fail closed.
        if (roles.Any(x => x.Scope != "internal")) return null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new LocalIdentity(View(user,roles),Principal(user,roles));
    }

    public async Task<ActorView?> GetActorAsync(Guid userId,CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var reference = await IdentitySnapshot.Reference(db,userId,cancellationToken);
        if (reference == null) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var snapshot = await IdentitySnapshot.Lock(db,reference,cancellationToken);
        if (snapshot == null || snapshot.User.AgencyId != null) return null;
        var result = View(snapshot.User,snapshot.Roles);
        await transaction.CommitAsync(cancellationToken); return result;
    }

    public static ActorContext Actor(ClaimsPrincipal principal) => new(Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
        Guid.TryParse(principal.FindFirstValue(TeamClaim),out var team) ? team : null,null,
        principal.FindAll(ClaimTypes.Role).Select(x => x.Value).ToHashSet(StringComparer.Ordinal));

    internal static Task<List<Role>> RolesAsync(BackOfficeDbContext db,Guid userId,CancellationToken cancellationToken) =>
        (from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId == userId select role).AsNoTracking().ToListAsync(cancellationToken);
    internal static ClaimsPrincipal Principal(StaffUser user,IEnumerable<Role> roles)
    {
        var claims = new List<Claim> {new(ClaimTypes.NameIdentifier,user.Id.ToString()),new(ClaimTypes.Name,user.DisplayName),new(StampClaim,user.SecurityStamp)};
        if (user.TeamId is Guid team) claims.Add(new(TeamClaim,team.ToString()));
        claims.AddRange(roles.Select(x => new Claim(ClaimTypes.Role,x.Code)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme));
    }
    private static ActorView View(StaffUser user,IEnumerable<Role> roles) => new(user.Id,user.DisplayName,user.Email,roles.Select(x => x.Code).Order().ToArray(),false);
    internal static AuditEvent AuthenticationAudit(Guid userId,string kind,DateTimeOffset now) => new()
    {
        ActorId=userId,EventType=kind,OccurredAt=now,CorrelationId=Guid.NewGuid(),After=JsonSerializer.Serialize(new {userId})
    };
}
