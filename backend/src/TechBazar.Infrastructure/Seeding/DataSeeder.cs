using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechBazar.Domain.Common;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;
using TechBazar.Infrastructure.Identity;
using TechBazar.Infrastructure.Persistence;

namespace TechBazar.Infrastructure.Seeding;

/// <summary>Idempotent: each block only runs when its table is empty, so re-running never duplicates or overwrites data.</summary>
public sealed partial class DataSeeder(
    ApplicationDbContext db,
    RoleManager<ApplicationRole> roles,
    UserManager<ApplicationUser> users,
    IOptions<SeedOptions> options,
    ILogger<DataSeeder> logger)
{
    private readonly SeedOptions _opt = options.Value;

    /// <summary>Spec keys exposed as catalog filters/facets.</summary>
    internal static readonly HashSet<string> FilterableKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Series", "Generation", "Socket", "Chipset", "Form Factor", "RAM Type", "Capacity", "Interface", "GPU Chipset",
        "Video Memory", "Wattage", "Efficiency", "Modular", "Screen Size", "Resolution", "Panel Type", "Refresh Rate",
        "Cores", "Storage Type", "Storage Capacity", "Supported Motherboards",
    };

    /// <summary>Spec keys whose leading number is stored in NumericValue (normalised: TB -> GB, units dropped).</summary>
    internal static readonly HashSet<string> NumericKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "TDP", "Wattage", "Recommended PSU", "Cores", "Threads", "Capacity", "Storage Capacity", "Video Memory",
        "Screen Size", "Refresh Rate", "Speed", "Output Power", "Max GPU Length", "Length", "Max Memory",
    };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedRolesAndAdminAsync();
        if (!await db.Categories.AnyAsync(ct)) await SeedCatalogAsync(ct);
        if (!await db.Coupons.AnyAsync(ct)) await SeedCouponsAsync(ct);
    }

    private async Task SeedRolesAndAdminAsync()
    {
        foreach (var role in new[] { Roles.Customer, Roles.Admin })
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new ApplicationRole(role));

        if (string.IsNullOrWhiteSpace(_opt.AdminPassword))
        {
            logger.LogInformation("Seed:AdminPassword not set (user-secrets); skipping admin account creation.");
            return;
        }
        if (await users.FindByEmailAsync(_opt.AdminEmail) is not null) return;

        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = _opt.AdminEmail, Email = _opt.AdminEmail, EmailConfirmed = true,
            FullName = _opt.AdminFullName, CreatedAt = DateTime.UtcNow,
        };
        var result = await users.CreateAsync(admin, _opt.AdminPassword);
        if (!result.Succeeded)
        {
            logger.LogError("Admin seed failed: {Errors}", string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }
        await users.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Seeded admin account {Email}", _opt.AdminEmail);
    }

    private async Task SeedCatalogAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Categories (parents are listed before children).
        var cats = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        foreach (var c in CatalogSeedData.Categories)
        {
            var cat = new Category
            {
                Name = c.Name, Slug = SlugHelper.Slugify(c.Name), Description = c.Description, DisplayOrder = order++,
                Parent = c.Parent is null ? null : cats[c.Parent],
                ImageUrl = $"/images/categories/{SlugHelper.Slugify(c.Name)}.svg",
            };
            cats[c.Name] = cat;
            db.Categories.Add(cat);
        }

        var brands = CatalogSeedData.Brands.ToDictionary(b => b.Name, b => new Brand
        {
            Name = b.Name, Slug = SlugHelper.Slugify(b.Name), Description = b.Description,
            LogoUrl = $"/images/brands/{SlugHelper.Slugify(b.Name)}.svg",
        });
        db.Brands.AddRange(brands.Values);

        // Deterministic pseudo-random popularity/age so listings sort the same on every machine.
        var rnd = new Random(42);
        var skuCounters = new Dictionary<string, int>();
        foreach (var p in CatalogSeedData.Products)
        {
            var category = cats[p.Category];
            var brand = brands[p.Brand];
            var code = SkuPrefix(category.Slug);
            skuCounters[code] = skuCounters.GetValueOrDefault(code) + 1;

            var sold = rnd.Next(5, 400);
            var product = new Product
            {
                Name = p.Name,
                Slug = SlugHelper.Slugify(p.Name),
                Sku = $"TB-{code}-{skuCounters[code]:0000}",
                ShortDescription = p.Features.Split('|')[0],
                Description = $"{p.Name} by {brand.Name}. {string.Join(". ", p.Features.Split('|'))}. " +
                              $"Sold with {WarrantyText(p.WarrantyMonths)} from TechBazar BD.",
                Price = p.Price,
                DiscountPrice = p.Sale,
                StockStatus = p.Stock,
                StockQuantity = p.Stock == StockStatus.InStock ? rnd.Next(3, 60) : 0,
                WarrantyMonths = p.WarrantyMonths,
                WarrantyDetails = WarrantyText(p.WarrantyMonths),
                SoldCount = sold,
                ViewCount = sold * rnd.Next(8, 30),
                IsFeatured = p.Featured,
                Category = category,
                Brand = brand,
                CreatedAt = now.AddDays(-rnd.Next(1, 240)),
            };
            product.RecalculateEffectivePrice();

            product.Images.Add(new ProductImage
            {
                Url = $"/images/placeholders/{category.Slug}.svg", AltText = p.Name, IsPrimary = true,
            });

            var featureOrder = 0;
            foreach (var f in p.Features.Split('|'))
                product.KeyFeatures.Add(new ProductKeyFeature { Text = f, DisplayOrder = featureOrder++ });

            var specOrder = 0;
            foreach (var line in p.Specs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var sep = line.IndexOf('|');
                var group = line[..sep];
                foreach (var pair in line[(sep + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var eq = pair.IndexOf('=');
                    var key = pair[..eq].Trim();
                    var value = pair[(eq + 1)..].Trim();
                    product.Specifications.Add(new ProductSpecification
                    {
                        Group = group, Key = key, Value = value,
                        NumericValue = NumericKeys.Contains(key) ? ParseNumeric(value) : null,
                        IsFilterable = FilterableKeys.Contains(key),
                        DisplayOrder = specOrder++,
                    });
                }
            }
            db.Products.Add(product);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Categories} categories, {Brands} brands, {Products} products",
            cats.Count, brands.Count, CatalogSeedData.Products.Length);
    }

    private async Task SeedCouponsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        foreach (var c in CatalogSeedData.Coupons)
            db.Coupons.Add(new Coupon
            {
                Code = c.Code, Description = c.Description, DiscountType = c.Type, Value = c.Value,
                MinOrderAmount = c.Min, MaxDiscountAmount = c.Max, UsageLimit = c.Limit,
                StartsAt = now, ExpiresAt = now.AddDays(c.ExpiresInDays),
            });
        await db.SaveChangesAsync(ct);
    }

    private static string SkuPrefix(string categorySlug) => categorySlug switch
    {
        "processor" => "CPU", "motherboard" => "MB", "ram" => "RAM", "ssd" => "SSD", "graphics-card" => "GPU",
        "power-supply" => "PSU", "casing" => "CSE", "monitor" => "MON", "ups" => "UPS", "gaming-laptop" or "everyday-laptop" => "LAP",
        "gaming-pc" or "office-pc" => "PC", "keyboard" => "KBD", "mouse" => "MSE", "headphone" => "HDP", "webcam" => "CAM",
        _ => "GEN",
    };

    private static string WarrantyText(int months) =>
        months >= 120 ? "lifetime (limited) warranty"
        : months % 12 == 0 ? $"{months / 12}-year warranty"
        : $"{months}-month warranty";

    /// <summary>"65 W" -> 65, "1 TB" -> 1000 (GB), "3200 MHz" -> 3200, "DDR5" -> null.</summary>
    internal static decimal? ParseNumeric(string value)
    {
        var m = LeadingNumber().Match(value);
        if (!m.Success || !decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) return null;
        return m.Groups[2].Value.Equals("TB", StringComparison.OrdinalIgnoreCase) ? n * 1000 : n;
    }

    [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)\s*([A-Za-z]+)?")]
    private static partial Regex LeadingNumber();
}
