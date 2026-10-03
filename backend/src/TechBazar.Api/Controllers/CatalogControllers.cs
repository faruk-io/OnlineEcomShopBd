using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using TechBazar.Api.Extensions;
using TechBazar.Application.Catalog;
using TechBazar.Application.Catalog.Dtos;
using TechBazar.Application.Catalog.Validators;
using TechBazar.Application.Common;

namespace TechBazar.Api.Controllers;

[OutputCache(PolicyName = Policies.CatalogCache)]
public sealed class ProductsController(IProductService products) : ApiControllerBase
{
    /// <summary>List products with filtering, sorting and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ProductListQuery query, CancellationToken ct) =>
        Ok(await products.GetProductsAsync(query, ct));

    /// <summary>Filter facets (brands, price range, spec values) for a category.</summary>
    [HttpGet("facets")]
    public async Task<ActionResult<ProductFacetsDto>> Facets([FromQuery] string? category, CancellationToken ct) =>
        Ok(await products.GetFacetsAsync(category, ct));

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(ProductDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDetailDto>> Get(string slug, CancellationToken ct) =>
        Ok(await products.GetBySlugAsync(slug, ct));

    [HttpGet("{slug}/related")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Related(string slug, [FromQuery] int count = 8, CancellationToken ct = default) =>
        Ok(await products.GetRelatedAsync(slug, Math.Clamp(count, 1, 20), ct));
}

[OutputCache(PolicyName = Policies.CatalogCache)]
public sealed class CategoriesController(ICategoryService categories) : ApiControllerBase
{
    /// <summary>Full category tree with product counts.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryTreeNodeDto>>> Tree(CancellationToken ct) =>
        Ok(await categories.GetTreeAsync(ct));

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDetailDto>> Get(string slug, CancellationToken ct) =>
        Ok(await categories.GetBySlugAsync(slug, ct));
}

[OutputCache(PolicyName = Policies.CatalogCache)]
public sealed class BrandsController(IBrandService brands) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BrandListItemDto>>> List(CancellationToken ct) =>
        Ok(await brands.GetAllAsync(ct));
}

public sealed class SearchController(IProductService products, ISearchService search) : ApiControllerBase
{
    /// <summary>Full search results (same filters/sort/paging as /products, <c>q</c> required).</summary>
    [HttpGet]
    [OutputCache(PolicyName = Policies.CatalogCache)]
    public async Task<ActionResult<PagedResult<ProductListItemDto>>> Search([FromQuery] SearchQuery query, CancellationToken ct) =>
        Ok(await products.GetProductsAsync(query, ct));

    /// <summary>Type-ahead suggestions: top products, categories and brands.</summary>
    [HttpGet("autocomplete")]
    [OutputCache(PolicyName = Policies.AutocompleteCache)]
    public async Task<ActionResult<AutocompleteResultDto>> Autocomplete([FromQuery] AutocompleteQuery query, CancellationToken ct) =>
        Ok(await search.AutocompleteAsync(query.Q!, query.Limit, ct));
}
