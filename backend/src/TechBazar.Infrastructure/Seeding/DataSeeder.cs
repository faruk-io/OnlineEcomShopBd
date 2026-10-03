using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechBazar.Application.Catalog;
using TechBazar.Domain.Common;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;
using TechBazar.Infrastructure.Identity;
using TechBazar.Infrastructure.Persistence;

namespace TechBazar.Infrastructure.Seeding;

/// <summary>
/// Additive and idempotent: every category, brand, product (by slug) and coupon (by code) is added only if it does not exist,
/// so re-running never duplicates anything, never overwrites edits made in the admin panel, and never resurrects a row that an
/// administrator deleted (soft-deleted rows count as existing). New seed data in a later release is therefore picked up
/// automatically by an existing database.
/// </summary>
public sealed class DataSeeder(
    ApplicationDbContext db,
    RoleManager<ApplicationRole> roles,
    UserManager<ApplicationUser> users,
    IOptions<SeedOptions> options,
    ILogger<DataSeeder> logger)
{
    private readonly SeedOptions _opt = options.Value;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedRolesAndAdminAsync();
        await SeedCatalogAsync(ct);
        await SeedCouponsAsync(ct);
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

        var cats = await db.Categories.IgnoreQueryFilters().ToDictionaryAsync(c => c.Slug, c => c, StringComparer.OrdinalIgnoreCase, ct);
        var byName = cats.Values.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var order = cats.Count;
        var newCats = 0;
        foreach (var c in CatalogSeedData.Categories)
        {
            var slug = SlugHelper.Slugify(c.Name);
            if (cats.ContainsKey(slug)) continue;
            var cat = new Category
            {
                Name = c.Name, Slug = slug, Description = c.Description, DisplayOrder = order++,
                Parent = c.Parent is null ? null : byName[c.Parent],
                ImageUrl = $"/images/categories/{slug}.svg",
            };
            cats[slug] = byName[c.Name] = cat;
            db.Categories.Add(cat);
            newCats++;
        }

        var brands = await db.Brands.IgnoreQueryFilters().ToDictionaryAsync(b => b.Name, b => b, StringComparer.OrdinalIgnoreCase, ct);
        var newBrands = 0;
        foreach (var b in CatalogSeedData.Brands)
        {
            if (brands.ContainsKey(b.Name)) continue;
            var slug = SlugHelper.Slugify(b.Name);
            var brand = new Brand { Name = b.Name, Slug = slug, Description = b.Description, LogoUrl = $"/images/brands/{slug}.svg" };
            brands[b.Name] = brand;
            db.Brands.Add(brand);
            newBrands++;
        }

        var slugs = (await db.Products.IgnoreQueryFilters().Select(p => p.Slug).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingSkus = await db.Products.IgnoreQueryFilters().Select(p => p.Sku).ToListAsync(ct);
        var skuCounters = new Dictionary<string, int>();
        foreach (var sku in existingSkus)
        {
            var parts = sku.Split('-'); // TB-CPU-0005
            if (parts.Length == 3 && parts[0] == "TB" && int.TryParse(parts[2], out var n))
                skuCounters[parts[1]] = Math.Max(skuCounters.GetValueOrDefault(parts[1]), n);
        }

        // Deterministic pseudo-random popularity/age so a fresh database always looks the same.
        var rnd = new Random(42);
        var newProducts = 0;
        foreach (var p in CatalogSeedData.Products)
        {
            var slug = SlugHelper.Slugify(p.Name);
            var category = cats[SlugHelper.Slugify(p.Category)];
            var brand = brands[p.Brand];
            var sold = rnd.Next(5, 400);
            var views = sold * rnd.Next(8, 30);
            var stock = rnd.Next(3, 60);
            var ageDays = rnd.Next(1, 240);
            if (!slugs.Add(slug)) continue;

            var code = SkuPrefix(category.Slug);
            skuCounters[code] = skuCounters.GetValueOrDefault(code) + 1;
            var features = p.Features.Split('|');
            var product = new Product
            {
                Name = p.Name, Slug = slug, Sku = $"TB-{code}-{skuCounters[code]:0000}",
                ShortDescription = features[0],
                Description = $"{p.Name} by {brand.Name}. {string.Join(". ", features)}. Sold with {WarrantyText(p.WarrantyMonths)} from TechBazar BD.",
                Price = p.Price, DiscountPrice = p.Sale, StockStatus = p.Stock,
                StockQuantity = p.Stock == StockStatus.InStock ? stock : 0,
                WarrantyMonths = p.WarrantyMonths, WarrantyDetails = WarrantyText(p.WarrantyMonths),
                SoldCount = sold, ViewCount = views, IsFeatured = p.Featured,
                Category = category, Brand = brand, CreatedAt = now.AddDays(-ageDays),
            };
            product.RecalculateEffectivePrice();
            product.Images.Add(new ProductImage { Url = $"/images/placeholders/{category.Slug}.svg", AltText = p.Name, IsPrimary = true });

            var featureOrder = 0;
            foreach (var f in features) product.KeyFeatures.Add(new ProductKeyFeature { Text = f, DisplayOrder = featureOrder++ });

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
                        NumericValue = SpecRules.NumericFor(key, value), IsFilterable = SpecRules.IsFilterable(key), DisplayOrder = specOrder++,
                    });
                }
            }
            db.Products.Add(product);
            newProducts++;
        }

        if (newCats + newBrands + newProducts > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Categories} categories, {Brands} brands, {Products} products", newCats, newBrands, newProducts);
        }
    }

    private async Task SeedCouponsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var existing = (await db.Coupons.IgnoreQueryFilters().Select(c => c.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = false;
        foreach (var c in CatalogSeedData.Coupons.Where(c => !existing.Contains(c.Code)))
        {
            db.Coupons.Add(new Coupon
            {
                Code = c.Code, Description = c.Description, DiscountType = c.Type, Value = c.Value,
                MinOrderAmount = c.Min, MaxDiscountAmount = c.Max, UsageLimit = c.Limit,
                StartsAt = now, ExpiresAt = now.AddDays(c.ExpiresInDays),
            });
            added = true;
        }
        if (added) await db.SaveChangesAsync(ct);
    }

    private static string SkuPrefix(string categorySlug) => categorySlug switch
    {
        "processor" => "CPU",
        "motherboard" => "MB",
        "ram" => "RAM",
        "ssd" => "SSD",
        "graphics-card" => "GPU",
        "power-supply" => "PSU",
        "casing" => "CSE",
        "cpu-cooler" => "CLR",
        "monitor" => "MON",
        "ups" => "UPS",
        "gaming-laptop" or "everyday-laptop" => "LAP",
        "gaming-pc" or "office-pc" => "PC",
        "keyboard" => "KBD",
        "mouse" => "MSE",
        "headphone" => "HDP",
        "webcam" => "CAM",
        _ => "GEN",
    };

    private static string WarrantyText(int months) =>
        months >= 120 ? "lifetime (limited) warranty"
        : months % 12 == 0 ? $"{months / 12}-year warranty"
        : $"{months}-month warranty";
}
