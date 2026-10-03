using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Auth;
using TechBazar.Application.Email;
using TechBazar.Application.Orders;
using TechBazar.Application.Payments;
using TechBazar.Application.Storage;
using TechBazar.Infrastructure.Email;
using TechBazar.Infrastructure.Payments;
using TechBazar.Infrastructure.Storage;
using TechBazar.Infrastructure.Identity;
using TechBazar.Infrastructure.Persistence;
using TechBazar.Infrastructure.Seeding;

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

        services.AddOptions<ShippingOptions>().Bind(configuration.GetSection(ShippingOptions.SectionName));
        services.AddOptions<PaymentOptions>().Bind(configuration.GetSection(PaymentOptions.SectionName));
        services.AddOptions<SslCommerzOptions>().Bind(configuration.GetSection(SslCommerzOptions.SectionName));
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName));

        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IPaymentGateway, CashOnDeliveryGateway>();
        services.AddHttpClient<SslCommerzGateway>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddTransient<IPaymentGateway>(sp => sp.GetRequiredService<SslCommerzGateway>());

        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<DataSeeder>();
        return services;
    }
}
