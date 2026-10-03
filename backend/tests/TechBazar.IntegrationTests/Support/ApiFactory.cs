using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    private readonly Action<IServiceCollection>? _services;

    public ApiFactory() : this(1000, null) { }

    private ApiFactory(int authPermitLimit, IReadOnlyDictionary<string, string?>? extra, Action<IServiceCollection>? services = null)
    {
        _authPermitLimit = authPermitLimit;
        _extra = extra ?? new Dictionary<string, string?>();
        _services = services;
    }

    /// <summary>Factory with a tight auth rate limit, for rate-limiter tests.</summary>
    public static ApiFactory WithAuthLimit(int permits) => new(permits, null);

    /// <summary>Factory whose service container is adjusted after the app's own registrations (e.g. to stub a dependency).</summary>
    public static ApiFactory WithServices(Action<IServiceCollection> services) => new(1000, null, services);

    /// <summary>Factory with extra / overriding configuration (e.g. a tiny global rate limit, secure cookies).</summary>
    public static ApiFactory WithConfig(params (string Key, string Value)[] settings) =>
        new(1000, settings.ToDictionary(x => x.Key, x => (string?)x.Value));

    public const string AdminEmail = "admin@techbazar.test";
    public const string AdminPassword = "AdminPassw0rd!";
    public const string TestJwtKey = "integration-tests-signing-key-0123456789-abcdef";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "tb-it-uploads-" + Guid.NewGuid().ToString("N"));

    public CapturingEmailSender Emails { get; } = new();
    /// <summary>Counts every SQL command the API sends, so tests can budget queries per endpoint and catch N+1 patterns.</summary>
    public SqlCommandCounter Sql { get; } = new();
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
            ["RateLimiting:Recovery:PermitLimit"] = "100000",
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
            _services?.Invoke(services);
            _connection.Open();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection).AddInterceptors(Sql));
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

/// <summary>EF Core interceptor that records the SQL text of each command (reader, scalar and non-query).</summary>
public sealed class SqlCommandCounter : DbCommandInterceptor
{
    private readonly List<string> _commands = [];
    private readonly object _gate = new();

    public IReadOnlyList<string> Commands { get { lock (_gate) return [.. _commands]; } }
    public int Count { get { lock (_gate) return _commands.Count; } }
    private void Record(DbCommand c) { lock (_gate) _commands.Add(c.CommandText); }
    public void Reset() { lock (_gate) _commands.Clear(); }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default) { Record(command); return new(result); }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Record(command); return result; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default) { Record(command); return new(result); }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) { Record(command); return result; }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken ct = default) { Record(command); return new(result); }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) { Record(command); return result; }
}
