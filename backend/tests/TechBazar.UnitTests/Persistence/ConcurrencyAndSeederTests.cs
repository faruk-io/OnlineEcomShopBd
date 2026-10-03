using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechBazar.Application.Catalog;
using TechBazar.Application.Storage;
using TechBazar.Infrastructure.Persistence;
using TechBazar.Infrastructure.Seeding;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Persistence;

public class ConcurrencyTests
{
    [Fact]
    public async Task TwoWritersOfTheSameProduct_CannotBothWin()
    {
        using var db = new TestDatabase();
        using var a = db.CreateScope();
        using var b = db.CreateScope();
        var ctxA = a.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ctxB = b.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pa = await ctxA.Products.FirstAsync(p => p.Name.Contains("Ryzen 5 5600"));
        var pb = await ctxB.Products.FirstAsync(p => p.Id == pa.Id);
        pa.StockQuantity = 5; pb.StockQuantity = 7;

        await ctxA.SaveChangesAsync();                                             // first writer wins
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());   // second must re-read and retry
        Assert.Equal(1, (await ctxA.Products.AsNoTracking().FirstAsync(p => p.Id == pa.Id)).Version);
    }

    [Fact]
    public async Task EveryUpdateBumpsTheVersion_NewRowsStartAtZero()
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coupon = await ctx.Coupons.FirstAsync();
        Assert.Equal(0, coupon.Version);
        coupon.UsedCount++; await ctx.SaveChangesAsync();
        coupon.UsedCount++; await ctx.SaveChangesAsync();
        Assert.Equal(2, coupon.Version);
    }
}

public class SeederTests
{
    [Fact]
    public async Task Seeding_IsAdditive_FillsMissingCatalogDataAndNeverDuplicates()
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();

        // Simulate a database seeded by an earlier release: no cooler category / products yet.
        var coolerProducts = await ctx.Products.Where(p => p.Category.Slug == "cpu-cooler").ToListAsync();
        Assert.True(coolerProducts.Count >= 4);
        ctx.ProductImages.RemoveRange(await ctx.ProductImages.Where(i => coolerProducts.Select(c => c.Id).Contains(i.ProductId)).ToListAsync());
        foreach (var p in coolerProducts) ctx.Entry(p).State = EntityState.Detached;
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM ProductSpecifications WHERE ProductId IN (SELECT Id FROM Products WHERE Slug LIKE '%cooler%' OR Slug LIKE 'deepcool%' OR Slug LIKE 'id-cooling%')");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM ProductKeyFeatures WHERE ProductId IN (SELECT Id FROM Products WHERE Slug LIKE '%cooler%' OR Slug LIKE 'deepcool%' OR Slug LIKE 'id-cooling%')");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM ProductImages WHERE ProductId IN (SELECT Id FROM Products WHERE Slug LIKE '%cooler%' OR Slug LIKE 'deepcool%' OR Slug LIKE 'id-cooling%')");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM Products WHERE Slug LIKE '%cooler%' OR Slug LIKE 'deepcool%' OR Slug LIKE 'id-cooling%'");
        ctx.ChangeTracker.Clear();
        var before = await ctx.Products.CountAsync();

        await seeder.SeedAsync();
        ctx.ChangeTracker.Clear();
        Assert.True(await ctx.Products.CountAsync() > before);
        Assert.Equal(coolerProducts.Count, await ctx.Products.CountAsync(p => p.Category.Slug == "cpu-cooler"));

        var total = await ctx.Products.CountAsync();
        await seeder.SeedAsync();
        await seeder.SeedAsync();
        Assert.Equal(total, await ctx.Products.CountAsync());
        Assert.Equal(await ctx.Products.CountAsync(), await ctx.Products.Select(p => p.Sku).Distinct().CountAsync());   // SKU numbering continues without clashes
    }

    [Fact]
    public async Task Seeding_NeverOverwritesAdminEdits_NorResurrectsDeletedProducts()
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var edited = await ctx.Products.FirstAsync(p => p.Name.Contains("Ryzen 5 5600"));
        edited.Price = 99999m; edited.DiscountPrice = null;
        var deleted = await ctx.Products.FirstAsync(p => p.Name.Contains("Hyper 212"));
        ctx.Products.Remove(deleted);
        await ctx.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
        ctx.ChangeTracker.Clear();

        Assert.Equal(99999m, (await ctx.Products.FirstAsync(p => p.Id == edited.Id)).Price);
        Assert.False(await ctx.Products.AnyAsync(p => p.Name.Contains("Hyper 212")));          // still deleted
        Assert.True(await ctx.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == deleted.Id));
    }

    [Fact]
    public void CoolerSeedDataCarriesWhatTheBuilderRulesNeed()
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coolers = ctx.Products.Include(p => p.Specifications).Where(p => p.Category.Slug == "cpu-cooler").ToList();
        Assert.True(coolers.Count >= 4);
        Assert.All(coolers, c =>
        {
            Assert.Contains(c.Specifications, s => s.Key == "Supported Sockets" && s.Value.Contains("LGA1700") && s.Value.Contains("AM5"));
            Assert.NotNull(c.Specifications.Single(s => s.Key == "TDP Rating").NumericValue);
            Assert.NotNull(c.Specifications.Single(s => s.Key == "Height").NumericValue);
        });
    }

    [Fact]
    public void EveryCaseMotherboardAndRamCarriesTheSpecsTheCompatibilityRulesRead()
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        void Require(string category, params string[] keys)
        {
            foreach (var p in ctx.Products.Include(p => p.Specifications).Where(p => p.Category.Slug == category).ToList())
                foreach (var key in keys) Assert.True(p.Specifications.Any(s => s.Key == key), $"{p.Name} lacks '{key}'");
        }
        Require("processor", "Socket", "TDP", "Integrated Graphics");
        Require("motherboard", "Socket", "RAM Type", "Form Factor", "Memory Slots", "Max Memory", "M.2 Slots", "SATA Ports");
        Require("ram", "RAM Type", "Capacity", "Modules", "Form Factor");
        Require("ssd", "Interface", "Form Factor");
        Require("graphics-card", "TDP", "Length", "Recommended PSU");
        Require("power-supply", "Wattage");
        Require("casing", "Supported Motherboards", "Max GPU Length", "Max CPU Cooler Height");
        Require("cpu-cooler", "Supported Sockets", "TDP Rating", "Height");
    }
}

public class SmallHelpersTests
{
    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }, ".png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }, ".jpg")]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0, 0, 0, 0, 0, 0 }, ".gif")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, ".webp")]
    public void ImageTypeIsDetectedFromContent(byte[] header, string ext) => Assert.Equal(ext, ImageSniffer.DetectExtension(header));

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<?php echo 1; ?>")]
    [InlineData("MZ\u0090\u0000\u0003\u0000\u0000\u0000")]
    [InlineData("")]
    public void SvgScriptsExecutablesAndEmptyFilesAreNotImages(string content) =>
        Assert.Null(ImageSniffer.DetectExtension(System.Text.Encoding.Latin1.GetBytes(content)));

    [Fact]
    public void RiffWithoutWebpIsNotAnImage() =>
        Assert.Null(ImageSniffer.DetectExtension("RIFF\u0001\u0002\u0003\u0004WAVE"u8));

    [Theory]
    [InlineData("65 W", 65)]
    [InlineData("1 TB", 1000)]
    [InlineData("2 x 8 GB", 2)]
    public void NumericParsing(string value, int expected) => Assert.Equal(expected, SpecRules.ParseNumeric(value));

    [Fact]
    public void SpecKeysUsedByTheBuilderAreFlaggedNumeric() =>
        Assert.All(new[] { "TDP", "Wattage", "Memory Slots", "Max Memory", "M.2 Slots", "SATA Ports", "TDP Rating", "Height", "Max CPU Cooler Height", "Length" },
            k => Assert.True(SpecRules.NumericKeys.Contains(k), k));
}
