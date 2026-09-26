using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Persistence;
public sealed partial class BackOfficeDbContext
{
    private static void ConfigureAdministration(ModelBuilder model)
    {
        var actions=Record<IdentityAction>(model,"IdentityAction");
        Text(actions,("Kind",30),("SecurityStamp",100));Hash(actions,"TokenHash");
        actions.HasIndex(x=>x.TokenHash).IsUnique();actions.HasIndex(x=>new{x.UserId,x.Kind});
        actions.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.NoAction);
        Check(actions,"Kind","[Kind] IN ('invitation','password-reset','mfa-enrolment','mfa-login','mfa-recovery')");
        Check(actions,"Attempts","[Attempts] BETWEEN 0 AND 5");
        Check(actions,"Expiry","[ExpiresAt] > [CreatedAt]");
    }
}
