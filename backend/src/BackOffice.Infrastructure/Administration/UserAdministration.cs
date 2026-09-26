using System.Data;
using System.Net.Mail;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace BackOffice.Infrastructure.Administration;
public sealed record UserInvitation(string Email,string DisplayName,Guid TeamId,string Reason);
public sealed record UserChange(Guid UserId,string Etag,string Email,Guid TeamId,string[] Roles,bool ResetMfa,string Reason,bool SelfRequested=false);
public sealed class UserAdministration(IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,IdentitySecrets secrets,TimeProvider time,IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions Json=ProductAdministration.Json;
    public async Task<object> ListAsync(ActorContext actor,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);await AdminAccess.Authorize(db,actor,ct);
        var users=await db.Set<StaffUser>().AsNoTracking().Where(x=>x.AgencyId==null).OrderBy(x=>x.DisplayName).ToArrayAsync(ct);
        var roles=await db.Set<Role>().AsNoTracking().Where(x=>x.Scope=="internal").ToArrayAsync(ct);var links=await db.Set<UserRole>().AsNoTracking().ToArrayAsync(ct);
        var credentials=await db.Set<UserCredential>().AsNoTracking().Select(x=>new{x.UserId,mfa=x.MfaSecretCiphertext!=null,x.MustReset}).ToArrayAsync(ct);
        var deliveries=await db.Set<IdentityAction>().AsNoTracking().Where(x=>(x.Kind=="invitation"||x.Kind=="password-reset")&&x.ConsumedAt==null&&x.ExpiresAt>time.GetUtcNow()).Select(x=>new{x.Id,x.UserId,x.Kind,x.ExpiresAt}).ToArrayAsync(ct);
        var result=new {users=users.Select(x=>new{x.Id,x.Email,x.DisplayName,x.State,x.TeamId,etag=AdminAccess.Etag(x.RowVersion),roles=roles.Where(r=>links.Any(l=>l.UserId==x.Id&&l.RoleId==r.Id)).Select(r=>r.Code),mfaEnabled=credentials.FirstOrDefault(c=>c.UserId==x.Id)?.mfa??false,mustReset=credentials.FirstOrDefault(c=>c.UserId==x.Id)?.MustReset??false}),
            teams=(await db.Set<Team>().AsNoTracking().OrderBy(x=>x.Name).ToArrayAsync(ct)).Select(x=>new{x.Id,x.Name,etag=AdminAccess.Etag(x.RowVersion)}),roles=roles.Select(x=>x.Code),requests=await AdministrationRequests.List(db,"user",ct),deliveries,demoDelivery=environment.IsDevelopment()};
        await tx.CommitAsync(ct);return result;
    }
    public Task<CommandOutcome> InviteAsync(ActorContext actor,UserInvitation input,string key,CancellationToken ct=default)
        =>Command(actor,"/api/v1/admin/users/invitations",key,input,async(db,t)=>
        {
            var email=Email(input.Email);AdminAccess.Text(input.Reason,1000);
            if(!await db.Set<Team>().AnyAsync(x=>x.Id==input.TeamId,t)||await db.Set<StaffUser>().AnyAsync(x=>x.NormalizedEmail==email.ToUpperInvariant(),t))throw Conflict();
            var user=new StaffUser{Email=email,NormalizedEmail=email.ToUpperInvariant(),DisplayName=AdminAccess.Text(input.DisplayName),State="invited",TeamId=input.TeamId,CreatedBy=actor.UserId};
            db.Add(user);db.Add(new UserRole{UserId=user.Id,RoleId=(await db.Set<Role>().SingleAsync(x=>x.Code=="servicing"&&x.Scope=="internal",t)).Id});
            db.Add(new UserCredential{UserId=user.Id,ProviderSubject=user.NormalizedEmail,MustReset=true});await db.SaveChangesAsync(t);
            var issued=await secrets.Issue(db,user,"invitation",TimeSpan.FromHours(24),true,t);
            AdminAccess.Audit(db,actor,user.Id,"administration.user-invited",input.Reason,null,new{user.Id,user.Email,role="servicing",deliveryId=issued.Action.Id},time.GetUtcNow());
            return Outcome(user.Id,new{id=user.Id,deliveryId=issued.Action.Id},201);
        },ct);
    public Task<CommandOutcome> TeamAsync(ActorContext actor,Guid? id,string name,string etag,string reason,string key,CancellationToken ct=default)
        =>Command(actor,"/api/v1/admin/teams/"+(id?.ToString()??"new"),key,new{id,name,etag,reason},async(db,t)=>
        {
            name=AdminAccess.Text(name,100);if(await db.Set<Team>().AnyAsync(x=>x.Name==name&&x.Id!=id,t))throw Conflict();
            var row=id is Guid value?await db.Set<Team>().SingleOrDefaultAsync(x=>x.Id==value,t)??throw Missing():new Team();
            if(id!=null)AdminAccess.Version(row,etag);var before=new{row.Name};row.Name=name;if(id==null)db.Add(row);
            AdminAccess.Audit(db,actor,row.Id,"administration.team-saved",reason,before,new{row.Name},time.GetUtcNow());return Outcome(row.Id,new{row.Id});
        },ct);
    public Task<CommandOutcome> ProposeAsync(ActorContext actor,UserChange input,string key,CancellationToken ct=default)
        =>Command(actor,"/api/v1/admin/users/requests",key,input,async(db,t)=>
        {
            if(input.SelfRequested)throw new QuoteOperationException(400,"user-change-invalid");
            var user=await Internal(db,input.UserId,t);AdminAccess.Version(user,input.Etag);await Validate(db,input,t);
            var row=AdministrationRequests.Create(db,actor,"user",input,time.GetUtcNow());AdminAccess.Audit(db,actor,user.Id,"administration.user-change-requested",input.Reason,null,new{requestId=row.Id,input.UserId,input.Email,input.TeamId,input.Roles,input.ResetMfa},time.GetUtcNow());
            return Outcome(row.Id,AdministrationRequests.View(row),201);
        },ct);
    public Task<CommandOutcome> DecideAsync(ActorContext actor,Guid id,string etag,bool approve,string reason,string key,CancellationToken ct=default)
        =>Command(actor,$"/api/v1/admin/users/requests/{id}/decision",key,new{etag,approve,reason},async(db,t)=>
        {
            var row=await AdministrationRequests.Hold(db,id,etag,"user",t);var request=AdministrationRequests.View(row);
            if(request.RequestedBy==actor.UserId)throw new QuoteOperationException(403,"independent-approval-required");
            var input=request.Proposal.Deserialize<UserChange>(Json)!;
            if(approve)
            {
                if(input.SelfRequested)
                {
                    if(request.RequestedBy!=input.UserId||await IdentitySnapshot.Lock(db,new(request.RequestedBy,null),t)==null)throw new QuoteOperationException(403,"requester-no-longer-active");
                }
                else await AdminAccess.Authorize(db,new(request.RequestedBy,null,null,new HashSet<string>()),t);
                var user=await Internal(db,input.UserId,t);AdminAccess.Version(user,input.Etag);await Validate(db,input,t);
                if(!input.Roles.Contains("system-admin"))await GuardLastAdmin(db,user,t);
                var before=new{user.Email,user.TeamId};var credential=await db.Set<UserCredential>().SingleAsync(x=>x.UserId==user.Id&&x.Provider=="local",t);
                user.Email=Email(input.Email);user.NormalizedEmail=user.Email.ToUpperInvariant();user.TeamId=input.TeamId;credential.ProviderSubject=user.NormalizedEmail;
                db.RemoveRange(await db.Set<UserRole>().Where(x=>x.UserId==user.Id).ToArrayAsync(t));await db.SaveChangesAsync(t);
                var roles=await db.Set<Role>().Where(x=>input.Roles.Contains(x.Code)&&x.Scope=="internal").ToArrayAsync(t);db.AddRange(roles.Select(r=>new UserRole{UserId=user.Id,RoleId=r.Id}));
                if(input.ResetMfa){credential.MfaSecretCiphertext=null;credential.LastTotpStep=null;}
                await IdentitySecrets.Invalidate(db,user,time.GetUtcNow(),t);
                if(user.State=="invited")await secrets.Issue(db,user,"invitation",TimeSpan.FromHours(24),true,t);
                AdminAccess.Audit(db,actor,user.Id,"administration.user-change-applied",reason,before,new{user.Email,user.TeamId,input.Roles,input.ResetMfa,requestId=id},time.GetUtcNow());
            }
            var decision=AdministrationRequests.Decide(db,row,actor,approve?"approved":"rejected",reason,time.GetUtcNow());return Outcome(id,AdministrationRequests.View(decision));
        },ct);
    public Task<CommandOutcome> RestrictAsync(ActorContext actor,Guid id,string etag,string action,string reason,string key,CancellationToken ct=default)
        =>Command(actor,$"/api/v1/admin/users/{id}/{action}",key,new{etag,reason},async(db,t)=>
        {
            var user=await Internal(db,id,t);AdminAccess.Version(user,etag);AdminAccess.Text(reason,1000);
            if(action is not("suspend" or "resume" or "force-reset" or "revoke-sessions"))throw new QuoteOperationException(400,"user-action-invalid");
            if(action=="suspend"||action=="force-reset")await GuardLastAdmin(db,user,t);
            if(action=="suspend")user.State="suspended";
            if(action=="resume"){if(user.State!="suspended")throw Conflict();user.State="active";}
            await IdentitySecrets.Invalidate(db,user,time.GetUtcNow(),t);
            if(action=="force-reset")
            {
                if(user.State!="active")throw Conflict();var credential=await db.Set<UserCredential>().SingleAsync(x=>x.UserId==id&&x.Provider=="local",t);credential.MustReset=true;
                await secrets.Issue(db,user,"password-reset",TimeSpan.FromMinutes(30),true,t);
            }
            AdminAccess.Audit(db,actor,id,"administration.user-"+action,reason,null,new{id,state=user.State},time.GetUtcNow());return Outcome(id,new{id,state=user.State});
        },ct);
    public async Task<string> RevealAsync(ActorContext actor,Guid id,CancellationToken ct=default)
    {
        if(!environment.IsDevelopment())throw Missing();await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);await AdminAccess.Authorize(db,actor,ct);
        var hint=await db.Set<IdentityAction>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw Missing();var user=await Internal(db,hint.UserId,ct);
        var row=await db.Set<IdentityAction>().SingleAsync(x=>x.Id==id,ct);
        if(row.Kind is not("invitation" or "password-reset")||row.ConsumedAt!=null||row.ExpiresAt<=time.GetUtcNow()||row.SecurityStamp!=user.SecurityStamp||row.SecretCiphertext==null||user.State=="suspended")throw Conflict();
        var token=secrets.Unprotect(row.SecretCiphertext);AdminAccess.Audit(db,actor,user.Id,"administration.demo-delivery-viewed","Explicit local demo delivery",null,new{deliveryId=id,row.Kind},time.GetUtcNow());await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return token;
    }
    public async Task<bool> AcceptInvitationAsync(string token,string password,CancellationToken ct=default)
    {
        InvitationPassword.Validate(password);if(token is null||token.Length!=43)return false;
        await using var db=await factory.CreateDbContextAsync(ct);var hash=IdentitySecrets.Hash(token);var hint=await db.Set<IdentityAction>().AsNoTracking().SingleOrDefaultAsync(x=>x.TokenHash==hash&&x.Kind=="invitation",ct);if(hint==null)return false;
        await using var tx=await db.Database.BeginTransactionAsync(ct);var user=await Internal(db,hint.UserId,ct);
        var row=await db.Set<IdentityAction>().FromSqlInterpolated($"SELECT * FROM IdentityAction WITH(UPDLOCK,HOLDLOCK) WHERE Id={hint.Id}").SingleAsync(ct);
        if(user.State!="invited"||row.ConsumedAt!=null||row.ExpiresAt<=time.GetUtcNow()||row.SecurityStamp!=user.SecurityStamp)return false;
        var credential=await db.Set<UserCredential>().SingleAsync(x=>x.UserId==user.Id&&x.Provider=="local",ct);credential.PasswordHash=new PasswordHasher<StaffUser>().HashPassword(user,password);credential.MustReset=false;user.State="active";await IdentitySecrets.Invalidate(db,user,time.GetUtcNow(),ct);
        db.Add(LocalIdentityService.AuthenticationAudit(user.Id,"authentication.invitation-accepted",time.GetUtcNow()));await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return true;
    }
    private static async Task Validate(BackOfficeDbContext db,UserChange input,CancellationToken ct)
    {
        var email=Email(input.Email);AdminAccess.Text(input.Reason,1000);
        if(input.Roles==null||input.Roles.Length is <1 or >10||input.Roles.Distinct().Count()!=input.Roles.Length||
            await db.Set<Role>().CountAsync(x=>x.Scope=="internal"&&input.Roles.Contains(x.Code),ct)!=input.Roles.Length||!await db.Set<Team>().AnyAsync(x=>x.Id==input.TeamId,ct)||
            await db.Set<StaffUser>().AnyAsync(x=>x.Id!=input.UserId&&x.NormalizedEmail==email.ToUpperInvariant(),ct))throw Conflict();
    }
    private static string Email(string value)
    {
        var email=AdminAccess.Text(value,254);if(!MailAddress.TryCreate(email,out var parsed)||parsed.Address!=email)throw new QuoteOperationException(400,"user-email-invalid");return email;
    }
    private static async Task<StaffUser> Internal(BackOfficeDbContext db,Guid id,CancellationToken ct){var user=await IdentitySecrets.HoldUser(db,id,ct);if(user.AgencyId!=null)throw Missing();return user;}
    private static async Task GuardLastAdmin(BackOfficeDbContext db,StaffUser user,CancellationToken ct)
    {
        var admins=await(from u in db.Set<StaffUser>() join l in db.Set<UserRole>() on u.Id equals l.UserId join r in db.Set<Role>() on l.RoleId equals r.Id join c in db.Set<UserCredential>() on u.Id equals c.UserId where u.AgencyId==null&&u.State=="active"&&r.Code=="system-admin"&&!c.MustReset select u.Id).Distinct().ToArrayAsync(ct);
        if(admins.Contains(user.Id)&&admins.Length<=1)throw new QuoteOperationException(409,"last-active-administrator");
    }
    private Task<CommandOutcome> Command<T>(ActorContext actor,string route,string key,T body,Func<BackOfficeDbContext,CancellationToken,Task<CommandOutcome>> write,CancellationToken ct)
        =>commands.ExecuteAuthorizedAsync(new(actor.UserId,route,key,Guid.NewGuid()),body,"administration.user-command",async(db,t)=>
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'CoverMGA.InternalIdentityAdministration',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51110,'Identity administration busy',1;",t);
            await AdminAccess.Authorize(db,actor,t);
        },write,ct,IsolationLevel.Serializable);
    private static QuoteOperationException Missing()=>new(404,"user-not-found");
    private static QuoteOperationException Conflict()=>new(409,"user-change-not-applicable");
    private static CommandOutcome Outcome(Guid id,object value,int status=200)=>new(id,status,JsonSerializer.Serialize(value,Json));
}
