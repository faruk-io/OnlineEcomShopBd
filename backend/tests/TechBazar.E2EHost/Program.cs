// TEST ONLY - SQLite, never deploy.
// Boots the REAL TechBazar API pipeline (controllers, filters, middleware, auth, rate limiting, output cache) on a
// throw-away SQLite file so the Playwright suite in /frontend/e2e can exercise the storefront end to end without
// SQL Server. Keep the wiring in sync with src/TechBazar.Api/Program.cs.
//   E2E_API_PORT  listen port (default 5180)
//   E2E_DB        SQLite file path (default <tmp>/techbazar-e2e.db); deleted and recreated at every start
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using TechBazar.Api.Controllers;
using TechBazar.Api.Extensions;
using TechBazar.Api.Filters;
using TechBazar.Api.Middleware;
using TechBazar.Application;
using TechBazar.Infrastructure;
using TechBazar.Infrastructure.Persistence;
using TechBazar.Infrastructure.Seeding;

var port = Environment.GetEnvironmentVariable("E2E_API_PORT") ?? "5180";
var dbPath = Environment.GetEnvironmentVariable("E2E_DB") ?? Path.Combine(Path.GetTempPath(), "techbazar-e2e.db");
var uploads = Path.Combine(Path.GetTempPath(), "techbazar-e2e-uploads");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://localhost:{port}");
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Jwt:Key"] = "e2e-test-only-signing-key-0123456789-abcdefghij",
    ["Seed:AdminPassword"] = "AdminPassw0rd!",
    ["ConnectionStrings:DefaultConnection"] = "unused-in-e2e",
    ["RateLimiting:Auth:PermitLimit"] = "100000",
    ["RateLimiting:Public:PermitLimit"] = "100000",
    ["RateLimiting:Global:PermitLimit"] = "1000000",
    ["Storage:RootPath"] = uploads,
});

builder.Services.AddControllers(o =>
    {
        o.Filters.Add<ValidationFilter>();
        o.Conventions.Add(new RouteTokenTransformerConvention(new LowercaseRouteTransformer()));
    })
    .AddApplicationPart(typeof(ProductsController).Assembly) // controllers live in TechBazar.Api, not in this host
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApplication();
builder.Services.AddScoped<CatalogCache>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuth();
builder.Services.AddCorsFromConfig();
builder.Services.AddAppRateLimiting();
builder.Services.AddForwardedHeadersFromConfig(builder.Configuration);
builder.Services.AddApiCompression();
builder.Services.AddCatalogOutputCache();

// Swap SQL Server for a fresh SQLite file (tests only).
foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" }) if (File.Exists(f)) File.Delete(f);
var cs = $"DataSource={dbPath};Pooling=False;Default Timeout=30";
using (var init = new SqliteConnection(cs))
{
    init.Open();
    using var cmd = init.CreateCommand();
    cmd.CommandText = "PRAGMA journal_mode=WAL;";
    cmd.ExecuteNonQuery();
}
builder.Services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
builder.Services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(cs));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
// No HTTPS redirection / HSTS here: the host is plain http on localhost.
app.UseWhen(c => !c.Request.Path.StartsWithSegments("/api/v1/auth"), b => b.UseResponseCompression());
Directory.CreateDirectory(uploads);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(uploads), RequestPath = "/uploads" });
app.UseCors(Policies.Cors);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
}
app.Run();
