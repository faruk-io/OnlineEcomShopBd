using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TechBazar.Application;
using TechBazar.Application.Email;
using TechBazar.Application.Orders;
using TechBazar.Application.Payments;
using TechBazar.Infrastructure.Identity;
using TechBazar.Infrastructure.Persistence;
using TechBazar.Infrastructure.Seeding;

namespace TechBazar.UnitTests.Support;

/// <summary>
/// SQLite in-memory database seeded with the real catalog seed data. TEST ONLY: production uses SQL Server.
/// One instance per test class (xunit IClassFixture) to keep the suite fast; tests must not mutate seeded data
/// (tests that write create their own <see cref="TestDatabase"/>).
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    public ServiceProvider Services { get; }

    public TestDatabase() : this(true) { }

    private TestDatabase(bool seed)
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<TechBazar.Application.Abstractions.IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.Configure<SeedOptions>(o => { o.Enabled = true; });
        services.AddScoped<DataSeeder>();
        services.AddApplication();
        services.AddOptions<ShippingOptions>();
        services.Configure<PaymentOptions>(o => { o.PublicApiBaseUrl = "https://api.test"; o.StorefrontBaseUrl = "https://shop.test"; });
        services.AddSingleton<CapturingEmailSender>();
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<CapturingEmailSender>());
        services.AddSingleton<FakeOnlineGateway>();
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<FakeOnlineGateway>());
        services.AddSingleton<IPaymentGateway, TechBazar.Infrastructure.Payments.CashOnDeliveryGateway>();
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
        if (seed) scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync().GetAwaiter().GetResult();
    }

    public CapturingEmailSender Emails => Services.GetRequiredService<CapturingEmailSender>();
    public FakeOnlineGateway Gateway => Services.GetRequiredService<FakeOnlineGateway>();

    public IServiceScope CreateScope() => Services.CreateScope();

    public void Dispose()
    {
        Services.Dispose();
        _connection.Dispose();
    }
}
