using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TechBazar.Application.Catalog;
using TechBazar.Application.Shopping;

namespace TechBazar.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<ProductListQuery>(ServiceLifetime.Singleton);
        services.AddScoped<ICategoryHierarchy, CategoryHierarchy>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IWishlistService, WishlistService>();
        return services;
    }
}
