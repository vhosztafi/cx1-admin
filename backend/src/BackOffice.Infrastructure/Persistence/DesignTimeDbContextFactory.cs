using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackOffice.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BackOfficeDbContext>
{
    public BackOfficeDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<BackOfficeDbContext>()
        .UseSqlServer("Server=.\\SQL2022;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true", sql => sql.UseCompatibilityLevel(160))
        .Options);
}
