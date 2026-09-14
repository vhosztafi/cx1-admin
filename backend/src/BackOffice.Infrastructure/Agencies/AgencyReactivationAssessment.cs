using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed record AgencyReactivationReadiness(string Fingerprint,Guid TermsVersionId,IReadOnlyList<Guid> FreshInvitationUserIds);

// Transaction participant only. This assessment neither restores access nor issues invitations.
public sealed class AgencyReactivationAssessment(AgencyEvidenceService evidence,TimeProvider time)
{
    public async Task<AgencyReactivationReadiness> Assess(BackOfficeDbContext db,Guid agencyId,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Reactivation assessment requires its agency transaction.");
        var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,ROWLOCK) WHERE Id={agencyId}").SingleAsync(token);
        if(agency.State!="suspended")throw new AgencyCommandException(409,"agency-not-suspended");
        var now=time.GetUtcNow();var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
        var versions=await db.Set<AgencyTermsVersion>().FromSqlInterpolated($"SELECT * FROM AgencyTermsVersion WITH(HOLDLOCK) WHERE AgencyId={agencyId}").AsNoTracking().ToListAsync(token);
        var current=versions.Where(x=>x.EffectiveFrom<=today).OrderByDescending(x=>x.EffectiveFrom).ThenByDescending(x=>x.Version).FirstOrDefault()??throw new AgencyCommandException(422,"agency-effective-terms-required");
        using var snapshot=JsonDocument.Parse(current.Snapshot);var terms=AgencyTermsRules.ReadPublished(snapshot.RootElement);
        if(terms.EffectiveFrom!=current.EffectiveFrom)throw new AgencyCommandException(422,"agency-terms-date-mismatch");
        var distribution=await AgencyApprovalLocks.Eligibility(db,terms,now,token);
        await db.Set<SettingVersion>().FromSqlRaw("SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope=N'agency-compliance'").AsNoTracking().ToListAsync(token);
        var rule=await evidence.Configuration(db,token);
        var users=await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(UPDLOCK,HOLDLOCK) WHERE AgencyId={agencyId} ORDER BY Id").ToListAsync(token);
        var links=new List<UserRole>();var credentials=new List<UserCredential>();
        foreach(var user in users)
        {
            links.AddRange(await db.Set<UserRole>().FromSqlInterpolated($"SELECT * FROM UserRole WITH(HOLDLOCK) WHERE UserId={user.Id}").AsNoTracking().ToListAsync(token));
            credentials.AddRange(await db.Set<UserCredential>().FromSqlInterpolated($"SELECT * FROM UserCredential WITH(HOLDLOCK) WHERE UserId={user.Id}").AsNoTracking().ToListAsync(token));
        }
        var roles=new List<Role>();
        foreach(var id in links.Select(x=>x.RoleId).Distinct().Order())roles.Add(await db.Set<Role>().FromSqlInterpolated($"SELECT * FROM Role WITH(HOLDLOCK) WHERE Id={id}").AsNoTracking().SingleAsync(token));
        var fresh=new List<Guid>();var viable=false;
        foreach(var user in users)
        {
            var membership=links.Where(x=>x.UserId==user.Id).ToArray();
            if(membership.Length!=1)continue;var role=roles.Single(x=>x.Id==membership[0].RoleId);
            if(role.Scope!="agency"||role.Code is not ("broker-admin" or "broker-user" or "broker-readonly"))continue;
            if(user.State=="invited")
            {
                var validated=AgencyUserRules.Validate(user.Email,user.DisplayName,role.Code);
                if(validated.NormalizedEmail!=user.NormalizedEmail)throw new AgencyCommandException(422,"agency-user-email-unavailable");
                fresh.Add(user.Id);if(role.Code=="broker-admin")viable=true;
            }
            if(user.State=="active"&&role.Code=="broker-admin"&&credentials.Any(x=>x.UserId==user.Id&&x.Provider=="local"&&!string.IsNullOrEmpty(x.PasswordHash)))viable=true;
        }
        if(agency.RelationshipManagerId is Guid manager)
        {
            await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(HOLDLOCK) WHERE Id={manager}").AsNoTracking().ToListAsync(token);
            var managerLinks=await db.Set<UserRole>().FromSqlInterpolated($"SELECT * FROM UserRole WITH(HOLDLOCK) WHERE UserId={manager}").AsNoTracking().ToListAsync(token);
            foreach(var link in managerLinks.OrderBy(x=>x.RoleId))await db.Set<Role>().FromSqlInterpolated($"SELECT * FROM Role WITH(HOLDLOCK) WHERE Id={link.RoleId}").AsNoTracking().ToListAsync(token);
        }
        var draft=JsonNode.Parse(await db.Set<AgencyOnboarding>().Where(x=>x.AgencyId==agencyId).Select(x=>x.Details).SingleAsync(token))!.AsObject();
        // Evidence declarations remain the current onboarding facts; commercial values
        // come from the currently effective approved version, never stale draft products.
        foreach(var key in new[]{"commercialTerms","settlement","paymentTermsDays","creditLimit"})draft[key]=JsonNode.Parse(snapshot.RootElement.GetProperty(key).GetRawText());
        using var details=JsonDocument.Parse(draft.ToJsonString());
        var latest=await db.Set<AgencyEvidence>().AsNoTracking().Where(x=>x.AgencyId==agencyId&&!db.Set<AgencyEvidence>().Any(n=>n.AgencyId==x.AgencyId&&n.Kind==x.Kind&&n.Ordinal>x.Ordinal)).ToListAsync(token);
        var facts=latest.ToDictionary(x=>x.Kind,x=>new AgencyEvidenceFact(x.Id,x.Kind,x.State,x.InputFingerprint,x.RuleVersionId,x.ExpiresOn));
        var checklist=AgencyActivationRules.Evaluate(details.RootElement,facts,rule.Id,today,rule.MinimumPi,rule.TobaVersion,new(true,viable));
        if(checklist.Any(x=>x.State!="satisfied")||agency.RelationshipManagerId is not Guid managerId||!await AgencyDraftService.Managers(db).AnyAsync(x=>x.Id==managerId,token))throw new AgencyCommandException(422,"agency-not-ready");
        var fingerprint=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{agency.Id,agency.RowVersion,termsId=current.Id,distribution,ruleId=rule.Id,
            evidence=checklist.Where(x=>x.EvidenceId!=null).OrderBy(x=>x.Code,StringComparer.Ordinal).Select(x=>new{x.Code,x.EvidenceId}),
            users=users.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}),roles=roles.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}),
            links=links.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}),credentials=credentials.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}),fresh=fresh.Order()}))).ToLowerInvariant();
        return new(fingerprint,current.Id,fresh.Order().ToArray());
    }
}
