using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechBazar.Application.Orders;
using TechBazar.Application.Shopping;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;
using TechBazar.Infrastructure.Identity;
using TechBazar.Infrastructure.Persistence;

namespace TechBazar.UnitTests.Support;

/// <summary>A signed-in shopper on a fresh seeded database: add to cart, add addresses, check out, then inspect rows.</summary>
public sealed class Scenario : IDisposable
{
    public TestDatabase Db { get; }
    public IServiceScope Scope { get; }
    public Guid UserId { get; private set; }
    public string Email { get; private set; } = "";
    public T Get<T>() where T : notnull => Scope.ServiceProvider.GetRequiredService<T>();
    public ApplicationDbContext Ctx => Get<ApplicationDbContext>();

    private Scenario(Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configure)
    {
        Db = configure is null ? new TestDatabase() : TestDatabase.With(configure);
        Scope = Db.CreateScope();
    }

    public static async Task<Scenario> CreateAsync(string email = "rahim@example.com", Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configure = null)
    {
        var s = new Scenario(configure);
        (s.UserId, s.Email) = await s.NewUserAsync(email);
        return s;
    }

    public async Task<(Guid, string)> NewUserAsync(string email)
    {
        var users = Get<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, FullName = "Rahim Uddin", CreatedAt = DateTime.UtcNow };
        var r = await users.CreateAsync(user, "Passw0rd!");
        Assert.True(r.Succeeded, string.Join(",", r.Errors.Select(e => e.Description)));
        return (user.Id, email);
    }

    public int ProductId(string nameFragment) => Ctx.Products.Where(p => p.Name.Contains(nameFragment)).Select(p => p.Id).First();
    public Product Product(string nameFragment) => Ctx.Products.First(p => p.Name.Contains(nameFragment));

    public Task AddToCart(string nameFragment, int qty = 1, Guid? user = null) =>
        Get<ICartService>().SetItemAsync(user ?? UserId, ProductId(nameFragment), qty);

    public Task<AddressDto> AddAddress(string district = "Dhaka", string division = "Dhaka", Guid? user = null) =>
        Get<IAddressService>().CreateAsync(user ?? UserId, new SaveAddressRequest("Home", "Rahim Uddin", "01712345678", division, district, null, "House 1, Road 2", "1207", false));

    public async Task<PlaceOrderResult> Place(PaymentMethod payment = PaymentMethod.CashOnDelivery, ShippingMethod shipping = ShippingMethod.HomeDeliveryInsideDhaka,
        string? coupon = null, int? addressId = null, Guid? user = null, string? email = null, string? contactName = null, string? contactPhone = null)
    {
        var id = addressId;
        if (id is null && shipping != ShippingMethod.StorePickup)
            id = (await AddAddress(shipping == ShippingMethod.HomeDeliveryOutsideDhaka ? "Chattogram" : "Dhaka", shipping == ShippingMethod.HomeDeliveryOutsideDhaka ? "Chattogram" : "Dhaka", user)).Id;
        return await Get<ICheckoutService>().PlaceAsync(user ?? UserId, email ?? Email,
            new PlaceOrderRequest(shipping, id, contactName, contactPhone, payment, coupon, null));
    }

    public async Task<Order> LoadOrder(string number) =>
        await Ctx.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.History).Include(o => o.Payments).SingleAsync(o => o.OrderNumber == number);

    public void Dispose()
    {
        Scope.Dispose();
        Db.Dispose();
    }
}
