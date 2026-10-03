using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Catalog;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Domain.Entities;
using TechBazar.Infrastructure.Persistence;

namespace TechBazar.UnitTests.Persistence;

/// <summary>
/// The suite runs on SQLite, but production is SQL Server. These tests compile the catalog queries with the real SQL Server
/// provider (no connection is opened; <c>ToQueryString</c> only translates) so provider-specific translation bugs surface in CI.
/// </summary>
public class SqlServerTranslationTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer("Server=.;Database=NotUsed;Trusted_Connection=True;TrustServerCertificate=True").Options);

    public void Dispose() => _db.Dispose();

    private static IQueryable<Product> Filtered(IQueryable<Product> q, ProductListQuery f)
    {
        // Same predicates as ProductService.ApplyFiltersAsync minus the category-id lookup.
        var brands = (f.Brand ?? []).Select(b => b.ToLowerInvariant()).ToList();
        if (brands.Count > 0) q = q.Where(p => brands.Contains(p.Brand.Slug));
        if (f.MinPrice is { } min) q = q.Where(p => p.EffectivePrice >= min);
        if (f.MaxPrice is { } max) q = q.Where(p => p.EffectivePrice <= max);
        foreach (var (key, values) in SpecFilterParser.Parse(f.Spec))
        {
            var k = key; var v = values.ToList();
            q = q.Where(p => p.Specifications.Any(s => s.Key == k && v.Contains(s.Value)));
        }
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var pattern = TextSearch.Contains(f.Q);
            q = q.Where(p => EF.Functions.Like(p.Name, pattern, TextSearch.Escape) || EF.Functions.Like(p.Brand.Name, pattern, TextSearch.Escape));
        }
        return q;
    }

    [Theory]
    [InlineData("popularity", "ORDER BY [p].[SoldCount] DESC")]
    [InlineData("newest", "ORDER BY [p].[CreatedAt] DESC")]
    [InlineData("price_asc", "ORDER BY [p].[EffectivePrice]")]
    [InlineData("price_desc", "ORDER BY [p].[EffectivePrice] DESC")]
    public void ListQuery_TranslatesForEverySort(string sort, string expectedOrderBy)
    {
        var f = new ProductListQuery
        {
            Sort = sort, Brand = ["intel"], MinPrice = 1000, MaxPrice = 90000, Spec = ["Socket:AM5", "Socket:LGA1700", "RAM Type:DDR5"], Q = "core i5", Page = 3, PageSize = 24,
        };

        var sql = ProductService.BuildPageQuery(Filtered(_db.Products.Where(p => p.IsActive), f), f).ToQueryString();

        Assert.Contains(expectedOrderBy, sql);
        Assert.Matches(@"OFFSET @\w+ ROWS\s+FETCH NEXT @\w+ ROWS ONLY", sql);
        Assert.Contains("LIKE", sql);
        Assert.Contains("[p].[IsDeleted] = CAST(0 AS bit)", sql); // soft-delete filter applied
    }

    [Fact]
    public void AutocompleteStyleQuery_Translates()
    {
        var contains = TextSearch.Contains("ryz");
        var sql = _db.Products.Where(p => p.IsActive && EF.Functions.Like(p.Name, contains, TextSearch.Escape))
            .OrderByDescending(p => EF.Functions.Like(p.Name, TextSearch.StartsWith("ryz"), TextSearch.Escape))
            .Take(8).Select(p => new { p.Name, p.EffectivePrice }).ToQueryString();
        Assert.Contains("LIKE", sql);
    }

    [Fact]
    public void FacetQueries_Translate()
    {
        var scope = _db.Products.Where(p => p.IsActive);
        Assert.Contains("GROUP BY", scope.GroupBy(p => new { p.Brand.Name, p.Brand.Slug }).Select(g => new { g.Key.Name, Count = g.Count() }).ToQueryString());
        var ids = scope.Select(p => p.Id);
        Assert.Contains("COUNT(DISTINCT", _db.ProductSpecifications.Where(s => s.IsFilterable && ids.Contains(s.ProductId))
            .GroupBy(s => new { s.Key, s.Value }).Select(g => new { g.Key.Key, g.Key.Value, C = g.Select(x => x.ProductId).Distinct().Count() }).ToQueryString());
    }

    [Fact]
    public void Model_UsesDecimal18_2_ForMoney_AndFilteredUniqueIndexes()
    {
        var product = _db.Model.FindEntityType(typeof(Product))!;
        foreach (var name in new[] { nameof(Product.Price), nameof(Product.DiscountPrice), nameof(Product.EffectivePrice) })
        {
            var p = product.FindProperty(name)!;
            Assert.Equal(18, p.GetPrecision());
            Assert.Equal(2, p.GetScale());
            Assert.Null(p.GetValueConverter()); // converter-free on SQL Server (the double mapping is SQLite-test-only)
        }
        foreach (var prop in new[] { nameof(Product.Slug), nameof(Product.Sku) })
        {
            var idx = product.GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == prop);
            Assert.True(idx.IsUnique);
            Assert.Equal("[IsDeleted] = 0", idx.GetFilter());
        }
        Assert.Equal(typeof(decimal), product.FindProperty(nameof(Product.Price))!.GetProviderClrType() ?? typeof(decimal));
    }

    // ---- admin dashboard: aggregation must run IN the database (bounded rows), and must translate for SQL Server
    [Fact]
    public void DashboardDailySales_GroupsByDateInSql()
    {
        var sql = TechBazar.Application.Admin.DashboardService.DailySales(_db.Orders.AsNoTracking(), new DateTime(2026, 9, 1)).ToQueryString();
        Assert.True(sql.Contains("CONVERT(date, [o].[CreatedAt])") && sql.Contains("GROUP BY [o0].[Key]"), sql);
        Assert.Contains("SUM([o0].[GrandTotal])", sql);
        Assert.Contains("COUNT(*)", sql);
        Assert.Contains("[o].[Status] <> 6", sql);    // Cancelled
        Assert.Contains("[o].[Status] <> 7", sql);    // Returned
    }

    [Fact]
    public void DashboardTopProducts_IsAJoinedGroupByLimitedToFiveRows()
    {
        var sql = TechBazar.Application.Admin.DashboardService.TopProducts(_db.OrderItems.AsNoTracking(), new DateTime(2026, 9, 1)).ToQueryString().Replace("\r", "");
        Assert.True(sql.Contains("INNER JOIN (") && sql.Contains("GROUP BY [o].[ProductId]"), sql);
        Assert.True(sql.Contains("DECLARE @__p_1 int = 5;") && sql.Contains("SELECT TOP(@__p_1)"), sql);   // five rows leave the database
        Assert.True(sql.Contains("ORDER BY COALESCE(SUM([o].[LineTotal]), 0.0) DESC"), sql);               // ranked by revenue, in SQL
    }
}
