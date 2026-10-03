using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TechBazar.Domain.Enums;
using TechBazar.Infrastructure.Identity;

namespace TechBazar.Api.Extensions;

public static class Policies
{
    public const string Cors = "Frontend";
    public const string AuthRateLimit = "auth";
    public const string CatalogCache = "Catalog";
    public const string AutocompleteCache = "Autocomplete";
    public const string AdminOnly = "AdminOnly";
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJwtAuth(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        // Configured through options so the key is read lazily (user-secrets / env vars / test overrides).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((o, jwt) =>
            {
                var j = jwt.Value;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = j.Issuer,
                    ValidateAudience = true, ValidAudience = j.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(j.Key)),
                    ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name", RoleClaimType = "role",
                };
            });
        services.AddAuthorizationBuilder().AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin));
        return services;
    }

    public static IServiceCollection AddCorsFromConfig(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IConfiguration>((o, cfg) =>
        {
            var origins = cfg.GetSection("Cors:AllowedOrigins").Get<string[]>() is { Length: > 0 } o2 ? o2 : ["http://localhost:4200"];
            o.AddPolicy(Policies.Cors, p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Retry-After"));
        });
        return services;
    }

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((o, cfg) =>
        {
            var permit = cfg.GetValue("RateLimiting:Auth:PermitLimit", 10);
            var window = TimeSpan.FromSeconds(cfg.GetValue("RateLimiting:Auth:WindowSeconds", 60));

            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Policies.AuthRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = window, QueueLimit = 0, AutoReplenishment = true }));
            o.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var pds = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await pds.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = 429, Title = "Too many requests.", Detail = "Rate limit exceeded. Please retry later.",
                    },
                });
            };
        });
        return services;
    }

    public static IServiceCollection AddCatalogOutputCache(this IServiceCollection services)
    {
        services.AddOutputCache();
        services.AddOptions<OutputCacheOptions>().Configure<IConfiguration>((o, cfg) =>
        {
            var seconds = cfg.GetValue("OutputCache:CatalogSeconds", 60);
            var autoSeconds = cfg.GetValue("OutputCache:AutocompleteSeconds", 30);
            o.AddPolicy(Policies.CatalogCache, b => b.Expire(TimeSpan.FromSeconds(seconds)).SetVaryByQuery("*").Tag("catalog"));
            o.AddPolicy(Policies.AutocompleteCache, b => b.Expire(TimeSpan.FromSeconds(autoSeconds)).SetVaryByQuery("*").Tag("catalog"));
        });
        return services;
    }

    public static IServiceCollection AddSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "TechBazar BD API", Version = "v1",
                Description = "E-commerce API for computers & electronics retail in Bangladesh. Prices are in BDT.",
            });
            var scheme = new OpenApiSecurityScheme
            {
                Name = "Authorization", Description = "Paste the access token only (the 'Bearer ' prefix is added for you).",
                In = ParameterLocation.Header, Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            };
            c.AddSecurityDefinition("Bearer", scheme);
            c.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
        });
        return services;
    }
}
