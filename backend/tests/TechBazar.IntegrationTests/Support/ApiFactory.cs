using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TechBazar.Application.Email;
using TechBazar.Application.Payments;
using TechBazar.Infrastructure.Payments;
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
    private readonly IReadOnlyDictionary<string, string?> _extra;

    public ApiFactory() : this(1000, null) { }

    private ApiFactory(int authPermitLimit, IReadOnlyDictionary<string, string?>? extra)
    {
        _authPermitLimit = authPermitLimit;
        _extra = extra ?? new Dictionary<string, string?>();
    }

    /// <summary>Factory with a tight auth rate limit, for rate-limiter tests.</summary>
    public static ApiFactory WithAuthLimit(int permits) => new(permits, null);

    /// <summary>Factory with extra / overriding configuration (e.g. a tiny global rate limit, secure cookies).</summary>
    public static ApiFactory WithConfig(params (string Key, string Value)[] settings) =>
        new(1000, settings.ToDictionary(x => x.Key, x => (string?)x.Value));

    public const string AdminEmail = "admin@techbazar.test";
    public const string AdminPassword = "AdminPassw0rd!";
    public const string TestJwtKey = "integration-tests-signing-key-0123456789-abcdef";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "tb-it-uploads-" + Guid.NewGuid().ToString("N"));

    public CapturingEmailSender Emails { get; } = new();
    public string UploadsPath => _uploads;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = TestJwtKey,
            ["RateLimiting:Auth:PermitLimit"] = _authPermitLimit.ToString(),
            ["RateLimiting:Auth:WindowSeconds"] = "60",
            ["RateLimiting:Public:PermitLimit"] = "10000",
            ["RateLimiting:Global:PermitLimit"] = "100000",
            ["Storage:RootPath"] = _uploads,
            ["Payments:PublicApiBaseUrl"] = "https://api.test",
            ["Payments:StorefrontBaseUrl"] = "https://shop.test",
            ["Seed:Enabled"] = "false",
            ["Seed:ApplyMigrations"] = "false",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = AdminPassword,
            ["ConnectionStrings:DefaultConnection"] = "unused-in-tests",
        }));
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(_extra));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway, CashOnDeliveryGateway>();
            services.AddSingleton<IPaymentGateway, FakeOnlineGateway>();
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
        if (!disposing) return;
        _connection.Dispose();
        try { if (Directory.Exists(_uploads)) Directory.Delete(_uploads, true); } catch { /* best effort */ }
    }
}
