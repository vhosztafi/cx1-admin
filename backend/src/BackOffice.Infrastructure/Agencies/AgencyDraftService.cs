using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Agencies;

public sealed class AgencyDraftService(IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
{
    public async Task<CommandOutcome> Save(ActorContext actor,Guid? agencyId,string key,byte[]? version,ValidatedAgencyDraft draft,int step,IReadOnlyList<AgencyProductInput>? products,CancellationToken token)
    {
        AgencyDraftRules.ValidateStep(step);if(products is not null)AgencyDraftRules.ValidateProducts(products);
        await Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,agencyId is null?"/api/v1/agencies":$"/api/v1/agencies/{agencyId}",key,Guid.NewGuid()),
            new{draft.Json,step,products},agencyId is null?"agency.created":"agency.draft-saved",async(db,ct)=>
        {
            Agency row;
            if(agencyId is Guid id)row=await Lock(db,id,version!,ct);
            else{row=new Agency{Reference=await NextReference(db,ct),CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow()};db.Add(row);}
            if(row.State!="draft")throw new AgencyCommandException(409,"agency-not-draft");
            if(draft.RelationshipManagerId is Guid manager&&!await Managers(db).AnyAsync(x=>x.Id==manager,ct))throw new AgencyCommandException(422,"invalid-relationship-manager");
            row.LegalName=draft.LegalName;row.NormalizedName=draft.NormalizedName;row.RegulatoryReference=draft.RegulatoryReference;row.RelationshipManagerId=draft.RelationshipManagerId;row.OnboardingStep=step;
            if(agencyId is not null)db.Entry(row).Property(x=>x.UpdatedAt).IsModified=true;
            var details=agencyId is null?null:await db.Set<AgencyOnboarding>().SingleOrDefaultAsync(x=>x.AgencyId==row.Id,ct);
            if(details is null){details=new AgencyOnboarding{AgencyId=row.Id,CreatedBy=actor.UserId};db.Add(details);}
            details.Details=draft.Json;
            if(products is not null)await SetProducts(db,row.Id,products,actor.UserId,ct);
            AddActivity(db,row.Id,actor.UserId,agencyId is null?"agency.created":"agency.draft-saved");
            await db.SaveChangesAsync(ct);
            return Outcome(row,agencyId is null?201:200);
        },token);
    }
    public async Task<CommandOutcome> ReplaceProducts(ActorContext actor,Guid agencyId,string key,byte[] version,IReadOnlyList<AgencyProductInput> products,string reason,CancellationToken token)
    {
        AgencyDraftRules.ValidateProducts(products);ValidateReason(reason);await Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agencies/{agencyId}/products",key,Guid.NewGuid()),new{products,reason},"agency.products-saved",async(db,ct)=>
        {
            var agency=await Lock(db,agencyId,version,ct);if(agency.State!="draft")throw new AgencyCommandException(409,"agency-not-draft");
            await SetProducts(db,agencyId,products,actor.UserId,ct);db.Entry(agency).Property(x=>x.UpdatedAt).IsModified=true;
            AddActivity(db,agencyId,actor.UserId,"agency.products-saved");await db.SaveChangesAsync(ct);return Outcome(agency,200);
        },token);
    }
    public async Task<CommandOutcome> Abandon(ActorContext actor,Guid agencyId,string key,byte[] version,string reason,CancellationToken token)
    {
        ValidateReason(reason);await Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agencies/{agencyId}/abandon",key,Guid.NewGuid()),new{reason},"agency.abandoned",async(db,ct)=>
        {
            var row=await Lock(db,agencyId,version,ct);if(row.State!="draft")throw new AgencyCommandException(409,"agency-not-draft");
            // No invitation tables/routes exist in 04-02. Their owning plan must
            // add staged/pending revocation to this same transaction before use.
            row.State="abandoned";AddActivity(db,agencyId,actor.UserId,"agency.abandoned");
            db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,EventType="agency.abandon-reason",OccurredAt=time.GetUtcNow(),CorrelationId=Guid.NewGuid(),After=JsonSerializer.Serialize(new{agencyId,reason})});
            await db.SaveChangesAsync(ct);return Outcome(row,200);
        },token);
    }
    internal async Task Authorize(ActorContext actor,Guid? agencyId,CancellationToken token)
    {
        if(!actor.HasCapability("agency-admin"))throw new AgencyCommandException(403,"agency-access-denied");
        await using var db=await factory.CreateDbContextAsync(token);
        if(!await (from user in db.Set<StaffUser>() join ur in db.Set<UserRole>() on user.Id equals ur.UserId join role in db.Set<Role>() on ur.RoleId equals role.Id
            where user.Id==actor.UserId&&user.State=="active"&&role.Scope=="internal"&&(role.Code=="agency-admin"||role.Code=="system-admin") select user.Id).AnyAsync(token))throw new AgencyCommandException(403,"agency-access-denied");
        if(agencyId is Guid id&&!await db.Set<Agency>().AnyAsync(x=>x.Id==id,token))throw new AgencyCommandException(404,"agency-not-found");
    }
    public static IQueryable<StaffUser> Managers(BackOfficeDbContext db)=>(from user in db.Set<StaffUser>() join ur in db.Set<UserRole>() on user.Id equals ur.UserId join role in db.Set<Role>() on ur.RoleId equals role.Id
        where user.State=="active"&&role.Scope=="internal"&&(role.Code=="agency-admin"||role.Code=="underwriter"||role.Code=="senior-underwriter") select user).Distinct();
    public static async Task<Agency> Lock(BackOfficeDbContext db,Guid id,byte[] expected,CancellationToken token)
    {
        var row=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM [Agency] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={id}").SingleOrDefaultAsync(token)??throw new AgencyCommandException(404,"agency-not-found");
        if(!CryptographicOperations.FixedTimeEquals(row.RowVersion,expected))throw new AgencyCommandException(412,"stale-agency");return row;
    }
    private static async Task SetProducts(BackOfficeDbContext db,Guid agencyId,IReadOnlyList<AgencyProductInput> products,Guid actor,CancellationToken token)
    {
        var ids=products.Select(x=>x.ProductVersionId).ToArray();
        var versions=await (from version in db.Set<ProductVersion>() join product in db.Set<Product>() on version.ProductId equals product.Id
            where ids.Contains(version.Id)&&(product.Code=="motor-trade-road-risks"||product.Code=="motor-trade-combined"||product.Code=="commercial-combined") select new{version.Id,version.ProductId}).ToListAsync(token);
        if(versions.Count!=ids.Length||versions.Select(x=>x.ProductId).Distinct().Count()!=ids.Length)throw new AgencyCommandException(422,"invalid-agency-products");
        var existing=await db.Set<AgencyDraftProduct>().Where(x=>x.AgencyId==agencyId).ToListAsync(token);
        foreach(var old in existing.Where(x=>!ids.Contains(x.ProductVersionId)))db.Remove(old);
        foreach(var input in products){var row=existing.SingleOrDefault(x=>x.ProductVersionId==input.ProductVersionId);if(row is null){row=new AgencyDraftProduct{AgencyId=agencyId,ProductVersionId=input.ProductVersionId,CreatedBy=actor};db.Add(row);}row.EffectiveFrom=input.EffectiveFrom;row.BrokerCommissionBasisPoints=input.BrokerCommissionBasisPoints;}
    }
    private void AddActivity(BackOfficeDbContext db,Guid agencyId,Guid actor,string action)=>db.Add(new AgencyActivity{AgencyId=agencyId,ActorId=actor,CreatedBy=actor,Action=action,OccurredAt=time.GetUtcNow()});
    private static void ValidateReason(string reason){if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason.Any(char.IsControl))throw new AgencyCommandException(422,"reason-required");}
    public static string Etag(byte[] version)=>"\""+Convert.ToBase64String(version)+"\"";
    private static CommandOutcome Outcome(Agency row,int status)=>new(row.Id,status,JsonSerializer.Serialize(new{id=row.Id}),Etag:Etag(row.RowVersion));
    public static async Task<string> NextReference(BackOfficeDbContext db,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Agency reference requires transaction.");
        await using var command=db.Database.GetDbConnection().CreateCommand();command.Transaction=db.Database.CurrentTransaction.GetDbTransaction();command.CommandText="SELECT NEXT VALUE FOR [AgencyReferenceSequence]";
        return "AG-"+Convert.ToInt64(await command.ExecuteScalarAsync(token),CultureInfo.InvariantCulture).ToString("D7",CultureInfo.InvariantCulture);
    }
}
