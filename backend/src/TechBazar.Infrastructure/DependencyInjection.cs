using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Auth;
using TechBazar.Application.Email;
using TechBazar.Application.Orders;
using TechBazar.Application.Payments;
using TechBazar.Application.Storage;
using TechBazar.Infrastructure.Email;
using TechBazar.Infrastructure.Identity;
using TechBazar.Infrastructure.Payments;
using TechBazar.Infrastructure.Persistence;
using TechBazar.Infrastructure.Seeding;
using TechBazar.Infrastructure.Storage;

namespace TechBazar.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolved lazily from IConfiguration so test hosts can override settings.
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
                     ?? throw new InvalidOperationException(
                         "ConnectionStrings:DefaultConnection is not configured (appsettings.Development.json or user-secrets).");
            options.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(3));
        });
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.SectionName));

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddOptions<TechBazar.Application.Auth.AccountOptions>().Bind(configuration.GetSection(TechBazar.Application.Auth.AccountOptions.SectionName));
        services.AddScoped<TechBazar.Application.Auth.IAccountService, AccountService>();
        services.AddSingleton<AccountJobQueue>();
        services.AddSingleton<TechBazar.Application.Auth.IAccountJobs>(sp => sp.GetRequiredService<AccountJobQueue>());
        services.AddHostedService<AccountJobWorker>();
        services.AddOptions<ShippingOptions>().Bind(configuration.GetSection(ShippingOptions.SectionName));
        services.AddOptions<PaymentOptions>().Bind(configuration.GetSection(PaymentOptions.SectionName));
        services.AddOptions<SslCommerzOptions>().Bind(configuration.GetSection(SslCommerzOptions.SectionName));
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName));

        // Real SMTP when Email:Smtp:Host is configured, otherwise the log-only sender (development). Validated at startup.
        services.AddOptions<SmtpOptions>().Bind(configuration.GetSection(SmtpOptions.SectionName))
            .Validate(SmtpOptions.IsValid, "Email:Smtp is incomplete or insecure: needs a valid FromAddress, a sane port/timeout, and TLS when a Username is set.")
            .ValidateOnStart();
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<IOptions<SmtpOptions>>().Value.IsConfigured
            ? ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp)
            : ActivatorUtilities.CreateInstance<LoggingEmailSender>(sp));
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IPaymentGateway, CashOnDeliveryGateway>();
        // The validation call carries store_passwd in its query string (SSLCommerz API design): never let the HTTP client log that URL.
        services.AddHttpClient<SslCommerzGateway>(c => c.Timeout = TimeSpan.FromSeconds(20)).RemoveAllLoggers();
        services.AddTransient<IPaymentGateway>(sp => sp.GetRequiredService<SslCommerzGateway>());

        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<DataSeeder>();
        return services;
    }
}
