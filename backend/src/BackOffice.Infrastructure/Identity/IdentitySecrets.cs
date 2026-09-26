using System.Security.Cryptography;
using System.Text;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Identity;

public sealed class IdentitySecrets(IDataProtectionProvider protection,TimeProvider time)
{
    private readonly IDataProtector protector=protection.CreateProtector("CoverMGA.AccountSecurity.v1");
    public byte[] Protect(string value)=>protector.Protect(Encoding.UTF8.GetBytes(value));
    public string Unprotect(byte[] value)=>Encoding.UTF8.GetString(protector.Unprotect(value));
    public static string Token()=>WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public static byte[] Hash(string value)=>SHA256.HashData(Encoding.UTF8.GetBytes(value));
    public async Task<(IdentityAction Action,string Token)> Issue(BackOfficeDbContext db,StaffUser user,string kind,TimeSpan lifetime,bool delivery,CancellationToken ct)
    {
        var now=time.GetUtcNow();
        await db.Set<IdentityAction>().Where(x=>x.UserId==user.Id&&x.Kind==kind&&x.ConsumedAt==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,now).SetProperty(x=>x.SecretCiphertext,(byte[]?)null),ct);
        var token=Token();var row=new IdentityAction{UserId=user.Id,Kind=kind,TokenHash=Hash(token),SecurityStamp=user.SecurityStamp,
            CreatedAt=now,UpdatedAt=now,ExpiresAt=now.Add(lifetime),SecretCiphertext=delivery?Protect(token):null};db.Add(row);return(row,token);
    }
    public static async Task Invalidate(BackOfficeDbContext db,StaffUser user,DateTimeOffset now,CancellationToken ct)
    {
        user.SecurityStamp=Guid.NewGuid().ToString("N");
        await db.Set<UserSession>().Where(x=>x.UserId==user.Id&&x.RevokedAt==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,now),ct);
        await db.Set<IdentityAction>().Where(x=>x.UserId==user.Id&&x.ConsumedAt==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,now).SetProperty(x=>x.SecretCiphertext,(byte[]?)null),ct);
    }
    public static async Task<StaffUser> HoldUser(BackOfficeDbContext db,Guid id,CancellationToken ct)
        =>await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync(ct)??throw new QuoteOperationException(404,"user-not-found");
}
