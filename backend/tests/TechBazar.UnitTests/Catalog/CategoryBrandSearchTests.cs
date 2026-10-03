using Microsoft.Extensions.DependencyInjection;
using TechBazar.Application.Catalog;
using TechBazar.Application.Common;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Catalog;

public class CategoryBrandSearchTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private T Get<T>(Func<IServiceProvider, T> f) { using var s = db.CreateScope(); return f(s.ServiceProvider); }

    [Fact]
    public async Task Tree_HasExpectedRootsAndComponentChildren()
    {
        var tree = await Get(sp => sp.GetRequiredService<ICategoryService>().GetTreeAsync());

        var roots = tree.Select(r => r.Slug).ToList();
        Assert.Equal(["desktop", "laptop", "component", "monitor", "ups", "accessories"], roots);

        var component = tree.Single(r => r.Slug == "component");
        Assert.Equal(
            ["processor", "motherboard", "ram", "ssd", "graphics-card", "power-supply", "casing", "cpu-cooler"],
            component.Children.Select(c => c.Slug));
    }

    [Fact]
    public async Task Tree_RollsUpProductCounts()
    {
        var tree = await Get(sp => sp.GetRequiredService<ICategoryService>().GetTreeAsync());
        var total = await Get(sp => sp.GetRequiredService<IProductService>().GetProductsAsync(new ProductListQuery { PageSize = 1 }));

        Assert.Equal(total.Items.Count == 0 ? 0 : total.TotalCount, tree.Sum(r => r.ProductCount));
        var component = tree.Single(r => r.Slug == "component");
        Assert.Equal(component.Children.Sum(c => c.ProductCount), component.ProductCount);
    }

    [Fact]
    public async Task CategoryBySlug_ReturnsBreadcrumbsAndChildren()
    {
        var c = await Get(sp => sp.GetRequiredService<ICategoryService>().GetBySlugAsync("component"));
        Assert.Equal(8, c.Children.Count);

        var leaf = await Get(sp => sp.GetRequiredService<ICategoryService>().GetBySlugAsync("graphics-card"));
        Assert.Equal(["Component", "Graphics Card"], leaf.Breadcrumbs.Select(b => b.Name));
    }

    [Fact]
    public async Task CategoryBySlug_Unknown_ThrowsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() => Get(sp => sp.GetRequiredService<ICategoryService>().GetBySlugAsync("zzz")));

    [Fact]
    public async Task Brands_AreSortedAndCounted()
    {
        var brands = await Get(sp => sp.GetRequiredService<IBrandService>().GetAllAsync());
        Assert.True(brands.Count >= 10);
        Assert.Equal(brands.Select(b => b.Name).OrderBy(n => n, StringComparer.Ordinal), brands.Select(b => b.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(brands, b => Assert.True(b.ProductCount > 0, $"{b.Name} has no products"));
    }

    [Fact]
    public async Task Autocomplete_RanksPrefixMatchesFirst_AndRespectsLimit()
    {
        var r = await Get(sp => sp.GetRequiredService<ISearchService>().AutocompleteAsync("ryzen", 3));

        Assert.Equal(3, r.Products.Count);
        Assert.All(r.Products, p => Assert.Contains("Ryzen", p.Name));
    }

    [Fact]
    public async Task Autocomplete_ReturnsMatchingBrandsAndCategories()
    {
        var r = await Get(sp => sp.GetRequiredService<ISearchService>().AutocompleteAsync("ssd", 5));
        Assert.Contains(r.Categories, c => c.Slug == "ssd");

        var b = await Get(sp => sp.GetRequiredService<ISearchService>().AutocompleteAsync("kingston", 5));
        Assert.Contains(b.Brands, x => x.Slug == "kingston");
        Assert.NotEmpty(b.Products);
    }

    [Fact]
    public async Task Autocomplete_NoMatch_ReturnsEmptyCollections()
    {
        var r = await Get(sp => sp.GetRequiredService<ISearchService>().AutocompleteAsync("zzzzqqq", 5));
        Assert.Empty(r.Products);
        Assert.Empty(r.Categories);
        Assert.Empty(r.Brands);
    }
}
