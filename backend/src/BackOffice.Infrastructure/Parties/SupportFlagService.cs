using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Parties;

public sealed class SupportFlagOperationException(int status,string code):Exception("The support change cannot be applied.")
{ public int Status{get;}=status;public string Code{get;}=code; }

public static class SupportFlagService
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};

    public static async Task AuthorizeCreateAsync(BackOfficeDbContext db,ActorContext actor,Guid originId,Guid personId,IReadOnlyCollection<Guid> grantIds,CancellationToken token=default)
    {
        RequireWrite(actor);var scope=new PartyScope(actor);
        var origin=await scope.Relationships(db).SingleOrDefaultAsync(x=>x.Id==originId,token) ?? throw new SupportFlagAccessException();
        // Historical membership is enough to authorize receipt replay. Active membership is a mutation prerequisite.
        if(!await scope.Contacts(db,includeEnded:true).AnyAsync(x=>x.RelationshipId==originId && x.PersonId==personId,token))throw new SupportFlagAccessException();
        await AuthorizeGrants(db,scope,origin.ClientId,grantIds,token);
    }
    public static async Task<SupportFlag> AuthorizeFlagAsync(BackOfficeDbContext db,ActorContext actor,Guid flagId,IReadOnlyCollection<Guid> grantIds,CancellationToken token=default)
    {
        RequireWrite(actor);
        var row=await new SupportFlagScope(actor).InternalFlags(db).SingleOrDefaultAsync(x=>x.Id==flagId,token) ?? throw new SupportFlagAccessException();
        await AuthorizeGrants(db,new PartyScope(actor),row.ClientId,grantIds,token);return row;
    }
    private static async Task AuthorizeGrants(BackOfficeDbContext db,PartyScope scope,Guid clientId,IReadOnlyCollection<Guid> ids,CancellationToken token)
    {
        if(ids.Count>100 || ids.Any(x=>x==Guid.Empty) || ids.Distinct().Count()!=ids.Count)throw new SupportFlagAccessException();
        if(await scope.Relationships(db).CountAsync(x=>x.ClientId==clientId && ids.Contains(x.Id),token)!=ids.Count)throw new SupportFlagAccessException();
    }
    private static void RequireWrite(ActorContext actor)
    { if(!actor.HasCapability("support-write"))throw new SupportFlagOperationException(403,"support-write-forbidden"); }

    public static async Task<SupportFlag> CreateAsync(BackOfficeDbContext db,ActorContext actor,Guid originId,Guid personId,byte[] expected,
        ValidatedFlag input,DateTimeOffset now,CancellationToken token=default)
    {
        await AuthorizeCreateAsync(db,actor,originId,personId,input.VisibleRelationshipIds,token);
        ReviewDate(input,now);
        var parents=await LockParents(db,input.VisibleRelationshipIds.Append(originId),token);var origin=parents.Single(x=>x.Id==originId);
        Version(origin.RowVersion,expected);
        var clientId=await new SupportFlagScope(actor).ValidateMembershipAsync(db,originId,personId,input.VisibleRelationshipIds,token);
        var flag=new SupportFlag {ClientId=clientId,OriginRelationshipId=originId,PersonId=personId,CreatedBy=actor.UserId,CreatedAt=now,UpdatedAt=now};
        Apply(flag,input);db.Add(flag);AddGrants(db,flag,input.VisibleRelationshipIds,actor,now);
        await Finish(db,flag,origin,actor,"created",input.VisibleRelationshipIds,now,token);return flag;
    }
    public static async Task<SupportFlag> UpdateAsync(BackOfficeDbContext db,ActorContext actor,Guid flagId,byte[] expected,ValidatedFlag input,DateTimeOffset now,CancellationToken token=default)
    {
        var authorized=await AuthorizeFlagAsync(db,actor,flagId,input.VisibleRelationshipIds,token);
        ReviewDate(input,now);
        var priorIds=await db.Set<FlagVisibility>().Where(x=>x.FlagId==flagId).Select(x=>x.RelationshipId).ToArrayAsync(token);
        var parents=await LockParents(db,priorIds.Concat(input.VisibleRelationshipIds).Append(authorized.OriginRelationshipId),token);
        var flag=await LockFlag(db,flagId,expected,token);
        await new SupportFlagScope(actor).ValidateMembershipAsync(db,flag.OriginRelationshipId,flag.PersonId,input.VisibleRelationshipIds,token);
        var action=SameDeclaration(flag,input) && priorIds.Order().SequenceEqual(input.VisibleRelationshipIds.Order()) ? "reviewed" : "amended";
        // Reconcile instead of delete/reinsert so an unchanged grant keeps its recorded provenance.
        var grants=await db.Set<FlagVisibility>().Where(x=>x.FlagId==flagId).ToListAsync(token);
        db.RemoveRange(grants.Where(x=>!input.VisibleRelationshipIds.Contains(x.RelationshipId)));
        AddGrants(db,flag,input.VisibleRelationshipIds.Except(grants.Select(x=>x.RelationshipId)),actor,now);Apply(flag,input);
        await Finish(db,flag,parents.Single(x=>x.Id==flag.OriginRelationshipId),actor,action,input.VisibleRelationshipIds,now,token);return flag;
    }
    public static async Task<SupportFlag> EndAsync(BackOfficeDbContext db,ActorContext actor,Guid flagId,byte[] expected,string reason,DateTimeOffset now,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(reason) || reason.Length>1000 || reason.Any(c=>char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new SupportFlagOperationException(422,"end-reason-required");
        var authorized=await AuthorizeFlagAsync(db,actor,flagId,[],token);
        var ids=await db.Set<FlagVisibility>().Where(x=>x.FlagId==flagId).Select(x=>x.RelationshipId).ToArrayAsync(token);
        var parents=await LockParents(db,ids.Append(authorized.OriginRelationshipId),token);
        var flag=await LockFlag(db,flagId,expected,token);
        // Resolution remains possible after a contact or relationship has ended.
        flag.EndedAt=now;flag.EndedBy=actor.UserId;flag.Reason=reason.Replace("\r\n","\n").Replace('\r','\n').Trim();
        await Finish(db,flag,parents.Single(x=>x.Id==flag.OriginRelationshipId),actor,"ended",ids,now,token);return flag;
    }
    private static async Task<List<ClientAgencyRelationship>> LockParents(BackOfficeDbContext db,IEnumerable<Guid> ids,CancellationToken token)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Support mutations require the caller's audited transaction.");
        var parents=new List<ClientAgencyRelationship>();
        foreach(var id in ids.Distinct().Order())parents.Add(await db.Set<ClientAgencyRelationship>()
            .FromSqlInterpolated($"SELECT * FROM [ClientAgencyRelationship] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={id}").SingleOrDefaultAsync(token) ?? throw new SupportFlagAccessException());
        return parents;
    }
    private static async Task<SupportFlag> LockFlag(BackOfficeDbContext db,Guid id,byte[] expected,CancellationToken token)
    {
        var flag=await db.Set<SupportFlag>().FromSqlInterpolated($"SELECT * FROM [SupportFlag] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={id}").SingleOrDefaultAsync(token) ?? throw new SupportFlagAccessException();
        Version(flag.RowVersion,expected);if(flag.EndedAt is not null)throw new SupportFlagOperationException(409,"support-flag-ended");return flag;
    }
    private static void Version(byte[] current,byte[] expected)
    { if(expected.Length!=8 || !CryptographicOperations.FixedTimeEquals(current,expected))throw new SupportFlagOperationException(412,"stale-support-flag"); }
    private static void ReviewDate(ValidatedFlag input,DateTimeOffset now)=>SupportFlagRules.ValidateReviewDate(input.ReviewOn,
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime));
    private static void Apply(SupportFlag flag,ValidatedFlag input)
    {
        flag.TypeCode=input.TypeCode;flag.InternalCategory=input.InternalCategory;flag.InternalInstruction=input.InternalInstruction;flag.AgencyInstruction=input.AgencyInstruction;
        flag.ConsentBasis=input.ConsentBasis;flag.ReviewOn=input.ReviewOn;flag.Reason=input.Reason;
    }
    private static bool SameDeclaration(SupportFlag flag,ValidatedFlag input)=>flag.TypeCode==input.TypeCode && flag.InternalCategory==input.InternalCategory &&
        flag.InternalInstruction==input.InternalInstruction && flag.AgencyInstruction==input.AgencyInstruction && flag.ConsentBasis==input.ConsentBasis;
    private static void AddGrants(BackOfficeDbContext db,SupportFlag flag,IEnumerable<Guid> ids,ActorContext actor,DateTimeOffset now)
    {
        foreach(var id in ids)db.Add(new FlagVisibility {FlagId=flag.Id,ClientId=flag.ClientId,RelationshipId=id,CreatedBy=actor.UserId,CreatedAt=now});
    }
    private static async Task Finish(BackOfficeDbContext db,SupportFlag flag,ClientAgencyRelationship origin,ActorContext actor,string action,Guid[] ids,DateTimeOffset now,CancellationToken token)
    {
        origin.UpdatedAt=now;db.Entry(origin).Property(x=>x.UpdatedAt).IsModified=true;
        if(db.Entry(flag).State!=EntityState.Added){flag.UpdatedAt=now;db.Entry(flag).Property(x=>x.UpdatedAt).IsModified=true;}
        await db.SaveChangesAsync(token);
        db.Add(new SupportFlagHistory {FlagId=flag.Id,ActorId=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,OccurredAt=now,Action=action,Reason=flag.Reason,Snapshot=JsonSerializer.Serialize(View(flag,ids),Json)});
        db.Add(new ClientActivity {ClientId=flag.ClientId,RelationshipId=origin.Id,ActorId=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,OccurredAt=now,EventType="support-flag."+action});
        await db.SaveChangesAsync(token);
    }
    public static SupportFlagView View(SupportFlag flag,IEnumerable<Guid> grants)=>new(flag.Id,flag.PersonId,flag.OriginRelationshipId,flag.TypeCode,flag.InternalCategory,
        flag.InternalInstruction,flag.ConsentBasis,flag.ReviewOn,flag.Reason,grants.Order().ToArray(),flag.AgencyInstruction,flag.EndedAt);
}
