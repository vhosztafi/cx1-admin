using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

// Explicit local fixture setup, never called by application startup/initialization.
public static class CommercialDemoAuthoritySeed
{
    public static async Task<Guid> SeedAsync(BackOfficeDbContext db, DateTimeOffset now, CancellationToken token = default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Explicit demo authority setup requires a transaction.");
        await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'CoverMGA.CommercialDemoAuthority',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @result<0 THROW 51972,'Commercial demo authority setup is busy.',1;",token);
        async Task<StaffUser> User(string role)
        {
            var user=await db.Set<StaffUser>().SingleOrDefaultAsync(x=>x.Email==role+"@cover.example"&&x.State=="active"&&x.AgencyId==null,token)
                ??throw new InvalidOperationException("Current internal demo staff are required.");
            if(!await (from link in db.Set<UserRole>() join item in db.Set<Role>() on link.RoleId equals item.Id where link.UserId==user.Id&&item.Code==role select link.Id).AnyAsync(token))
                throw new InvalidOperationException("Current demo staff role is required.");
            return user;
        }
        var admin=await User("system-admin");var senior=await User("senior-underwriter");
        var authority=await (from row in db.Set<AuthorityVersion>() join product in db.Set<Product>() on row.ProductId equals product.Id
            join version in db.Set<ProductVersion>() on row.ProductVersionId equals version.Id
            join binder in db.Set<BinderVersion>() on row.BinderVersionId equals binder.Id
            where product.Code=="commercial-combined"&&version.Version==3&&version.State=="published"&&row.State=="published"&&binder.State=="published"
                &&row.EffectiveFrom<=now&&now<row.EffectiveTo&&version.EffectiveFrom<=now&&now<version.EffectiveTo&&binder.EffectiveFrom<=now&&now<binder.EffectiveTo
            select row).SingleOrDefaultAsync(token)??throw new InvalidOperationException("Current published commercial demo authority is required.");
        // Any existing grant, including a revoked/expired one, remains authoritative.
        var existing=await db.Set<UserAuthorityGrant>().Where(x=>x.UserId==senior.Id&&x.AuthorityVersionId==authority.Id).OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).FirstOrDefaultAsync(token);
        if(existing is not null)return existing.Id;
        var grant=new UserAuthorityGrant{UserId=senior.Id,AuthorityVersionId=authority.Id,GrantedBy=admin.Id,CreatedBy=admin.Id,CreatedAt=now,
            EffectiveFrom=now,EffectiveTo=authority.EffectiveTo,Reason="Explicit local commercial demo setup; preserve subsequent role changes and revocation."};
        db.Add(grant);await db.SaveChangesAsync(token);return grant.Id;
    }
}
