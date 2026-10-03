using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Serilog;
using TechBazar.Api.Extensions;
using TechBazar.Api.Filters;
using TechBazar.Api.Middleware;
using TechBazar.Application;
using TechBazar.Infrastructure;
using TechBazar.Infrastructure.Persistence;
using TechBazar.Infrastructure.Seeding;
using TechBazar.Infrastructure.Storage;
using TechBazar.Api.Controllers;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddCatalogOutputCache();
builder.Services.AddSwagger();
builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("database");

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();           // 401/403/404/429 without a body -> ProblemDetails
app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "TechBazar BD API v1");
    c.DocumentTitle = "TechBazar BD API";
});

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
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
        ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    },
});

app.UseCors(Policies.Cors);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

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
