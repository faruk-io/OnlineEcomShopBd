using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechBazar.Application.Catalog;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;
using TechBazar.Infrastructure.Persistence;
using TechBazar.Infrastructure.Seeding;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Catalog;

/// <summary>Guards the contract the future PC Builder relies on (socket, RAM type, TDP/wattage) and basic data integrity.</summary>
public class SeedDataTests(TestDatabase fixture) : IClassFixture<TestDatabase>
{
    private List<Product> Load()
    {
        using var scope = fixture.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Products
            .Include(p => p.Category).Include(p => p.Brand).Include(p => p.Specifications).Include(p => p.Images).Include(p => p.KeyFeatures)
            .AsNoTracking().ToList();
    }

    private static string? Spec(Product p, string key) => p.Specifications.FirstOrDefault(s => s.Key == key)?.Value;

    [Fact]
    public void Seed_HasAtLeast40Products_And10Brands()
    {
        var products = Load();
        Assert.True(products.Count >= 40, $"only {products.Count} products");
        Assert.True(products.Select(p => p.BrandId).Distinct().Count() >= 10);
    }

    [Fact]
    public void Seed_SlugsAndSkusAreUnique()
    {
        var products = Load();
        Assert.Equal(products.Count, products.Select(p => p.Slug).Distinct().Count());
        Assert.Equal(products.Count, products.Select(p => p.Sku).Distinct().Count());
    }

    [Fact]
    public void Seed_EveryProductHasImageFeaturesSpecsAndValidPrices()
    {
        foreach (var p in Load())
        {
            Assert.True(p.Images.Count > 0 && p.KeyFeatures.Count >= 3 && p.Specifications.Count >= 3, p.Name);
            Assert.True(p.Price > 0, p.Name);
            if (p.DiscountPrice is { } d) Assert.True(d < p.Price, p.Name);
            Assert.Equal(p.DiscountPrice ?? p.Price, p.EffectivePrice);
            Assert.True(p.WarrantyMonths > 0, p.Name);
        }
    }

    [Fact]
    public void Seed_CatalogCoversAllStockStatuses()
    {
        var statuses = Load().Select(p => p.StockStatus).Distinct().ToHashSet();
        Assert.Superset(new HashSet<StockStatus> { StockStatus.InStock, StockStatus.OutOfStock, StockStatus.PreOrder, StockStatus.UpComing }, statuses);
    }

    [Fact]
    public void Seed_ProcessorsAndMotherboards_HaveSocket_AndMotherboardsAndRamHaveRamType()
    {
        var products = Load();
        foreach (var p in products.Where(p => p.Category.Slug is "processor" or "motherboard"))
            Assert.Contains(Spec(p, "Socket"), new[] { "LGA1700", "AM4", "AM5" });

        foreach (var p in products.Where(p => p.Category.Slug is "motherboard" or "ram"))
            Assert.Contains(Spec(p, "RAM Type"), new[] { "DDR4", "DDR5" });
    }

    [Fact]
    public void Seed_CpuAndGpuHaveNumericTdp_PsuHasNumericWattage_GpuHasRecommendedPsu()
    {
        var products = Load();
        foreach (var p in products.Where(p => p.Category.Slug is "processor" or "graphics-card"))
        {
            var tdp = p.Specifications.Single(s => s.Key == "TDP").NumericValue;
            Assert.True(tdp is > 0 and < 500, p.Name);
        }
        foreach (var p in products.Where(p => p.Category.Slug == "power-supply"))
            Assert.True(p.Specifications.Single(s => s.Key == "Wattage").NumericValue >= 400, p.Name);
        foreach (var p in products.Where(p => p.Category.Slug == "graphics-card"))
            Assert.True(p.Specifications.Single(s => s.Key == "Recommended PSU").NumericValue >= 300, p.Name);
    }

    [Fact]
    public void Seed_Ssd_CapacityIsNormalisedToGigabytes()
    {
        var ssd = Load().Single(p => p.Name.Contains("990 PRO"));
        Assert.Equal(2000m, ssd.Specifications.Single(s => s.Key == "Capacity").NumericValue);
    }

    [Fact]
    public async Task Seeder_IsIdempotent()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var before = (await db.Products.CountAsync(), await db.Categories.CountAsync(), await db.Coupons.CountAsync());

        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();

        Assert.Equal(before, (await db.Products.CountAsync(), await db.Categories.CountAsync(), await db.Coupons.CountAsync()));
    }

    [Theory]
    [InlineData("65 W", 65)]
    [InlineData("1 TB", 1000)]
    [InlineData("512 GB", 512)]
    [InlineData("3200 MHz", 3200)]
    [InlineData("4.4 GHz", 4.4)]
    public void ParseNumeric_ExtractsLeadingNumber(string value, double expected) =>
        Assert.Equal((decimal)expected, SpecRules.ParseNumeric(value));

    [Theory]
    [InlineData("DDR5")]
    [InlineData("LGA1700")]
    [InlineData("")]
    public void ParseNumeric_ReturnsNullForNonNumeric(string value) => Assert.Null(SpecRules.ParseNumeric(value));
}
