using Microsoft.Extensions.DependencyInjection;
using TechBazar.Application.Catalog;
using TechBazar.Application.Common;
using TechBazar.Domain.Enums;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Catalog;

public class ProductServiceTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private async Task<T> WithService<T>(Func<IProductService, Task<T>> action)
    {
        using var scope = db.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IProductService>());
    }

    [Fact]
    public async Task List_DefaultsToFirstPageOfTwenty()
    {
        var r = await WithService(s => s.GetProductsAsync(new ProductListQuery()));
        Assert.Equal(20, r.Items.Count);
        Assert.Equal(1, r.Page);
        Assert.True(r.TotalCount >= 40);
        Assert.Equal((int)Math.Ceiling(r.TotalCount / 20d), r.TotalPages);
    }

    [Fact]
    public async Task Pagination_PagesDoNotOverlapAndCoverEverything()
    {
        var all = new List<int>();
        for (var page = 1; ; page++)
        {
            var r = await WithService(s => s.GetProductsAsync(new ProductListQuery { Page = page, PageSize = 25, Sort = "newest" }));
            all.AddRange(r.Items.Select(i => i.Id));
            if (!r.HasNext) { Assert.Equal(r.TotalCount, all.Count); break; }
        }
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public async Task CategoryFilter_IncludesDescendants()
    {
        var component = await WithService(s => s.GetProductsAsync(new ProductListQuery { Category = "component", PageSize = 60 }));
        var processors = await WithService(s => s.GetProductsAsync(new ProductListQuery { Category = "processor", PageSize = 60 }));

        Assert.NotEmpty(processors.Items);
        Assert.All(processors.Items, p => Assert.Equal("processor", p.CategorySlug));
        Assert.True(component.TotalCount > processors.TotalCount);
        Assert.Contains(component.Items, p => p.CategorySlug == "graphics-card");
    }

    [Fact]
    public async Task UnknownCategory_ReturnsEmptyPage()
    {
        var r = await WithService(s => s.GetProductsAsync(new ProductListQuery { Category = "no-such-category" }));
        Assert.Empty(r.Items);
        Assert.Equal(0, r.TotalCount);
    }

    [Fact]
    public async Task BrandFilter_SupportsRepeatedAndCommaSeparatedValues()
    {
        var repeated = await WithService(s => s.GetProductsAsync(new ProductListQuery { Brand = ["intel", "amd"], PageSize = 60 }));
        var csv = await WithService(s => s.GetProductsAsync(new ProductListQuery { Brand = ["intel,amd"], PageSize = 60 }));

        Assert.Equal(repeated.TotalCount, csv.TotalCount);
        Assert.NotEmpty(repeated.Items);
        Assert.All(repeated.Items, p => Assert.Contains(p.BrandSlug, new[] { "intel", "amd" }));
    }

    [Fact]
    public async Task PriceRange_UsesEffectivePrice()
    {
        var r = await WithService(s => s.GetProductsAsync(new ProductListQuery { MinPrice = 10000, MaxPrice = 20000, PageSize = 60 }));
        Assert.NotEmpty(r.Items);
        Assert.All(r.Items, p => Assert.InRange(p.EffectivePrice, 10000m, 20000m));
        // The i5-12400F is listed at 14,500 but sells at 13,800: both fall in range, and a 14,000 cap must still include it only via the sale price.
        var capped = await WithService(s => s.GetProductsAsync(new ProductListQuery { Q = "12400F", MaxPrice = 14000 }));
        Assert.Single(capped.Items);
    }

    [Fact]
    public async Task InStockFilter_ExcludesOutOfStockPreOrderAndUpcoming()
    {
        var r = await WithService(s => s.GetProductsAsync(new ProductListQuery { InStock = true, PageSize = 60 }));
        var all = await WithService(s => s.GetProductsAsync(new ProductListQuery { PageSize = 60 }));
        Assert.All(r.Items, p => Assert.Equal(StockStatus.InStock, p.StockStatus));
        Assert.True(all.TotalCount > r.TotalCount);
    }

    [Fact]
    public async Task SpecFilter_SingleKey_FiltersBySocket()
    {
        var r = await WithService(s => s.GetProductsAsync(new ProductListQuery { Category = "processor", Spec = ["Socket:AM5"] }));
        Assert.NotEmpty(r.Items);
        Assert.All(r.Items, p => Assert.Contains("Ryzen", p.Name));
    }

    [Fact]
    public async Task SpecFilter_SameKeyValuesAreOr_DifferentKeysAreAnd()
    {
        var either = await WithService(s => s.GetProductsAsync(new ProductListQuery { Category = "motherboard", Spec = ["Socket:AM5", "Socket:AM4"], PageSize = 60 }));
        var am5Only = await WithService(s => s.GetProductsAsync(new ProductListQuery { Category = "motherboard", Spec = ["Socket:AM5"], PageSize = 60 }));
        var am5Ddr5 = await WithService(s => s.GetProductsAsync(new ProductListQuery { Category = "motherboard", Spec = ["Socket:AM5", "RAM Type:DDR5"], PageSize = 60 }));
        var am5Ddr4 = await WithService(s => s.GetProductsAsync(new ProductListQuery { Category = "motherboard", Spec = ["Socket:AM5", "RAM Type:DDR4"], PageSize = 60 }));

        Assert.True(either.TotalCount > am5Only.TotalCount);
        Assert.Equal(am5Only.TotalCount, am5Ddr5.TotalCount); // every AM5 board in the catalog is DDR5
        Assert.Equal(0, am5Ddr4.TotalCount);
    }

    [Theory]
    [InlineData("price_asc")]
    [InlineData("price_desc")]
    public async Task Sort_ByPrice_IsMonotonic(string sort)
    {
        var r = await WithService(s => s.GetProductsAsync(new ProductListQuery { Sort = sort, PageSize = 60 }));
        var prices = r.Items.Select(i => i.EffectivePrice).ToList();
        Assert.Equal(sort == "price_asc" ? prices.Order() : prices.OrderDescending(), prices);
    }

    [Fact]
    public async Task Sort_Newest_And_Popularity_AreDeterministic()
    {
        var a = await WithService(s => s.GetProductsAsync(new ProductListQuery { Sort = "newest", PageSize = 60 }));
        var b = await WithService(s => s.GetProductsAsync(new ProductListQuery { Sort = "newest", PageSize = 60 }));
        Assert.Equal(a.Items.Select(i => i.Id), b.Items.Select(i => i.Id));
        Assert.NotEqual(a.Items.Select(i => i.Id), (await WithService(s => s.GetProductsAsync(new ProductListQuery { PageSize = 60 }))).Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Search_MatchesAllTokensAcrossNameAndBrand()
    {
        var r = await WithService(s => s.GetProductsAsync(new ProductListQuery { Q = "corsair ddr5" }));
        Assert.NotEmpty(r.Items);
        Assert.All(r.Items, p => Assert.Equal("corsair", p.BrandSlug));
    }

    [Fact]
    public async Task GetBySlug_ReturnsGroupedSpecsBreadcrumbsAndImages()
    {
        var d = await WithService(s => s.GetBySlugAsync("intel-core-i5-12400f-12th-gen-processor"));

        Assert.Equal("Intel", d.Brand.Name);
        Assert.Equal(["Component", "Processor"], d.Breadcrumbs.Select(b => b.Name));
        Assert.Equal(13800m, d.EffectivePrice);
        Assert.Equal(5, d.DiscountPercent);
        Assert.Equal(700m, d.SavingsAmount);
        Assert.Equal(36, d.WarrantyMonths);
        Assert.NotEmpty(d.Images);
        Assert.NotEmpty(d.KeyFeatures);
        var general = Assert.Single(d.Specifications, g => g.Group == "General");
        Assert.Contains(general.Items, i => i is { Key: "Socket", Value: "LGA1700" });
    }

    [Fact]
    public async Task GetBySlug_Unknown_ThrowsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() => WithService(s => s.GetBySlugAsync("does-not-exist")));

    [Fact]
    public async Task Related_ExcludesSelf_PrefersSameCategory_AndHonoursCount()
    {
        var self = await WithService(s => s.GetBySlugAsync("intel-core-i5-12400f-12th-gen-processor"));
        var related = await WithService(s => s.GetRelatedAsync(self.Slug, 4));

        Assert.Equal(4, related.Count);
        Assert.DoesNotContain(related, r => r.Id == self.Id);
        Assert.All(related, r => Assert.Equal("processor", r.CategorySlug));
        Assert.Equal(related.Count, related.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public async Task Related_UnknownProduct_ThrowsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() => WithService(s => s.GetRelatedAsync("nope", 4)));

    [Fact]
    public async Task Facets_ForMotherboards_ListSocketAndRamTypeValuesWithCounts()
    {
        var f = await WithService(s => s.GetFacetsAsync("motherboard"));

        Assert.Equal(7, f.TotalCount);
        Assert.True(f.MinPrice < f.MaxPrice);
        var socket = Assert.Single(f.Specifications, s => s.Key == "Socket");
        Assert.Equal(new[] { "AM4", "AM5", "LGA1700" }, socket.Values.Select(v => v.Value).Order());
        Assert.Equal(f.TotalCount, socket.Values.Sum(v => v.Count));
        var ram = Assert.Single(f.Specifications, s => s.Key == "RAM Type");
        Assert.Equal(["DDR4", "DDR5"], ram.Values.Select(v => v.Value).Order());
        Assert.Equal(f.TotalCount, f.Brands.Sum(b => b.Count));
    }
}
