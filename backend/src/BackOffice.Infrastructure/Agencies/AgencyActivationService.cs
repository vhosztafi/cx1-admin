using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Agencies;

// Proposal creation does not activate the agency. Applying the decision and its
// delivery/follow-up effects is a separate transaction participant still to come.
public sealed class AgencyActivationService(AgencyDraftService agencies,AgencyEvidenceService evidence,SqlCommandBoundary commands,TimeProvider time)
{
    public async Task<CommandOutcome> Propose(ActorContext actor,Guid agencyId,string key,byte[] expected,string reason,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason.Any(char.IsControl))throw new AgencyCommandException(422,"reason-required");reason=reason.Trim();
        await agencies.Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agencies/{agencyId}/activate",key,Guid.NewGuid()),new{reason},"agency.activation-requested",async(db,ct)=>
        {
            var agency=await AgencyDraftService.Lock(db,agencyId,expected,ct);await AgencyApprovalLocks.Authority(db,actor.UserId,ct);
            if(agency.State!="draft")throw new AgencyCommandException(409,"agency-not-draft");
            var fingerprint=await Fingerprint(db,agency,ct);var now=time.GetUtcNow();
            var pending=await db.Set<AgencyStateRequest>().SingleOrDefaultAsync(x=>x.AgencyId==agencyId&&x.Kind=="activation"&&x.State=="pending",ct);
            if(pending is not null)
            {
                if(CryptographicOperations.FixedTimeEquals(pending.BaseVersion,agency.RowVersion))throw new AgencyCommandException(409,"agency-activation-pending");
                pending.State="stale";pending.DecisionReason="Agency changed before a replacement activation proposal.";pending.DecidedAt=now;await db.SaveChangesAsync(ct);
            }
            var request=new AgencyStateRequest{AgencyId=agencyId,BaseVersion=agency.RowVersion.ToArray(),Kind="activation",RequestedState="active",ProposedInputFingerprint=fingerprint,RequestedBy=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,RequestReason=reason};
            db.Add(request);await db.SaveChangesAsync(ct);
            return new(request.Id,202,JsonSerializer.Serialize(new{id=request.Id}),Etag:AgencyDraftService.Etag(request.RowVersion));
        },token);
    }
    internal async Task<string> Fingerprint(BackOfficeDbContext db,Agency agency,CancellationToken token)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Activation assessment requires its agency transaction.");
        // Agency mutations serialize on the parent. Retain external prerequisites,
        // including manager status and versioned rule ranges, until commit.
        await db.Set<SettingVersion>().FromSqlRaw("SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope=N'agency-compliance'").AsNoTracking().ToListAsync(token);
        var users=await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(UPDLOCK,HOLDLOCK) WHERE AgencyId={agency.Id} ORDER BY Id").ToListAsync(token);
        var userRoles=new List<UserRole>();
        foreach(var user in users)
            userRoles.AddRange(await db.Set<UserRole>().FromSqlInterpolated($"SELECT * FROM UserRole WITH(HOLDLOCK) WHERE UserId={user.Id}").AsNoTracking().ToListAsync(token));
        var roleIds=userRoles.Select(x=>x.RoleId).Distinct().Order().ToArray();
        foreach(var id in roleIds)await db.Set<Role>().FromSqlInterpolated($"SELECT * FROM Role WITH(HOLDLOCK) WHERE Id={id}").AsNoTracking().ToListAsync(token);
        if(agency.RelationshipManagerId is Guid manager)
        {
            await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(HOLDLOCK) WHERE Id={manager}").AsNoTracking().ToListAsync(token);
            var managerRoles=await db.Set<UserRole>().FromSqlInterpolated($"SELECT * FROM UserRole WITH(HOLDLOCK) WHERE UserId={manager}").AsNoTracking().ToListAsync(token);
            foreach(var link in managerRoles.OrderBy(x=>x.RoleId))await db.Set<Role>().FromSqlInterpolated($"SELECT * FROM Role WITH(HOLDLOCK) WHERE Id={link.RoleId}").AsNoTracking().ToListAsync(token);
        }
        using var document=JsonDocument.Parse(await db.Set<AgencyOnboarding>().Where(x=>x.AgencyId==agency.Id).Select(x=>x.Details).SingleAsync(token));
        var products=await db.Set<AgencyDraftProduct>().Where(x=>x.AgencyId==agency.Id).Select(x=>new AgencyProductInput(x.ProductVersionId,x.EffectiveFrom,x.BrokerCommissionBasisPoints)).ToListAsync(token);
        var now=time.GetUtcNow();var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
        var terms=AgencyTermsRules.ExtractInitial(document.RootElement,products,today,"Activation assessment");
        var distribution=await AgencyApprovalLocks.Eligibility(db,terms,now,token);
        var readiness=await evidence.Validate(db,agency,token);
        if(!readiness.Valid)throw new AgencyCommandException(422,"agency-not-ready");
        var invitations=await db.Set<AgencyInvitation>().AsNoTracking().Where(x=>x.AgencyId==agency.Id&&x.State=="staged").OrderBy(x=>x.Id).Select(x=>new{x.Id,x.UserId}).ToListAsync(token);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new{agencyBase=Convert.ToBase64String(agency.RowVersion),distribution,readiness.RuleVersionId,
            evidence=readiness.Items.Where(x=>x.EvidenceId!=null).Select(x=>new{x.Code,x.EvidenceId}).OrderBy(x=>x.Code,StringComparer.Ordinal),
            users=users.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.State,x.SecurityStamp}),roles=userRoles.OrderBy(x=>x.UserId).ThenBy(x=>x.RoleId).Select(x=>new{x.UserId,x.RoleId}),invitations});
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
