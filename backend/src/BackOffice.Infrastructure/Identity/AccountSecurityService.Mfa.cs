using System.Security.Claims;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Identity;
public sealed record MfaEnrolment(Guid EnrolmentId,string Secret,string OtpauthUri,DateTimeOffset ExpiresAt);
public sealed partial class AccountSecurityService
{
    public async Task<MfaEnrolment> EnrolAsync(AccountIdentity actor,string password,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Own(db,actor,ct);var credential=await Credential(db,user.Id,ct);
        if(credential.MfaSecretCiphertext!=null)throw new QuoteOperationException(409,"mfa-already-enabled");
        if(!await Proof(db,user,credential,new(password,null),false,ct)){await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);throw Denied();}
        var secret=LocalTotp.NewSecret();var issue=await secrets.Issue(db,user,"mfa-enrolment",TimeSpan.FromMinutes(10),false,ct);issue.Action.SecretCiphertext=secrets.Protect(secret);
        Audit(db,user,"account.mfa-enrolment-started");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return new(issue.Action.Id,secret,$"otpauth://totp/{Uri.EscapeDataString("Cover MGA:"+user.Email)}?secret={secret}&issuer=Cover%20MGA&algorithm=SHA1&digits=6&period=30",issue.Action.ExpiresAt);
    }
    public async Task<string[]?> ConfirmAsync(AccountIdentity actor,Guid id,string code,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Own(db,actor,ct);var credential=await Credential(db,user.Id,ct);var action=await Action(db,id,ct);
        if(action.UserId!=user.Id||action.Kind!="mfa-enrolment"||!Current(action,user)||action.SecretCiphertext==null||credential.MfaSecretCiphertext!=null||credential.LockedUntil>time.GetUtcNow())return null;
        var step=LocalTotp.Verify(secrets.Unprotect(action.SecretCiphertext),code,time.GetUtcNow(),null);
        if(step==null){action.Attempts++;Fail(credential);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return null;}
        credential.MfaSecretCiphertext=action.SecretCiphertext;credential.LastTotpStep=step;credential.FailedAttempts=0;credential.LockedUntil=null;
        await IdentitySecrets.Invalidate(db,user,time.GetUtcNow(),ct);var codes=await Recovery(db,user,ct);Audit(db,user,"account.mfa-enabled");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return codes;
    }
    public async Task CancelAsync(AccountIdentity actor,Guid id,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Own(db,actor,ct);var row=await Action(db,id,ct);if(row.UserId!=user.Id||row.Kind!="mfa-enrolment")throw Denied();row.ConsumedAt=time.GetUtcNow();row.SecretCiphertext=null;Audit(db,user,"account.mfa-enrolment-cancelled");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<string[]> ReplaceCodesAsync(AccountIdentity actor,AccountProof proof,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Own(db,actor,ct);var credential=await Credential(db,user.Id,ct);
        if(credential.MfaSecretCiphertext==null)throw Invalid();
        if(!await Proof(db,user,credential,proof,true,ct)){await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);throw Denied();}
        var codes=await Recovery(db,user,ct);Audit(db,user,"account.mfa-recovery-replaced");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return codes;
    }
    public async Task DisableAsync(AccountIdentity actor,AccountProof proof,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Own(db,actor,ct);var credential=await Credential(db,user.Id,ct);
        if(credential.MfaSecretCiphertext==null)throw Invalid();
        if(!await Proof(db,user,credential,proof,true,ct)){await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);throw Denied();}
        credential.MfaSecretCiphertext=null;credential.LastTotpStep=null;await IdentitySecrets.Invalidate(db,user,time.GetUtcNow(),ct);Audit(db,user,"account.mfa-disabled");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<string> ChallengeAsync(LocalIdentity identity,CancellationToken ct=default)
    {
        var actor=Identity(identity.Principal);await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Own(db,actor,ct);var credential=await Credential(db,user.Id,ct);
        if(credential.MfaSecretCiphertext==null||credential.LockedUntil>time.GetUtcNow())throw Denied();
        var issue=await secrets.Issue(db,user,"mfa-login",TimeSpan.FromMinutes(5),false,ct);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return issue.Token;
    }
    public async Task<LocalIdentity?> CompleteLoginAsync(string token,string code,CancellationToken ct=default)
    {
        if(token is null||token.Length!=43||code is null||code.Length>100)return null;
        await using var db=await factory.CreateDbContextAsync(ct);var hash=IdentitySecrets.Hash(token);var hint=await db.Set<IdentityAction>().AsNoTracking().SingleOrDefaultAsync(x=>x.TokenHash==hash&&x.Kind=="mfa-login",ct);if(hint==null)return null;
        var reference=await IdentitySnapshot.Reference(db,hint.UserId,ct);if(reference==null)return null;
        await using var tx=await db.Database.BeginTransactionAsync(ct);var snapshot=await IdentitySnapshot.Lock(db,reference,ct);if(snapshot==null)return null;
        var user=await IdentitySecrets.HoldUser(db,hint.UserId,ct);var credential=await Credential(db,user.Id,ct);var action=await Action(db,hint.Id,ct);
        if(!Current(action,user)||credential.MfaSecretCiphertext==null||credential.MustReset||credential.LockedUntil>time.GetUtcNow())return null;
        if(!await Factor(db,user,credential,code,ct)){action.Attempts++;Fail(credential);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return null;}
        action.ConsumedAt=time.GetUtcNow();credential.FailedAttempts=0;credential.LockedUntil=null;
        var principal=LocalIdentityService.Principal(user,snapshot.Roles);((ClaimsIdentity)principal.Identity!).AddClaim(new("amr","mfa"));
        var view=new ActorView(user.Id,user.DisplayName,user.Email,snapshot.Roles.Select(x=>x.Code).Order().ToArray(),true,user.AgencyId==null?"internal":"agency",user.AgencyId);
        Audit(db,user,"authentication.mfa-verified");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(view,principal);
    }
    private async Task<bool> Proof(BackOfficeDbContext db,StaffUser user,UserCredential credential,AccountProof proof,bool factor,CancellationToken ct)
    {
        if(credential.LockedUntil>time.GetUtcNow())return false;
        if(proof.Password is null||proof.Password.Length>1024||credential.PasswordHash==null||Hasher.VerifyHashedPassword(user,credential.PasswordHash,proof.Password)==PasswordVerificationResult.Failed){Fail(credential);return false;}
        if(factor&&credential.MfaSecretCiphertext!=null&&!await Factor(db,user,credential,proof.Code??"",ct)){Fail(credential);return false;}
        credential.FailedAttempts=0;credential.LockedUntil=null;return true;
    }
    private async Task<bool> Factor(BackOfficeDbContext db,StaffUser user,UserCredential credential,string code,CancellationToken ct)
    {
        if(code.Length==6&&credential.MfaSecretCiphertext!=null)
        {
            var step=LocalTotp.Verify(secrets.Unprotect(credential.MfaSecretCiphertext),code,time.GetUtcNow(),credential.LastTotpStep);
            if(step!=null){credential.LastTotpStep=step;return true;}
        }
        if(code.Length!=43)return false;var hash=IdentitySecrets.Hash(code);
        var recovery=await db.Set<IdentityAction>().FromSqlInterpolated($"SELECT * FROM IdentityAction WITH(UPDLOCK,HOLDLOCK) WHERE UserId={user.Id} AND Kind='mfa-recovery' AND TokenHash={hash}").SingleOrDefaultAsync(ct);
        if(recovery==null||!Current(recovery,user))return false;recovery.ConsumedAt=time.GetUtcNow();return true;
    }
    private async Task<string[]> Recovery(BackOfficeDbContext db,StaffUser user,CancellationToken ct)
    {
        await db.Set<IdentityAction>().Where(x=>x.UserId==user.Id&&x.Kind=="mfa-recovery"&&x.ConsumedAt==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,time.GetUtcNow()),ct);
        foreach(var entry in db.ChangeTracker.Entries<IdentityAction>().Where(x=>x.Entity.UserId==user.Id&&x.Entity.Kind=="mfa-recovery"&&x.State!=EntityState.Added).ToArray())entry.State=EntityState.Detached;
        var codes=Enumerable.Range(0,8).Select(_=>IdentitySecrets.Token()).ToArray();
        foreach(var code in codes)db.Add(new IdentityAction{UserId=user.Id,Kind="mfa-recovery",TokenHash=IdentitySecrets.Hash(code),SecurityStamp=user.SecurityStamp,CreatedAt=time.GetUtcNow(),UpdatedAt=time.GetUtcNow(),ExpiresAt=time.GetUtcNow().AddYears(5)});return codes;
    }
    private void Fail(UserCredential credential){if(credential.LockedUntil!=null&&credential.LockedUntil<=time.GetUtcNow()){credential.FailedAttempts=0;credential.LockedUntil=null;}credential.FailedAttempts=Math.Min(5,credential.FailedAttempts+1);if(credential.FailedAttempts>=5)credential.LockedUntil=time.GetUtcNow().AddMinutes(15);}
    private bool Current(IdentityAction row,StaffUser user)=>row.ConsumedAt==null&&row.Attempts<5&&row.ExpiresAt>time.GetUtcNow()&&row.SecurityStamp==user.SecurityStamp;
    private static async Task<IdentityAction> Action(BackOfficeDbContext db,Guid id,CancellationToken ct)=>await db.Set<IdentityAction>().FromSqlInterpolated($"SELECT * FROM IdentityAction WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync(ct)??throw Invalid();
}
