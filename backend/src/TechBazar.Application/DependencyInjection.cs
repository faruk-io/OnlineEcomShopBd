using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechBazar.Application.Admin;
using TechBazar.Application.Catalog;
using TechBazar.Application.Orders;
using TechBazar.Application.Payments;
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
        services.AddScoped<IAddressService, AddressService>();
        services.AddScoped<IOrderFulfilment, OrderFulfilment>();
        services.AddScoped<OrderReader>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<TechBazar.Application.PcBuilder.IPcBuilderService, TechBazar.Application.PcBuilder.PcBuilderService>();
        services.AddScoped<IAdminProductService, AdminProductService>();
        services.AddScoped<IAdminCatalogService, AdminCatalogService>();
        services.AddScoped<IAdminCouponService, AdminCouponService>();
        services.AddScoped<IAdminOrderService, AdminOrderService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
