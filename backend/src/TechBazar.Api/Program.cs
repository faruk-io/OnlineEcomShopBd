using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Serilog;
using TechBazar.Api.Controllers;
using TechBazar.Api.Extensions;
using TechBazar.Api.Filters;
using TechBazar.Api.Middleware;
using TechBazar.Application;
using TechBazar.Infrastructure;
using TechBazar.Infrastructure.Persistence;
using TechBazar.Infrastructure.Seeding;
using TechBazar.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;                              // do not advertise the server software
    k.Limits.MaxRequestBodySize = 2 * 1024 * 1024;          // JSON APIs never need more; the image upload endpoint raises it for itself
    k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
});

builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddControllers(o =>
    {
        o.Filters.Add<ValidationFilter>();
        o.Conventions.Add(new RouteTokenTransformerConvention(new LowercaseRouteTransformer()));
    })
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddApplication();
builder.Services.AddScoped<CatalogCache>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.PostConfigure<StorageOptions>(o => o.RootPath ??= Path.Combine(builder.Environment.ContentRootPath, "uploads"));
builder.Services.AddJwtAuth();
builder.Services.AddCorsFromConfig();
builder.Services.AddAppRateLimiting();
builder.Services.AddForwardedHeadersFromConfig(builder.Configuration);
builder.Services.AddApiCompression();
builder.Services.AddCatalogOutputCache();
builder.Services.AddSwagger();
builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("database");

var app = builder.Build();

// Must be first: everything after (rate limiting, https redirection, logging) should see the real client address/scheme.
app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();           // 401/403/404/429 without a body -> ProblemDetails
app.UseSerilogRequestLogging();

// API documentation is an information leak in production (every route, DTO and auth scheme): development, or opt in with Swagger:Enabled=true.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TechBazar BD API v1");
        c.DocumentTitle = "TechBazar BD API";
    });
}

if (!app.Environment.IsDevelopment()) app.UseHsts();
// TLS is normally terminated by a reverse proxy / load balancer in front of the container; turn this off there (Security:HttpsRedirection=false).
if (app.Configuration.GetValue("Security:HttpsRedirection", true)) app.UseHttpsRedirection();

// Compression sits outside output caching (entries are cached uncompressed) and skips the token-bearing auth endpoints.
app.UseWhen(c => !c.Request.Path.StartsWithSegments("/api/v1/auth"), b => b.UseResponseCompression());
// Admin-uploaded images. Served with nosniff + long cache; the file names are server generated GUIDs.
var storage = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
Directory.CreateDirectory(storage.RootPath!);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storage.RootPath!),
    RequestPath = storage.RequestPath,
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Even if a hostile file slipped through, a browser must not render or script it.
        ctx.Context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        ctx.Context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-site";
        ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    },
});

app.UseCors(Policies.Cors);
app.UseAuthentication();
// After authentication on purpose: the per-user rate-limit partitions (checkout, global) need to know who is calling.
app.UseRateLimiter();
app.UseAuthorization();
app.UseOutputCache();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

if (app.Environment.IsProduction() && app.Services.GetRequiredService<TechBazar.Application.Email.IEmailSender>() is TechBazar.Infrastructure.Email.LoggingEmailSender)
    app.Logger.LogWarning("Email:Smtp:Host is not configured: password-reset and verification emails are only logged, never delivered. " +
                          "Configure SMTP (see docs/security.md and README) before going live.");

if (app.Environment.IsProduction() && !app.Configuration.GetSection("ForwardedHeaders:KnownProxies").GetChildren().Any()
    && !app.Configuration.GetSection("ForwardedHeaders:KnownNetworks").GetChildren().Any())
    app.Logger.LogWarning("ForwardedHeaders:KnownProxies/KnownNetworks are not configured: behind a reverse proxy every client shares the proxy's IP, " +
                          "so per-IP rate limits become global. Configure the proxy addresses (see docs/security.md).");

await InitialiseDatabaseAsync(app);
app.Run();

static async Task InitialiseDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var opt = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
    if (opt.ApplyMigrations)
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    if (opt.Enabled)
        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
}

public partial class Program;
