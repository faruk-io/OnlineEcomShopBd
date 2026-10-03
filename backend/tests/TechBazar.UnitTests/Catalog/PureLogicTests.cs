using Microsoft.Extensions.DependencyInjection;
using TechBazar.Application.Auth;
using TechBazar.Application.Catalog;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Catalog.Validators;
using TechBazar.Domain.Common;
using TechBazar.Domain.Entities;

namespace TechBazar.UnitTests.Catalog;

public class SlugHelperTests
{
    [Theory]
    [InlineData("Intel Core i5-12400F (Box)", "intel-core-i5-12400f-box")]
    [InlineData("  Graphics   Card ", "graphics-card")]
    [InlineData("C++ Tools", "cplusplus-tools")]
    [InlineData("---", "")]
    [InlineData("", "")]
    public void Slugify(string input, string expected) => Assert.Equal(expected, SlugHelper.Slugify(input));
}

public class SpecFilterParserTests
{
    [Fact]
    public void Parse_GroupsValuesByKey_CaseInsensitively_AndDeduplicates()
    {
        var r = SpecFilterParser.Parse(["Socket:AM5", "socket:LGA1700", "Socket:am5", "RAM Type:DDR5"]);
        Assert.Equal(2, r.Count);
        Assert.Equal(["AM5", "LGA1700"], r["Socket"]);
        Assert.Equal(["DDR5"], r["ram type"]);
    }

    [Theory]
    [InlineData("nocolon")]
    [InlineData(":novalue")]
    [InlineData("nokey:")]
    [InlineData("   ")]
    public void Parse_IgnoresMalformedEntries(string raw) => Assert.Empty(SpecFilterParser.Parse([raw]));

    [Fact]
    public void Parse_KeepsColonsInsideTheValue()
    {
        var r = SpecFilterParser.Parse(["Ratio:16:9"]);
        Assert.Equal(["16:9"], r["Ratio"]);
    }
}

public class PricingTests
{
    [Theory]
    [InlineData(1000, 800, 20)]
    [InlineData(14500, 13800, 5)]
    [InlineData(1000, 1000, 0)]
    [InlineData(0, 0, 0)]
    public void DiscountPercent(decimal price, decimal effective, int expected) =>
        Assert.Equal(expected, PriceMath.DiscountPercent(price, effective));

    [Theory]
    [InlineData(1000, 900, 900)]
    [InlineData(1000, null, 1000)]
    [InlineData(1000, 0, 1000)]       // zero is not a valid sale price
    [InlineData(1000, 1200, 1000)]    // sale price above list price is ignored
    public void Product_EffectivePrice(decimal price, int? discount, decimal expected)
    {
        var p = new Product { Price = price, DiscountPrice = discount is null ? null : (decimal)discount };
        p.RecalculateEffectivePrice();
        Assert.Equal(expected, p.EffectivePrice);
    }
}

public class ValidatorTests
{
    private readonly ProductListQueryValidator _list = new();

    [Fact]
    public void ProductListQuery_Defaults_AreValid() => Assert.True(_list.Validate(new ProductListQuery()).IsValid);

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 61)]
    [InlineData(-3, 20)]
    public void ProductListQuery_RejectsBadPaging(int page, int size) =>
        Assert.False(_list.Validate(new ProductListQuery { Page = page, PageSize = size }).IsValid);

    [Fact]
    public void ProductListQuery_RejectsInvertedPriceRangeAndUnknownSort()
    {
        Assert.False(_list.Validate(new ProductListQuery { MinPrice = 500, MaxPrice = 100 }).IsValid);
        Assert.False(_list.Validate(new ProductListQuery { Sort = "cheapest" }).IsValid);
        Assert.True(_list.Validate(new ProductListQuery { Sort = "PRICE_ASC" }).IsValid);
    }

    [Fact]
    public void ProductListQuery_RejectsMalformedSpec() =>
        Assert.False(_list.Validate(new ProductListQuery { Spec = ["Socket"] }).IsValid);

    [Fact]
    public void SearchQuery_RequiresQ()
    {
        var v = new SearchQueryValidator();
        Assert.False(v.Validate(new SearchQuery()).IsValid);
        Assert.False(v.Validate(new SearchQuery { Q = "a" }).IsValid);
        Assert.True(v.Validate(new SearchQuery { Q = "ryzen" }).IsValid);
    }

    [Fact]
    public void Autocomplete_ValidatesLengthAndLimit()
    {
        var v = new AutocompleteQueryValidator();
        Assert.False(v.Validate(new AutocompleteQuery("a")).IsValid);
        Assert.False(v.Validate(new AutocompleteQuery("ab", 50)).IsValid);
        Assert.True(v.Validate(new AutocompleteQuery("ab", 5)).IsValid);
    }

    [Theory]
    [InlineData("Rahim Uddin", "rahim@example.com", "01712345678", "Passw0rdX", true)]
    [InlineData("Rahim Uddin", "rahim@example.com", "+8801912345678", "Passw0rdX", true)]
    [InlineData("Rahim Uddin", "rahim@example.com", null, "Passw0rdX", true)]
    [InlineData("", "rahim@example.com", null, "Passw0rdX", false)]
    [InlineData("Rahim", "not-an-email", null, "Passw0rdX", false)]
    [InlineData("Rahim", "rahim@example.com", "12345", "Passw0rdX", false)]
    [InlineData("Rahim", "rahim@example.com", null, "short1A", false)]
    [InlineData("Rahim", "rahim@example.com", null, "alllowercase1", false)]
    [InlineData("Rahim", "rahim@example.com", null, "NoDigitsHere", false)]
    public void RegisterRequest(string name, string email, string? phone, string password, bool valid) =>
        Assert.Equal(valid, new RegisterRequestValidator().Validate(new RegisterRequest(name, email, phone, password)).IsValid);
}

public class TextSearchTests
{
    [Theory]
    [InlineData("ryzen", "%ryzen%")]
    [InlineData("50%", "%50\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("[x]", "%\\[x]%")]
    public void Contains_EscapesWildcards(string term, string expected) => Assert.Equal(expected, TextSearch.Contains(term));

    [Fact]
    public async Task WildcardInput_DoesNotMatchEverything()
    {
        using var db = new TechBazar.UnitTests.Support.TestDatabase();
        using var scope = db.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IProductService>();
        var r = await svc.GetProductsAsync(new ProductListQuery { Q = "%" });
        Assert.Equal(0, r.TotalCount);
    }
}
