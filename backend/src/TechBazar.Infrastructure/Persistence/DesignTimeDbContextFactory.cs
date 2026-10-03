using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TechBazar.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef`. Reads TECHBAZAR_CONNECTION if set, otherwise targets SQL Server LocalDB / default instance.
/// No secrets are stored here (Windows auth).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TECHBAZAR_CONNECTION")
                 ?? "Server=(localdb)\\MSSQLLocalDB;Database=TechBazarBD;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(cs).Options;
        return new ApplicationDbContext(options);
    }
}
