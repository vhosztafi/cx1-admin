using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlCommercialDemoExplicitAuthorityPreservesRevocation() => WithDatabase(async (db,password) =>
    {
        await DemoDatabase.SeedAsync(db,password,includeQuoteCapture:true,includeUnderwriting:true,includeCommercialCapture:true,includeCommercialUnderwriting:true);
        async Task<Guid> Seed() { await using var transaction=await db.Database.BeginTransactionAsync();var id=await CommercialDemoAuthoritySeed.SeedAsync(db,Now);await transaction.CommitAsync();return id; }
        var id=await Seed();var first=await db.Set<UserAuthorityGrant>().AsNoTracking().SingleAsync(x=>x.Id==id);
        Assert.Equal(id,await Seed());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={Now},RevokedBy={first.GrantedBy},RevocationReason=N'Operator revoked this demo grant' WHERE Id={id}");
        db.ChangeTracker.Clear();Assert.Equal(id,await Seed());
        var retained=await db.Set<UserAuthorityGrant>().AsNoTracking().SingleAsync(x=>x.Id==id);Assert.Equal(Now,retained.RevokedAt);
        Assert.Equal(1,await db.Set<UserAuthorityGrant>().CountAsync(x=>x.UserId==first.UserId&&x.AuthorityVersionId==first.AuthorityVersionId));
        Assert.Equal(first.EffectiveFrom,retained.EffectiveFrom);Assert.Equal(first.EffectiveTo,retained.EffectiveTo);
    });

    [Theory]
    [InlineData("admin-disabled")]
    [InlineData("senior-role-removed")]
    [InlineData("authority-retired")]
    public Task RealSqlCommercialDemoExplicitAuthorityChecksCurrentSetup(string denial) => WithDatabase(async (db,password) =>
    {
        await DemoDatabase.SeedAsync(db,password,includeQuoteCapture:true,includeUnderwriting:true,includeCommercialCapture:true,includeCommercialUnderwriting:true);
        var product=await db.Set<Product>().SingleAsync(x=>x.Code=="commercial-combined");
        await using(var setup=await db.Database.BeginTransactionAsync()){await CommercialDemoAuthoritySeed.SeedAsync(db,Now);await setup.CommitAsync();}
        if(denial=="admin-disabled")await db.Set<StaffUser>().Where(x=>x.Email=="system-admin@cover.example").ExecuteUpdateAsync(x=>x.SetProperty(u=>u.State,"suspended"));
        if(denial=="senior-role-removed")
        {
            var user=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="senior-underwriter@cover.example");var role=await db.Set<Role>().SingleAsync(x=>x.Code=="senior-underwriter");
            await db.Set<UserRole>().Where(x=>x.UserId==user.Id&&x.RoleId==role.Id).ExecuteDeleteAsync();
        }
        if(denial=="authority-retired")await db.Set<AuthorityVersion>().Where(x=>x.ProductId==product.Id).ExecuteUpdateAsync(x=>x.SetProperty(a=>a.State,"retired"));
        var count=await db.Set<UserAuthorityGrant>().CountAsync();db.ChangeTracker.Clear();
        await using var transaction=await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>CommercialDemoAuthoritySeed.SeedAsync(db,Now));
        Assert.Equal(count,await db.Set<UserAuthorityGrant>().CountAsync());
    });
}
