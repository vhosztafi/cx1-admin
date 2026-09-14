using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed class InvitationAcceptance(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    private static readonly PasswordHasher<StaffUser> Hasher=new();

    // Secret-bearing authentication operations never use the command receipt store.
    public async Task<bool> Accept(string? tokenValue,string? password,CancellationToken token=default)
    {
        InvitationPassword.Validate(password);
        if(!InvitationToken.TryHash(tokenValue,out var hash))return false;
        await using var db=await factory.CreateDbContextAsync(token);
        var reference=await db.Set<AgencyInvitation>().AsNoTracking().Where(x=>x.TokenHash==hash)
            .Select(x=>new{x.Id,x.AgencyId,x.UserId}).SingleOrDefaultAsync(token);
        if(reference is null)return false;
        // Hash before taking aggregate locks; validity is checked again after hashing.
        var passwordHash=Hasher.HashPassword(new StaffUser(),password!);
        await using var transaction=await db.Database.BeginTransactionAsync(token);
        var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM [Agency] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={reference.AgencyId}").SingleAsync(token);
        var user=await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={reference.UserId}").SingleAsync(token);
        var invitation=await db.Set<AgencyInvitation>().FromSqlInterpolated($"SELECT * FROM [AgencyInvitation] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={reference.Id}").SingleAsync(token);
        var now=time.GetUtcNow();
        if(agency.State!="active"||user.State!="invited"||user.AgencyId!=agency.Id||invitation.UserId!=user.Id||invitation.AgencyId!=agency.Id||
            invitation.State!="pending"||invitation.IssuedAt is null||invitation.IssuedAt>now||invitation.ExpiresAt is null||invitation.ExpiresAt<=now||
            invitation.TokenHash is null||!CryptographicOperations.FixedTimeEquals(hash,invitation.TokenHash))return false;
        var roles=await(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id select role).ToListAsync(token);
        if(roles.Count!=1||roles[0].Scope!="agency"||roles[0].Code is not ("broker-admin" or "broker-user" or "broker-readonly")||
            await db.Set<UserCredential>().AnyAsync(x=>x.UserId==user.Id,token))return false;
        db.Add(new UserCredential{UserId=user.Id,Provider="local",ProviderSubject=user.Email,PasswordHash=passwordHash,CreatedBy=user.Id,CreatedAt=now});
        user.State="active";user.SecurityStamp=Guid.NewGuid().ToString("N");invitation.State="accepted";invitation.AcceptedAt=now;
        await db.Set<UserSession>().Where(x=>x.UserId==user.Id&&x.RevokedAt==null).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.RevokedAt,now),token);
        db.Entry(agency).Property(x=>x.UpdatedAt).IsModified=true;
        db.Add(new AuditEvent{ActorId=user.Id,CreatedBy=user.Id,SubjectRecordId=invitation.Id,EventType="agency.invitation-accepted",OccurredAt=now,After=JsonSerializer.Serialize(new{invitationId=invitation.Id,userId=user.Id})});
        db.Add(new AgencyActivity{AgencyId=agency.Id,ActorId=user.Id,CreatedBy=user.Id,Action="agency.invitation-accepted",OccurredAt=now});
        await db.SaveChangesAsync(token);await transaction.CommitAsync(token);return true;
    }
}
