using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TechBazar.Infrastructure.Persistence;
using TechBazar.Infrastructure.Seeding;

namespace TechBazar.IntegrationTests.Support;

/// <summary>
/// Boots the real API pipeline (middleware, auth, rate limiting, output cache, validation) against a SQLite in-memory
/// database seeded with the real catalog. TEST ONLY; production configuration stays on SQL Server.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly int _authPermitLimit;

    public ApiFactory() : this(1000) { }

    private ApiFactory(int authPermitLimit) => _authPermitLimit = authPermitLimit;

    /// <summary>Factory with a tight auth rate limit, for rate-limiter tests.</summary>
    public static ApiFactory WithAuthLimit(int permits) => new(permits);

    public const string AdminEmail = "admin@techbazar.test";
    public const string AdminPassword = "AdminPassw0rd!";
    public const string TestJwtKey = "integration-tests-signing-key-0123456789-abcdef";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = TestJwtKey,
            ["RateLimiting:Auth:PermitLimit"] = _authPermitLimit.ToString(),
            ["RateLimiting:Auth:WindowSeconds"] = "60",
            ["Seed:Enabled"] = "false",
            ["Seed:ApplyMigrations"] = "false",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = AdminPassword,
            ["ConnectionStrings:DefaultConnection"] = "unused-in-tests",
        }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            _connection.Open();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
        scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync().GetAwaiter().GetResult();
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
