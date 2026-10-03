using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Common;

namespace TechBazar.Application.Catalog;

public interface IProductService
{
    Task<PagedResult<ProductListItemDto>> GetProductsAsync(ProductListQuery query, CancellationToken ct = default);
    Task<ProductDetailDto> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<ProductListItemDto>> GetRelatedAsync(string slug, int count, CancellationToken ct = default);
    Task<ProductFacetsDto> GetFacetsAsync(string? categorySlug, CancellationToken ct = default);
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryTreeNodeDto>> GetTreeAsync(CancellationToken ct = default);
    Task<CategoryDetailDto> GetBySlugAsync(string slug, CancellationToken ct = default);
}

public interface IBrandService
{
    Task<IReadOnlyList<BrandListItemDto>> GetAllAsync(CancellationToken ct = default);
}

public interface ISearchService
{
    Task<AutocompleteResultDto> AutocompleteAsync(string query, int limit, CancellationToken ct = default);
}
