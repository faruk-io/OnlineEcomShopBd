using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechBazar.Domain.Entities;
using TechBazar.Infrastructure.Persistence;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Persistence;

public class DbContextBehaviourTests
{
    private static (ApplicationDbContext Db, Category Cat, Brand Brand) Seeded(TestDatabase t, IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (db, db.Categories.First(c => c.Slug == "ssd"), db.Brands.First());
    }

    private static Product NewProduct(Category c, Brand b, string slug = "test-product", string sku = "TST-1") => new()
    {
        Name = "Test Product", Slug = slug, Sku = sku, Price = 1000, DiscountPrice = 900, Category = c, Brand = b,
    };

    [Fact]
    public async Task Insert_SetsCreatedAt_Update_SetsUpdatedAt_AndEffectivePrice()
    {
        using var t = new TestDatabase();
        using var scope = t.CreateScope();
        var (db, cat, brand) = Seeded(t, scope);

        var p = NewProduct(cat, brand);
        db.Products.Add(p);
        var before = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        Assert.True(p.CreatedAt >= before);
        Assert.Null(p.UpdatedAt);
        Assert.Equal(900m, p.EffectivePrice);

        p.DiscountPrice = null;
        await db.SaveChangesAsync();
        Assert.NotNull(p.UpdatedAt);
        Assert.Equal(1000m, p.EffectivePrice);
    }

    [Fact]
    public async Task Delete_IsSoft_HiddenByFilter_ButStillInTable()
    {
        using var t = new TestDatabase();
        using var scope = t.CreateScope();
        var (db, cat, brand) = Seeded(t, scope);
        var p = NewProduct(cat, brand);
        db.Products.Add(p);
        await db.SaveChangesAsync();

        db.Products.Remove(p);
        await db.SaveChangesAsync();

        Assert.False(await db.Products.AnyAsync(x => x.Slug == "test-product"));
        var raw = await db.Products.IgnoreQueryFilters().SingleAsync(x => x.Slug == "test-product");
        Assert.True(raw.IsDeleted);
        Assert.NotNull(raw.DeletedAt);
    }

    [Fact]
    public async Task UniqueSlugAndSku_AreEnforced_ButReusableAfterSoftDelete()
    {
        using var t = new TestDatabase();
        using var scope = t.CreateScope();
        var (db, cat, brand) = Seeded(t, scope);

        var first = NewProduct(cat, brand);
        db.Products.Add(first);
        await db.SaveChangesAsync();

        db.Products.Add(NewProduct(cat, brand, slug: "test-product", sku: "TST-2"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.Products.Add(NewProduct(cat, brand, slug: "other", sku: "TST-1"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        // after soft-deleting the original, the slug/SKU become available again
        var tracked = await db.Products.SingleAsync(x => x.Slug == "test-product");
        db.Products.Remove(tracked);
        await db.SaveChangesAsync();
        db.Products.Add(NewProduct(await db.Categories.FirstAsync(c => c.Slug == "ssd"), await db.Brands.FirstAsync()));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task DiscountPriceAboveListPrice_IsRejectedByCheckConstraint()
    {
        using var t = new TestDatabase();
        using var scope = t.CreateScope();
        var (db, cat, brand) = Seeded(t, scope);
        var p = NewProduct(cat, brand);
        p.DiscountPrice = 1500;
        db.Products.Add(p);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Money_RoundTripsWithTwoDecimals()
    {
        using var t = new TestDatabase();
        using var scope = t.CreateScope();
        var (db, cat, brand) = Seeded(t, scope);
        var p = NewProduct(cat, brand);
        p.Price = 12345.67m;
        p.DiscountPrice = null;
        db.Products.Add(p);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(12345.67m, (await db.Products.SingleAsync(x => x.Slug == "test-product")).Price);
    }
}
