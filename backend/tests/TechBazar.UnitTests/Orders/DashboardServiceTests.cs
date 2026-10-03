using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Admin;
using TechBazar.Domain.Enums;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Orders;

/// <summary>Pins the dashboard numbers (sales window, exclusions, grouping, top products) independent of how they are computed.</summary>
public class DashboardServiceTests
{
    /// <summary>Places an order for the product, then back-dates / re-statuses it directly in the database.</summary>
    private static async Task<(string Number, decimal Total)> OrderAsync(Scenario s, string product, int qty, int daysAgo, OrderStatus status)
    {
        await s.AddToCart(product, qty);
        var placed = await s.Place();
        var when = DateTime.UtcNow.Date.AddDays(-daysAgo).AddHours(11);
        // direct updates: LoadOrder() returns untracked entities, and SaveChanges would restamp audit columns anyway
        await s.Ctx.Orders.Where(o => o.OrderNumber == placed.Order.OrderNumber)
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.CreatedAt, when).SetProperty(o => o.Status, status));
        s.Ctx.ChangeTracker.Clear();
        return (placed.Order.OrderNumber, placed.Order.GrandTotal);
    }

    [Fact]
    public async Task SalesWindow_ExcludesCancelledReturnedAndOldOrders_AndGroupsByDay()
    {
        using var s = await Scenario.CreateAsync();
        var today1 = await OrderAsync(s, "Ryzen 5 5600 Processor", 2, 0, OrderStatus.Confirmed);
        var today2 = await OrderAsync(s, "Core i5-12400F", 1, 0, OrderStatus.Pending);
        var threeAgo = await OrderAsync(s, "Ryzen 5 5600 Processor", 1, 3, OrderStatus.Delivered);
        await OrderAsync(s, "Core i5-12400F", 1, 1, OrderStatus.Cancelled);   // excluded: cancelled
        await OrderAsync(s, "Core i5-12400F", 1, 2, OrderStatus.Returned);    // excluded: returned
        await OrderAsync(s, "Core i5-12400F", 1, 40, OrderStatus.Delivered);  // outside a 30 day window

        var d = await s.Get<IDashboardService>().GetAsync(30, 5);

        var expectedRevenue = today1.Total + today2.Total + threeAgo.Total;
        Assert.Equal(3, d.Orders);
        Assert.Equal(expectedRevenue, d.Revenue);
        Assert.Equal(Math.Round(expectedRevenue / 3, 2), d.AverageOrderValue);
        Assert.Equal(2, d.OrdersToday);
        Assert.Equal(today1.Total + today2.Total, d.RevenueToday);

        Assert.Equal(30, d.SalesByDay.Count);                                   // one point per day, zero-filled
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.Date), d.SalesByDay[^1].Date);
        Assert.Equal((2, today1.Total + today2.Total), (d.SalesByDay[^1].Orders, d.SalesByDay[^1].Revenue));
        Assert.Equal((1, threeAgo.Total), (d.SalesByDay[^4].Orders, d.SalesByDay[^4].Revenue));
        Assert.Equal(expectedRevenue, d.SalesByDay.Sum(x => x.Revenue));
        Assert.Equal(0, d.SalesByDay[^2].Orders);                               // yesterday: only the cancelled order -> zero-filled
    }

    [Fact]
    public async Task StatusCounts_CoverAllOrdersEver_NotJustTheWindow()
    {
        using var s = await Scenario.CreateAsync();
        await OrderAsync(s, "Ryzen 5 5600 Processor", 1, 0, OrderStatus.Pending);
        await OrderAsync(s, "Ryzen 5 5600 Processor", 1, 0, OrderStatus.Pending);
        await OrderAsync(s, "Ryzen 5 5600 Processor", 1, 200, OrderStatus.Delivered);
        await OrderAsync(s, "Ryzen 5 5600 Processor", 1, 5, OrderStatus.Cancelled);

        var d = await s.Get<IDashboardService>().GetAsync(7, 5);
        var counts = d.OrdersByStatus.ToDictionary(x => x.Status, x => x.Count);
        Assert.Equal(2, counts[OrderStatus.Pending]);
        Assert.Equal(1, counts[OrderStatus.Delivered]);
        Assert.Equal(1, counts[OrderStatus.Cancelled]);
        Assert.Equal(2, d.PendingOrders);
        Assert.Equal(0, d.Orders - 2);   // only the two pending orders of the last 7 days count as sales
    }

    [Fact]
    public async Task TopProducts_AreRankedByRevenueFromSalesInTheWindowOnly()
    {
        using var s = await Scenario.CreateAsync();
        var ryzenUnit = s.Product("Ryzen 5 5600 Processor").EffectivePrice;
        await OrderAsync(s, "Ryzen 5 5600 Processor", 3, 0, OrderStatus.Confirmed);
        await OrderAsync(s, "Ryzen 5 5600 Processor", 1, 2, OrderStatus.Confirmed);
        await OrderAsync(s, "Core i5-12400F", 1, 1, OrderStatus.Confirmed);
        await OrderAsync(s, "Core i5-12400F", 9, 1, OrderStatus.Cancelled);    // cancelled sales never count
        await OrderAsync(s, "Core i5-12400F", 9, 60, OrderStatus.Confirmed);   // outside the window

        var d = await s.Get<IDashboardService>().GetAsync(30, 5);
        Assert.Equal(2, d.TopProducts.Count);
        Assert.Equal(("AMD Ryzen 5 5600 Processor", 4, ryzenUnit * 4), (d.TopProducts[0].Name, d.TopProducts[0].Quantity, d.TopProducts[0].Revenue));
        Assert.Equal(1, d.TopProducts[1].Quantity);
        Assert.True(d.TopProducts[0].Revenue >= d.TopProducts[1].Revenue);
    }

    [Fact]
    public async Task LowStock_ListsActiveInStockProductsAtOrBelowTheThreshold_LowestFirst()
    {
        using var s = await Scenario.CreateAsync();
        s.Product("Ryzen 5 5600 Processor").StockQuantity = 1;
        s.Product("Core i5-12400F").StockQuantity = 2;
        await s.Ctx.SaveChangesAsync();
        var d = await s.Get<IDashboardService>().GetAsync(30, 5);
        Assert.Equal(5, d.LowStockThreshold);
        Assert.Equal(["AMD Ryzen 5 5600 Processor", "Intel Core i5-12400F 12th Gen Processor"], d.LowStock.Select(p => p.Name).Take(2));
        Assert.All(d.LowStock, p => Assert.True(p.StockQuantity <= 5));
        Assert.True(d.LowStock.Zip(d.LowStock.Skip(1)).All(x => x.First.StockQuantity <= x.Second.StockQuantity));
    }

    [Fact]
    public async Task RecentOrders_AreNewestFirst_AndCapped()
    {
        using var s = await Scenario.CreateAsync();
        for (var i = 0; i < 10; i++) await OrderAsync(s, "Core i5-12400F", 1, 10 - i, OrderStatus.Confirmed);
        var d = await s.Get<IDashboardService>().GetAsync(30, 5);
        Assert.Equal(8, d.RecentOrders.Count);
        Assert.True(d.RecentOrders.Zip(d.RecentOrders.Skip(1)).All(x => x.First.CreatedAt >= x.Second.CreatedAt));
    }

    [Fact]
    public async Task EmptyStore_ReturnsZeroedKpisAndAFullSeries()
    {
        using var s = await Scenario.CreateAsync();
        var d = await s.Get<IDashboardService>().GetAsync(7, 5);
        Assert.Equal((0, 0m, 0m, 0), (d.Orders, d.Revenue, d.AverageOrderValue, d.OrdersToday));
        Assert.Equal(7, d.SalesByDay.Count);
        Assert.Empty(d.TopProducts);
        Assert.Empty(d.RecentOrders);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(100000, 365)]
    public async Task TheWindowIsClamped(int requested, int expectedDays)
    {
        using var s = await Scenario.CreateAsync();
        var d = await s.Get<IDashboardService>().GetAsync(requested, 5);
        Assert.Equal(expectedDays, d.SalesByDay.Count);
    }
}
