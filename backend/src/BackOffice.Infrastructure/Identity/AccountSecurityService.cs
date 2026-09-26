using System.Security.Claims;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Administration;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Identity;
public sealed record AccountIdentity(Guid UserId,Guid? AgencyId,string Stamp,Guid? SessionId);
public sealed record AccountProof(string Password,string? Code);
public sealed record AccountProfile(string DisplayName,string Etag);
public sealed partial class AccountSecurityService(IDbContextFactory<BackOfficeDbContext> factory,IdentitySecrets secrets,TimeProvider time)
{
    private static readonly PasswordHasher<StaffUser> Hasher=new();
    public static AccountIdentity Identity(ClaimsPrincipal p)=>new(Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!),
        Guid.TryParse(p.FindFirstValue(LocalIdentityService.AgencyClaim),out var agency)?agency:null,p.FindFirstValue(LocalIdentityService.StampClaim)??"",
        Guid.TryParse(p.FindFirstValue("cover:session"),out var session)?session:null);
    private async Task<StaffUser> Own(BackOfficeDbContext db,AccountIdentity actor,CancellationToken ct)
    {
        var snapshot=await IdentitySnapshot.Lock(db,new(actor.UserId,actor.AgencyId),ct);
        if(snapshot==null||snapshot.User.SecurityStamp!=actor.Stamp)throw Denied();
        if(actor.SessionId is Guid session&&!await db.Set<UserSession>().AnyAsync(x=>x.Id==session&&x.UserId==actor.UserId&&x.RevokedAt==null&&x.ExpiresAt>time.GetUtcNow()&&x.SecurityStamp==actor.Stamp,ct))throw Denied();
        var user=await IdentitySecrets.HoldUser(db,actor.UserId,ct);
        var credential=await Credential(db,user.Id,ct);if(credential.MustReset)throw Denied();return user;
    }
    private static Task<UserCredential> Credential(BackOfficeDbContext db,Guid id,CancellationToken ct)
        =>db.Set<UserCredential>().FromSqlInterpolated($"SELECT * FROM UserCredential WITH(UPDLOCK,HOLDLOCK) WHERE UserId={id} AND Provider='local'").SingleAsync(ct);
    public async Task<object> ReadAsync(AccountIdentity actor,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Own(db,actor,ct);var credential=await Credential(db,user.Id,ct);
        var sessions=await db.Set<UserSession>().AsNoTracking().Where(x=>x.UserId==user.Id&&x.RevokedAt==null&&x.ExpiresAt>time.GetUtcNow()).OrderByDescending(x=>x.CreatedAt).Select(x=>new{x.Id,x.DeviceLabel,x.CreatedAt,x.LastSeenAt,x.ExpiresAt,current=x.Id==actor.SessionId}).ToArrayAsync(ct);
        var requests=actor.AgencyId==null?(await AdministrationRequests.List(db,"user",ct)).Where(x=>x.RequestedBy==actor.UserId).ToArray():[];
        var teams=actor.AgencyId==null?await db.Set<Team>().OrderBy(x=>x.Name).Select(x=>new{x.Id,x.Name}).ToArrayAsync(ct):[];
        var roles=actor.AgencyId==null?await db.Set<Role>().Where(x=>x.Scope=="internal").Select(x=>x.Code).ToArrayAsync(ct):[];
        var currentRoles=await LocalIdentityService.RolesAsync(db,user.Id,ct);
        var reports=await db.Set<AuditEvent>().AsNoTracking().Where(x=>x.ActorId==user.Id&&x.EventType=="security.activity-reported").OrderByDescending(x=>x.OccurredAt).Take(20).Select(x=>new{x.Id,x.OccurredAt,x.Reason}).ToArrayAsync(ct);
        var result=new{user.Id,user.DisplayName,user.Email,user.TeamId,etag=AdminAccess.Etag(user.RowVersion),mfaEnabled=credential.MfaSecretCiphertext!=null,sessions,requests,teams,roles,currentRoles=currentRoles.Select(x=>x.Code),reports};await tx.CommitAsync(ct);return result;
    }
    public async Task ProfileAsync(AccountIdentity actor,AccountProfile input,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Own(db,actor,ct);AdminAccess.Version(user,input.Etag);user.DisplayName=AdminAccess.Text(input.DisplayName);Audit(db,user,"account.profile-updated");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task RequestIdentityAsync(AccountIdentity actor,string email,Guid teamId,string[] roles,string reason,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Own(db,actor,ct);if(user.AgencyId!=null)throw Denied();
        var input=new UserChange(user.Id,AdminAccess.Etag(user.RowVersion),email,teamId,roles,false,AdminAccess.Text(reason,1000),true);
        if(!System.Net.Mail.MailAddress.TryCreate(email,out var parsed)||parsed.Address!=email||email.Length>254||roles==null||roles.Length is <1 or >10||roles.Distinct().Count()!=roles.Length||await db.Set<Role>().CountAsync(x=>x.Scope=="internal"&&roles.Contains(x.Code),ct)!=roles.Length||!await db.Set<Team>().AnyAsync(x=>x.Id==teamId,ct))throw Invalid();
        AdministrationRequests.Create(db,new(user.Id,user.TeamId,null,new HashSet<string>()),"user",input,time.GetUtcNow());Audit(db,user,"account.identity-change-requested");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task PasswordAsync(AccountIdentity actor,AccountProof proof,string newPassword,CancellationToken ct=default)
    {
        Password(newPassword);await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Own(db,actor,ct);var credential=await Credential(db,user.Id,ct);
        if(!await Proof(db,user,credential,proof,true,ct)){await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);throw Denied();}
        credential.PasswordHash=Hasher.HashPassword(user,newPassword);credential.MustReset=false;await IdentitySecrets.Invalidate(db,user,time.GetUtcNow(),ct);Audit(db,user,"account.password-changed");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task ForgotAsync(string email,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(email)||email.Length>254)return;
        await using var db=await factory.CreateDbContextAsync(ct);var normalized=email.Trim().ToUpperInvariant();var hint=await db.Set<StaffUser>().AsNoTracking().SingleOrDefaultAsync(x=>x.NormalizedEmail==normalized&&x.AgencyId==null,ct);if(hint==null)return;
        await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await IdentitySecrets.HoldUser(db,hint.Id,ct);if(user.State!="active"||user.AgencyId!=null)return;
        // A public request cannot revoke access or expose the token. Repeated requests
        // during the delivery lifetime reuse the existing delivery to avoid abuse.
        if(!await db.Set<IdentityAction>().AnyAsync(x=>x.UserId==user.Id&&x.Kind=="password-reset"&&x.ConsumedAt==null&&x.ExpiresAt>time.GetUtcNow()&&x.SecurityStamp==user.SecurityStamp,ct))
        {await secrets.Issue(db,user,"password-reset",TimeSpan.FromMinutes(30),true,ct);Audit(db,user,"account.password-reset-requested");await db.SaveChangesAsync(ct);}
        await tx.CommitAsync(ct);
    }
    public async Task<bool> ResetAsync(string token,string newPassword,string? code,CancellationToken ct=default)
    {
        Password(newPassword);if(token is null||token.Length!=43)return false;await using var db=await factory.CreateDbContextAsync(ct);var hash=IdentitySecrets.Hash(token);var hint=await db.Set<IdentityAction>().AsNoTracking().SingleOrDefaultAsync(x=>x.TokenHash==hash&&x.Kind=="password-reset",ct);if(hint==null)return false;
        await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await IdentitySecrets.HoldUser(db,hint.UserId,ct);var credential=await Credential(db,user.Id,ct);var action=await Action(db,hint.Id,ct);
        if(user.State!="active"||!Current(action,user)||credential.LockedUntil>time.GetUtcNow())return false;
        if(credential.MfaSecretCiphertext!=null&&!await Factor(db,user,credential,code??"",ct)){Fail(credential);action.Attempts++;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return false;}
        credential.PasswordHash=Hasher.HashPassword(user,newPassword);credential.MustReset=false;credential.FailedAttempts=0;credential.LockedUntil=null;
        await IdentitySecrets.Invalidate(db,user,time.GetUtcNow(),ct);Audit(db,user,"account.password-reset-completed");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return true;
    }
    public async Task SessionsAsync(AccountIdentity actor,Guid? id,bool others,string reason,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Own(db,actor,ct);AdminAccess.Text(reason,1000);
        if(others&&actor.SessionId==null)throw Invalid();
        var rows=db.Set<UserSession>().Where(x=>x.UserId==user.Id&&x.RevokedAt==null);
        rows=others?rows.Where(x=>x.Id!=actor.SessionId):rows.Where(x=>x.Id==id);
        await rows.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,time.GetUtcNow()),ct);Audit(db,user,"account.sessions-revoked",reason);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task ReportAsync(AccountIdentity actor,string description,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Own(db,actor,ct);Audit(db,user,"security.activity-reported",AdminAccess.Text(description,1000));await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private void Audit(BackOfficeDbContext db,StaffUser user,string kind,string? reason=null){var row=LocalIdentityService.AuthenticationAudit(user.Id,kind,time.GetUtcNow());row.SubjectRecordId=user.Id;row.Reason=reason;db.Add(row);}
    private static void Password(string value){if(value is null||value.Length is <12 or >128)throw new QuoteOperationException(422,"password-length-invalid");}
    private static QuoteOperationException Denied()=>new(403,"account-verification-denied");
    private static QuoteOperationException Invalid()=>new(400,"account-input-invalid");
}
