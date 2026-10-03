using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
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
    public const string PublicRateLimit = "public";
    public const string CheckoutRateLimit = "checkout";
    public const string RecoveryRateLimit = "recovery";
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
                    ValidateIssuerSigningKey = true, RequireSignedTokens = true, RequireExpirationTime = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],   // no algorithm confusion ("none", RS256 with the key as a public key, ...)
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(j.Key)),
                    ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name", RoleClaimType = "role",
                };
            });
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin).AddRequirements(new MfaRequirement()))
            // Deny by default: an endpoint that forgets [Authorize] is NOT public. Anonymous endpoints must say [AllowAnonymous].
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddSingleton<IAuthorizationHandler, MfaAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, MfaAuthorizationResultHandler>();
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
            // Blanket per-client ceiling for the whole API (anonymous: per IP, signed-in: per user) on top of the stricter
            // auth / public policies. Protects the expensive anonymous endpoints (search, facets, builder evaluate).
            var globalPermit = cfg.GetValue("RateLimiting:Global:PermitLimit", 600);
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                if (!ctx.Request.Path.StartsWithSegments("/api")) return RateLimitPartition.GetNoLimiter("static");
                var key = ctx.User.FindFirst("sub")?.Value is { } sub ? "u:" + sub : "ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                return RateLimitPartition.GetFixedWindowLimiter(key,
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = globalPermit, Window = TimeSpan.FromSeconds(60), QueueLimit = 0, AutoReplenishment = true });
            });
            o.AddPolicy(Policies.AuthRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = window, QueueLimit = 0, AutoReplenishment = true }));
            // Anonymous write-ish endpoints (payment callbacks, saving builds): generous but bounded per IP.
            var publicPermit = cfg.GetValue("RateLimiting:Public:PermitLimit", 60);
            o.AddPolicy(Policies.PublicRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                "pub:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = publicPermit, Window = window, QueueLimit = 0, AutoReplenishment = true }));
            // Pricing / ordering endpoints: a human checks out a handful of times a minute. Per signed-in user (falls back to IP), this also stops
            // a logged-in account from brute-forcing coupon codes through the quote endpoint.
            var checkoutPermit = cfg.GetValue("RateLimiting:Checkout:PermitLimit", 30);
            o.AddPolicy(Policies.CheckoutRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                "co:" + (ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = checkoutPermit, Window = window, QueueLimit = 0, AutoReplenishment = true }));
            // Emails that can be triggered by anyone (forgot password, resend verification): few per client, long window, so the endpoint cannot
            // be used to flood an inbox or to probe at scale. (The per-ACCOUNT hourly cap lives in AccountService.)
            var recoveryPermit = cfg.GetValue("RateLimiting:Recovery:PermitLimit", 5);
            var recoveryWindow = TimeSpan.FromSeconds(cfg.GetValue("RateLimiting:Recovery:WindowSeconds", 900));
            o.AddPolicy(Policies.RecoveryRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                "rec:" + (ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = recoveryPermit, Window = recoveryWindow, QueueLimit = 0, AutoReplenishment = true }));
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

    /// <summary>
    /// Trusts X-Forwarded-For / -Proto ONLY from configured proxies (<c>ForwardedHeaders:KnownProxies</c> / <c>KnownNetworks</c> in CIDR form).
    /// Without this the API sees the SSR server's address for every visitor, so per-IP rate limits and audit IPs collapse into one bucket;
    /// trusting the headers from anyone would instead let clients spoof their IP and dodge the limits.
    /// </summary>
    public static IServiceCollection AddForwardedHeadersFromConfig(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = null;               // walk the chain from the right until the first untrusted hop

            var proxies = (cfg.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
                .Select(ip => IPAddress.TryParse(ip, out var a) ? a : null).OfType<IPAddress>().ToList();
            var networks = new List<Microsoft.AspNetCore.HttpOverrides.IPNetwork>();
            foreach (var net in cfg.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
            {
                var parts = net.Split('/');
                if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix) && int.TryParse(parts[1], out var bits))
                    networks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, bits));
            }

            // SECURITY: an EMPTY trust list means "trust every sender" to the middleware. So when nothing is configured we must keep the framework's
            // default (loopback only) instead of clearing it; otherwise any client could spoof X-Forwarded-For and dodge the per-IP rate limits.
            if (proxies.Count == 0 && networks.Count == 0) return;
            o.KnownProxies.Clear();
            o.KnownNetworks.Clear();
            foreach (var p in proxies) o.KnownProxies.Add(p);
            foreach (var n in networks) o.KnownNetworks.Add(n);
        });
        return services;
    }

    public static IServiceCollection AddApiCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;   // auth responses (tokens) are excluded in the pipeline to stay clear of BREACH-style attacks
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            o.MimeTypes = ["application/json", "application/problem+json", "image/svg+xml", "text/plain"];
        });
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
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
