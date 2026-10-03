using System.Linq.Expressions;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Domain.Entities;

namespace TechBazar.Application.Catalog;

internal static class ProductProjections
{
    public static readonly Expression<Func<Product, ProductListItemDto>> ListItem = p => new ProductListItemDto
    {
        Id = p.Id,
        Name = p.Name,
        Slug = p.Slug,
        Sku = p.Sku,
        Price = p.Price,
        DiscountPrice = p.DiscountPrice,
        EffectivePrice = p.EffectivePrice,
        StockStatus = p.StockStatus,
        ImageUrl = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault(),
        BrandName = p.Brand.Name,
        BrandSlug = p.Brand.Slug,
        CategoryName = p.Category.Name,
        CategorySlug = p.Category.Slug,
        RatingAverage = p.RatingAverage,
        ReviewCount = p.ReviewCount,
        WarrantyMonths = p.WarrantyMonths,
        KeyFeatures = p.KeyFeatures.OrderBy(k => k.DisplayOrder).Select(k => k.Text).Take(3).ToList(),
    };
}
